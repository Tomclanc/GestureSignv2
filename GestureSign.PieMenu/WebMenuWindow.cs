using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using GestureSign.Foundation.Diagnostics;

namespace GestureSign.PieMenu;

internal sealed class WebMenuWindow : IDisposable
{
    private const string Origin = "https://pie-menu.gesturesign.local/";
    private const string ClassName = "GestureSign.PieMenu.WebOverlay";
    private static readonly WindowProc Procedure = Dispatch;
    private static readonly Dictionary<nint, WebMenuWindow> Windows = [];
    private static ushort _class;
    private static Task<CoreWebView2Environment>? _environment;
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private readonly Action<Exception> _report;
    private readonly Action<int, PieStage, double?, int?> _diagnostic;
    private readonly string _configuration;
    private readonly bool _animations;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _initialization;
    private long _nextSession;
    private Session? _session;
    private CoreWebView2CompositionController? _web;
    private CompositionTarget? _target;
    private nint _hwnd;
    private double _scale = 1;
    private int _left, _top, _width, _height;
    private bool _shown, _disposed, _pendingSelection, _presented;
    private uint _mouseButtons;
    private bool _trackingMouse;
    internal event Action? Closed;

    private sealed record Session(long Id, PieMenu Menu, nint Foreground, Func<PieItem, nint, Task> Execute,
        Action Settings, long Started, bool Warm, int DiagnosticAttempt);

    private readonly int _fadeOut;
    private readonly double _zoom;
    internal bool IsDisposed => _disposed;
    internal bool IsReady => !_disposed && _ready.Task.IsCompletedSuccessfully;

    internal WebMenuWindow(MenuDocument document, MenuLanguage language, Action<Exception> report,
        Action<int, PieStage, double?, int?> diagnostic)
    {
        _configuration = Configuration(document, language);
        _animations = document.Animations;
        _fadeOut = (int)Math.Clamp(document.Advanced.FadeOutDuration, 0, 1000);
        _zoom = Math.Clamp(document.Advanced.ZoomFactor, .5, 4);
        _report = report;
        _diagnostic = diagnostic;
    }

    internal bool Matches(MenuDocument document, MenuLanguage language) =>
        !_disposed && _configuration == Configuration(document, language);

    private static string Configuration(MenuDocument document, MenuLanguage language)
    {
        var background = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
        bool dark = document.Advanced.EnableDarkModeForMenuThemes && background.R + background.G + background.B < 384;
        return JsonSerializer.Serialize(new
    {
        advanced = JsonSerializer.SerializeToElement(document.Advanced, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        animations = document.Animations, marking = document.Marking, palette = dark ? document.DarkPalette : document.Palette,
        turbo = document.Turbo, confirmHover = document.ConfirmHover, warpPointer = document.WarpPointer,
        closeOnCenterRelease = document.CloseOnCenterRelease, rightButton = document.RightButton,
        middleButton = document.MiddleButton, theme = dark ? document.DarkWebTheme : document.WebTheme, language = language.RendererTag
        });
    }

    internal void Inspect()
    {
        if (_web?.CoreWebView2 is not { } core) return;
        core.Settings.AreDevToolsEnabled = true;
        core.OpenDevToolsWindow();
    }

    internal Task PrepareAsync() => _initialization ??= InitializeAsync();

    internal async Task ShowAsync(PieMenu menu, Func<PieItem, nint, Task> execute, Action settings, int diagnosticAttempt)
    {
        var session = new Session(++_nextSession, menu, Desktop.GetForegroundWindow(), execute,
            settings, Stopwatch.GetTimestamp(), _ready.Task.IsCompletedSuccessfully, diagnosticAttempt);
        _session = session;
        _presented = false;
        var icons = ResolveSystemIcons(menu);
        await PrepareAsync();
        var systemIcons = await icons;
        if (!ReferenceEquals(_session, session) || _disposed) return;
        _diagnostic(diagnosticAttempt, PieStage.RendererReady, null, null);
        Present(session, systemIcons);
    }

    private async Task InitializeAsync()
    {
        var content = Path.Combine(AppContext.BaseDirectory, "KandoWeb");
        if (!File.Exists(Path.Combine(content, "renderer.js")))
            throw new FileNotFoundException("The Kando renderer is missing. Extract the entire component package.");
        var owner = GetModuleHandle(null);
        if (_class == 0)
        {
            var windowClass = new WndClass
            {
                Name = ClassName, Instance = owner, Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Cursor = LoadCursor(0, (nint)32512)
            };
            _class = RegisterClass(ref windowClass);
            if (_class == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Position();
        const uint extended = 0x00000008u | 0x00000080u | 0x00200000u; // Topmost, tool window, no redirection bitmap.
        _hwnd = CreateWindowEx(extended, ClassName, "GestureSign · Kando renderer", 0x80000000,
            _left, _top, _width, _height, 0, 0, owner, 0);
        if (_hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Windows.Add(_hwnd, this);
        var data = Path.Combine(Path.GetDirectoryName(MenuStorage.PathName)!, "WebView2");
        if (_environment is null || _environment.IsFaulted)
            _environment = CoreWebView2Environment.CreateAsync(userDataFolder: data);
        var environment = await _environment;
        if (_disposed) return;
        _target = new CompositionTarget(_hwnd);
        var controller = await environment.CreateCoreWebView2CompositionControllerAsync(_hwnd);
        if (_disposed)
        {
            controller.Close();
            return;
        }
        _web = controller;
        _web.IsVisible = true;
        _web.DefaultBackgroundColor = Color.Transparent;
        _web.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        _web.Bounds = new Rectangle(0, 0, _width, _height);
        _web.RootVisualTarget = _target.RootVisual;
        _web.ShouldDetectMonitorScaleChanges = false;
        UpdateRasterizationScale();
        _target.Commit();

        var core = _web.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping("pie-menu.gesturesign.local", content, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) => e.Cancel = !AllowedOrigin(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += Receive;
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                _environment = null;
            Fail(new InvalidOperationException("Menu renderer stopped: " + e.ProcessFailedKind));
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__gestureSignConfig=" + _configuration + ";");
        if (_disposed) return;
        core.Navigate(Origin + "index.html");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!_disposed && _session is null) controller.IsVisible = false;
    }

    private void Position()
    {
        Desktop.GetCursorPos(out var cursor);
        var (area, scale) = Desktop.MonitorAt(cursor);
        (_left, _top, _width, _height, _scale) = (area.X, area.Y, area.Width, area.Height, scale);
    }

    private void UpdateRasterizationScale()
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi != 0) _scale = dpi / 96d;
        if (_web is { } web && web.RasterizationScale != _scale)
            web.RasterizationScale = _scale;
    }

    private static bool AllowedOrigin(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var url) &&
        url.Scheme == "https" && url.Host == "pie-menu.gesturesign.local" && url.IsDefaultPort && url.UserInfo.Length == 0;

    private void Send(object value) => _web?.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));

    private static async Task<Dictionary<string, string>> ResolveSystemIcons(PieMenu menu)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(menu.SourceIconTheme, menu.SourceIcon);
        foreach (var item in menu.Items) Visit(item);
        var icons = await Task.WhenAll(names.Select(async id => (Id: id, Image: await WindowsApplicationIcons.ResolveImportedAsync(id))));
        return icons.Where(entry => entry.Image is not null)
            .ToDictionary(entry => entry.Id, entry => entry.Image!, StringComparer.OrdinalIgnoreCase);

        void Add(string? theme, string? icon)
        {
            if (theme == MenuIcons.System && !string.IsNullOrEmpty(icon)) names.Add(icon);
        }

        void Visit(PieItem item)
        {
            Add(item.SourceIconTheme, item.SourceIcon);
            foreach (var child in item.Children) Visit(child);
        }
    }

    private static (string Icon, string Theme) RenderIcon(string? icon, string? theme, string glyph,
        IReadOnlyDictionary<string, string> systemIcons)
    {
        if (theme == MenuIcons.System)
            return icon is not null && systemIcons.TryGetValue(icon, out var data)
                ? (data, MenuIcons.Image) : ("file-item.svg", MenuIcons.Kando);
        return (icon ?? glyph, theme ?? MenuIcons.Emoji);
    }

    private void Present(Session session, IReadOnlyDictionary<string, string> systemIcons)
    {
        if (_web is null) return;
        var oldArea = (_left, _top, _width, _height);
        Position();
        if (oldArea != (_left, _top, _width, _height))
        {
            SetWindowPos(_hwnd, 0, _left, _top, _width, _height, 0x14); // Keep z-order and activation.
            _web.Bounds = new Rectangle(0, 0, _width, _height);
            _web.NotifyParentWindowPositionChanged();
        }
        UpdateRasterizationScale();
        _web.IsVisible = true;
        Desktop.GetCursorPos(out var cursor);
        var menu = session.Menu;
        var artwork = RenderIcon(menu.SourceIcon, menu.SourceIconTheme, menu.Glyph, systemIcons);
        var root = new
        {
            type = "root", name = menu.Name,
            icon = artwork.Icon,
            iconTheme = artwork.Theme,
            activateWorkflow = new { quickSelectKey = menu.CenterKey, actions = Array.Empty<object>() },
            children = menu.Items.Select(item => RenderItem(item, systemIcons)).ToArray()
        };
        Send(new
        {
            type = "show", sessionId = session.Id, root,
            options = new
            {
                mousePosition = new { x = (cursor.X - _left) / _scale, y = (cursor.Y - _top) / _scale },
                windowSize = new { x = _width / _scale, y = _height / _scale },
                zoomFactor = _zoom, useFixedPosition = menu.HasFixedPosition,
                fixedMenuPosition = new { x = Math.Clamp(menu.FixedX ?? .5, 0, 1), y = Math.Clamp(menu.FixedY ?? .5, 0, 1) },
                anchoredMode = menu.Anchored, hoverMode = menu.Hover, systemIconsChanged = false
            }
        });
        _shown = true;
        ShowWindow(_hwnd, 5);
        Desktop.SetForegroundWindow(_hwnd);
        _web.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    private static object RenderItem(PieItem item, IReadOnlyDictionary<string, string> systemIcons)
    {
        var artwork = RenderIcon(item.SourceIcon, item.SourceIconTheme, item.Glyph, systemIcons);
        var value = new Dictionary<string, object?>
        {
            ["type"] = item.Submenu || item.Children.Count > 0 ? "submenu" : "button",
            ["name"] = item.Name,
            ["icon"] = artwork.Icon,
            ["iconTheme"] = artwork.Theme
        };
        if (item.Angle is { } angle && double.IsFinite(angle)) value["angle"] = angle;
        if (item.Submenu || item.Children.Count > 0)
        {
            value["children"] = item.Children.Select(child => RenderItem(child, systemIcons)).ToArray();
            value["openWorkflow"] = new { quickSelectKey = item.Key, actions = Array.Empty<object>() };
            value["activateWorkflow"] = new { quickSelectKey = "Backspace", actions = Array.Empty<object>() };
        }
        else value["selectWorkflow"] = new { quickSelectKey = item.Key, actions = Array.Empty<object>() };
        return value;
    }

    private void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || !AllowedOrigin(e.Source)) return;
        try
        {
            var raw = e.WebMessageAsJson;
            if (raw.Length > 16384) return;
            using var json = JsonDocument.Parse(raw);
            var message = json.RootElement;
            var type = message.GetProperty("type").GetString();
            if (type == "ready") { _ready.TrySetResult(); return; }
            if (type == "error")
            {
                Fail(new InvalidOperationException(message.GetProperty("message").GetString()));
                return;
            }
            if (!_shown || _session is not { } session ||
                !message.TryGetProperty("sessionId", out var id) || !id.TryGetInt64(out var number) || number != session.Id)
                return;

            switch (type)
            {
                case "presented":
                    var elapsed = Stopwatch.GetElapsedTime(session.Started).TotalMilliseconds;
                    _presented = true;
                    _diagnostic(session.DiagnosticAttempt, PieStage.Presented, Math.Round(elapsed, 1), null);
                    var rasterScale = message.TryGetProperty("pixelRatio", out var ratio) && ratio.TryGetDouble(out var actual)
                        ? actual : _scale;
                    Trace(FormattableString.Invariant($"overlay presented: {(session.Warm ? "warm" : "cold")} {elapsed:F1} ms, scale {_scale:F2}, WebView {rasterScale:F2}"));
                    break;
                case "settings":
                    Close(restore: false);
                    session.Settings();
                    break;
                case "movePointer":
                    if (message.TryGetProperty("dist", out var dist) &&
                        dist.GetProperty("x").TryGetDouble(out var dx) && dist.GetProperty("y").TryGetDouble(out var dy) &&
                        double.IsFinite(dx) && double.IsFinite(dy) &&
                        Math.Abs(dx) <= _width / _scale && Math.Abs(dy) <= _height / _scale)
                    {
                        Desktop.GetCursorPos(out var cursor);
                        SetCursorPos(Math.Clamp(cursor.X + (int)Math.Round(dx * _scale), _left, _left + _width - 1),
                            Math.Clamp(cursor.Y + (int)Math.Round(dy * _scale), _top, _top + _height - 1));
                    }
                    break;
                case "interaction":
                    Interact(session, message);
                    break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Interact(Session session, JsonElement message)
    {
        var interaction = message.GetProperty("interaction").GetString();
        if (interaction is "openSubmenu" or "closeSubmenu" or "closeMenu" or "selectButton")
            Trace("interaction " + interaction);
        if (_pendingSelection) return;
        if (interaction == "closeMenu")
        {
            _pendingSelection = true;
            _ = FadeAndClose(session, null);
            return;
        }
        if (interaction == "activateMenu")
        {
            if (!message.TryGetProperty("path", out var current) || current.ValueKind != JsonValueKind.Array)
                return;
            if (current.GetArrayLength() > 0)
            {
                Trigger("closeSubmenu");
                return;
            }
            if (session.Menu.CenterActions.Count == 0)
            {
                Trigger("closeMenu");
                return;
            }
            _pendingSelection = true;
            Trigger("closeMenu");
            _ = FadeAndClose(session, new PieItem { Actions = session.Menu.CenterActions });
            return;
        }
        if (interaction != "selectButton" || !message.TryGetProperty("path", out var path) ||
            path.ValueKind != JsonValueKind.Array || path.GetArrayLength() is < 1 or > 40) return;
        IReadOnlyList<PieItem> items = session.Menu.Items;
        PieItem? selected = null;
        foreach (var index in path.EnumerateArray())
        {
            if (!index.TryGetInt32(out var position) || position < 0 || position >= items.Count) return;
            selected = items[position];
            items = selected.Children;
        }
        if (selected is null || selected.Submenu || selected.Children.Count > 0) return;
        if (selected.Actions.Count == 1 && selected.Actions[0].Type == "close-submenu")
        {
            Trigger("closeSubmenu");
            return;
        }
        _pendingSelection = true;
        Trigger("closeMenu");
        _ = FadeAndClose(session, selected);
    }

    private void Trigger(string interaction)
    {
        if (_session is { } session) Send(new { type = "trigger", sessionId = session.Id, interaction });
    }

    private async Task FadeAndClose(Session session, PieItem? action)
    {
        await Task.Delay(_animations ? _fadeOut + 10 : 20);
        if (!ReferenceEquals(_session, session)) return;
        Close(restore: true);
        if (action is not null) await session.Execute(action, session.Foreground);
    }

    internal void Close(bool restore = false)
    {
        if (_session is not { } session) return;
        _session = null;
        var wasShown = _shown;
        _shown = _pendingSelection = _trackingMouse = false;
        if (_mouseButtons != 0)
        {
            _mouseButtons = 0;
            if (GetCapture() == _hwnd) ReleaseCapture();
        }
        if (wasShown && _hwnd != 0)
        {
            ShowWindow(_hwnd, 0);
            if (!_disposed && _web is not null)
            {
                Send(new { type = "reset", sessionId = session.Id });
                _web.IsVisible = false;
            }
        }
        if (restore && wasShown && session.Foreground != 0) Desktop.SetForegroundWindow(session.Foreground);
        if (!_presented) _diagnostic(session.DiagnosticAttempt, PieStage.ClosedBeforePresentation, null, null);
        Closed?.Invoke();
    }

    private void Fail(Exception error)
    {
        if (_disposed) return;
        var initialized = IsReady;
        var attempt = _session?.DiagnosticAttempt ?? 0;
        _ready.TrySetException(error);
        Dispose();
        _diagnostic(attempt, PieStage.RendererFailed, null, error.HResult);
        if (initialized) _report(error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ready.TrySetCanceled();
        Close(restore: true);
        if (_web is not null)
        {
            _web.RootVisualTarget = null;
            _web.Close();
            _web = null;
        }
        _target?.Dispose();
        _target = null;
        if (_hwnd != 0)
        {
            Windows.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private static nint Dispatch(nint hwnd, uint msg, nint wp, nint lp)
    {
        if (!Windows.TryGetValue(hwnd, out var menu)) return DefWindowProc(hwnd, msg, wp, lp);
        try
        {
            if (msg == 0x0010) { menu._queue.TryEnqueue(() => menu.Close(restore: true)); return 0; }
            if (msg == 0x0006 && (wp.ToInt64() & 0xffff) == 0 && menu._shown)
            {
                var session = menu._session;
                menu._queue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(menu._session, session)) menu.Close(restore: false);
                });
            }
            if (msg == 0x0100 && wp == (nint)27 && menu._shown) { menu.Trigger("closeMenu"); return 0; }
            if (msg == 0x0014) return 1;
            if (menu._shown && menu._web is not null && menu.ForwardMouse(hwnd, msg, wp, lp)) return 0;
        }
        catch (Exception ex) { menu._queue.TryEnqueue(() => { menu._report(ex); menu.Close(); }); }
        return DefWindowProc(hwnd, msg, wp, lp);
    }

    private static void Trace(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(MenuStorage.PathName)!, "PieMenu.log"),
                $"[{DateTimeOffset.Now:O}] {line}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool ForwardMouse(nint hwnd, uint msg, nint wp, nint lp)
    {
        if (msg is < 0x0200 or > 0x020e && msg != 0x02a3) return false;
        var point = new Desktop.Point { X = (short)(lp.ToInt64() & 0xffff), Y = (short)(lp.ToInt64() >> 16) };
        var wheel = msg is 0x020a or 0x020e;
        if (wheel) ScreenToClient(hwnd, ref point);
        if (msg is 0x0201 or 0x0204 or 0x0207) { _mouseButtons++; SetCapture(hwnd); }
        if ((msg is 0x0202 or 0x0205 or 0x0208) && _mouseButtons > 0 && --_mouseButtons == 0)
            ReleaseCapture();
        if (msg == 0x0200 && !_trackingMouse)
        {
            var track = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = hwnd };
            _trackingMouse = TrackMouseEvent(ref track);
        }
        if (msg == 0x02a3) _trackingMouse = false;
        _web!.SendMouseInput((CoreWebView2MouseEventKind)msg,
            (CoreWebView2MouseEventVirtualKeys)(wp.ToInt64() & 0xffff),
            wheel ? (uint)(wp.ToInt64() >> 16) & 0xffff : 0, new Point(point.X, point.Y));
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        internal uint Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? Menu, Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouse { internal uint Size, Flags; internal nint Window; internal uint HoverTime; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProc(nint hwnd, uint msg, nint wp, nint lp);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WndClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref Desktop.Point point);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouse track);
}
