using Microsoft.UI.Dispatching;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GestureSign.Foundation.Diagnostics;

namespace GestureSign.PieMenu;

public partial class App : Application
{
    private WebMenuWindow? _popup;
    private WebMenuWindow? _renderer;
    private bool _warmQueued;
    private MenuDocument? _document;
    private MenuDocument? _savedDocument;
    private ShortcutHost? _shortcuts;
    private Dictionary<MenuShortcut, List<PieMenu>> _boundMenus = [];
    private string _shortcutStatus = "";
    private bool _recordingShortcut;
    private bool _workflowInhibitsShortcuts;
    private bool _quitting;
    private bool _executing;
    private bool _redirected;
    private string? _hostExecutable;
    private string? _hostConfiguration;
    private (string Path, DateTime Modified, MenuLanguage Language)? _languageCache;
    private LocalDiagnostics.Writer<PieDiagnostic>? _diagnosticWriter;
    private PieDiagnostic _diagnostic = new(DateTimeOffset.UtcNow, DiagnosticProcess.Current);

    public App()
    {
        SetCurrentProcessExplicitAppUserModelID("GestureSign.PieMenu");
        InitializeComponent();
        UnhandledException += (_, e) => Log(e.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var directoryIndex = Array.IndexOf(arguments, "--config-dir");
            if (directoryIndex >= 0)
            {
                if (directoryIndex + 1 >= arguments.Length)
                    throw new ArgumentException("--config-dir requires a folder path.");
                MenuStorage.UseDirectory(arguments[directoryIndex + 1]);
                arguments = arguments.Where((_, index) => index != directoryIndex && index != directoryIndex + 1).ToArray();
            }
            var scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(MenuStorage.PathName.ToUpperInvariant())))[..16];
            var current = AppInstance.GetCurrent();
            var primary = AppInstance.FindOrRegisterForKey("GestureSign.PieMenu." + scope);
            var activated = current.GetActivatedEventArgs();
            if (!primary.IsCurrent)
            {
                _redirected = true;
                try
                {
                    Desktop.AllowSetForegroundWindow(primary.ProcessId);
                    await primary.RedirectActivationToAsync(activated);
                }
                catch (Exception error) { Log(error); }
                finally { Exit(); }
                return;
            }
            var queue = DispatcherQueue.GetForCurrentThread();
            primary.Activated += (_, activation) => queue.TryEnqueue(() =>
            {
                if (activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
                    ActivateCommand(ParseRedirected(launch.Arguments));
            });
            _diagnosticWriter = new(Path.GetDirectoryName(MenuStorage.PathName)!, LocalDiagnostics.PieFile);
            PublishDiagnostic(_diagnostic);
            ActivateCommand(arguments);
        }
        catch (Exception ex)
        {
            Log(ex);
            _document = new MenuDocument();
            Exit();
        }
    }

    private void ActivateCommand(string[] args)
    {
        try
        {
            var hostIndex = Array.IndexOf(args, "--host");
            if (hostIndex >= 0 && hostIndex + 1 < args.Length) _hostExecutable = args[hostIndex + 1];
            var configIndex = Array.IndexOf(args, "--host-config");
            if (configIndex >= 0 && configIndex + 1 < args.Length) _hostConfiguration = args[configIndex + 1];
            if (args.Contains("--quit")) { Quit(); return; }
            var developer = Array.IndexOf(args, "--developer");
            if (developer >= 0 && developer + 1 < args.Length)
            {
                RunDeveloperCommand(args[developer + 1]);
                return;
            }
            var previewIndex = Array.IndexOf(args, "--preview-file");
            if (previewIndex >= 0 && previewIndex + 1 < args.Length)
            {
                ShowPreviewDocument(args[previewIndex + 1], args);
                return;
            }
            if (_document is null)
            {
                _document = MenuStorage.Load(CurrentLanguage());
                if (File.Exists(MenuStorage.PathName))
                    AcceptSavedDocument();
            }
            if (args.Contains("--reload"))
            {
                _document = MenuStorage.Load(CurrentLanguage());
                var status = AcceptSavedDocument();
                var request = Array.IndexOf(args, "--result");
                if (request >= 0 && request + 1 < args.Length &&
                    Guid.TryParseExact(args[request + 1], "N", out var id))
                {
                    var response = Path.Combine(Path.GetDirectoryName(MenuStorage.PathName)!, $".result-{id:N}.txt");
                    File.WriteAllText(response + ".tmp", status);
                    File.Move(response + ".tmp", response, overwrite: true);
                }
                ExitWhenIdle();
                return;
            }
            var recordIndex = Array.IndexOf(args, "--recording");
            if (recordIndex >= 0 && recordIndex + 1 < args.Length)
            {
                SetShortcutRecording(args[recordIndex + 1] == "on");
                ExitWhenIdle();
                return;
            }
            if (args.Contains("--close-menu"))
            {
                _popup?.Close(restore: true);
                ExitWhenIdle();
                return;
            }
            if (args.Contains("--background"))
            {
                if (_shortcuts is null && _boundMenus.Count > 0) AcceptSavedDocument();
                ExitWhenIdle();
                return;
            }
            var triggerIndex = Array.FindIndex(args, arg => arg is "--trigger" or "-t");
            if (triggerIndex >= 0)
            {
                if (triggerIndex + 1 < args.Length && MenuShortcut.TryParse(args[triggerIndex + 1], out var shortcut, out _) && shortcut.Key != 0)
                    HandleShortcut(shortcut, external: true);
                else
                    ExitWhenIdle();
                return;
            }
            var nameIndex = Array.FindIndex(args, arg => arg is "--menu" or "-m");
            if (nameIndex < 0 && !args.Contains("--show"))
            {
                _popup?.Close();
                if (_shortcuts is null && _boundMenus.Count > 0) AcceptSavedDocument();
                ActivateHost();
                ExitWhenIdle();
                return;
            }
            var name = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : null;
            var menu = string.IsNullOrWhiteSpace(name) ? _document.Menus.FirstOrDefault() :
                SelectMenu(_document.Menus.Where(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            if (menu is null) Report(new InvalidOperationException(CurrentLanguage().Text("settings.no-matching-menus")));
            else ShowPopup(menu, _document);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private MenuLanguage CurrentLanguage()
    {
        if (_hostConfiguration is not { Length: > 0 } path)
            return MenuLanguage.FromHostConfiguration(null);
        var modified = File.GetLastWriteTimeUtc(path);
        if (_languageCache is { } cache && cache.Path == path && cache.Modified == modified)
            return cache.Language;
        var language = MenuLanguage.FromHostConfiguration(path);
        _languageCache = (path, modified, language);
        return language;
    }

    private bool ActivateHost()
    {
        if (_hostExecutable is not { Length: > 0 } host) return false;
        using var process = Process.Start(new ProcessStartInfo(host, "--quick-actions") { UseShellExecute = false });
        return process is not null;
    }

    private void Report(Exception error)
    {
        Log(error);
        ExitWhenIdle();
    }

    private void ShowPreviewDocument(string path, string[] args)
    {
        var expectedDirectory = Path.GetDirectoryName(MenuStorage.PathName)!;
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);
        if (!string.Equals(Path.GetDirectoryName(fullPath), expectedDirectory, StringComparison.OrdinalIgnoreCase) ||
            !fileName.StartsWith(".preview-", StringComparison.Ordinal) || !fileName.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid preview document path.");
        MenuDocument preview;
        try { preview = MenuStorage.Read(fullPath); }
        finally { File.Delete(fullPath); }
        var index = Array.IndexOf(args, "--preview-menu");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var menu) || (uint)menu >= preview.Menus.Count)
            throw new InvalidDataException("Invalid preview menu.");
        _popup?.Close();
        ShowPopup(preview.Menus[menu], preview);
    }

    private string AcceptSavedDocument()
    {
        _savedDocument = MenuStorage.Read(MenuStorage.PathName);
        _boundMenus = [];
        foreach (var menu in _savedDocument.Menus)
        {
            if (string.IsNullOrWhiteSpace(menu.Shortcut) || !MenuShortcut.TryParse(menu.Shortcut, out var shortcut, out _)) continue;
            if (!_boundMenus.TryGetValue(shortcut, out var list)) _boundMenus.Add(shortcut, list = []);
            list.Add(menu);
        }
        if (_boundMenus.Count == 0)
        {
            _shortcuts?.Dispose();
            _shortcuts = null;
        }
        else
        {
            _shortcuts ??= new ShortcutHost(HandleShortcut);
            QueueRendererWarmup();
        }
        return RefreshBindings();
    }

    private string RefreshBindings()
    {
        var locale = CurrentLanguage();
        var warnings = (_savedDocument?.Menus ?? []).Where(menu => !MenuShortcut.TryParse(menu.Shortcut, out _, out _))
            .Select(menu => $"{menu.Name}: {locale.Text("native.shortcut-invalid")} ({menu.Shortcut})").ToList();
        var paused = _recordingShortcut || _workflowInhibitsShortcuts;
        var configured = _boundMenus.Count;
        Dictionary<MenuShortcut, int> failures = [];
        if (_shortcuts is { } host)
        {
            failures = host.Bind(paused ? [] : _boundMenus.Keys);
            foreach (var (shortcut, error) in failures)
            {
                var name = _boundMenus[shortcut][0].Shortcut;
                var key = error is 1409 ? "native.shortcut-in-use" : "native.shortcut-unavailable";
                warnings.Add($"{name}: {locale.Text(key)} ({error})");
            }
        }
        PublishDiagnostic(_diagnostic with
        {
            ConfiguredShortcuts = configured, RegisteredShortcuts = _shortcuts is null || paused ? 0 : configured - failures.Count,
            ShortcutConflicts = failures.Values.Count(error => error == 1409),
            InvalidShortcuts = warnings.Count - failures.Count,
            RegistrationError = failures.Count == 0 ? null : failures.Values.First(),
            BindingsPaused = paused
        });
        return _shortcutStatus = string.Join(Environment.NewLine, warnings);
    }

    private void SetShortcutRecording(bool recording)
    {
        _recordingShortcut = recording;
        RefreshBindings();
    }

    private void HandleShortcut(MenuShortcut shortcut) => HandleShortcut(shortcut, external: false);

    private void HandleShortcut(MenuShortcut shortcut, bool external)
    {
        var attempt = _diagnostic.TriggerCount + 1;
        PublishDiagnostic(_diagnostic with { TriggerCount = attempt, ExternalTrigger = external, LastTriggerUtc = DateTimeOffset.UtcNow,
            Stage = PieStage.Received, PresentationMilliseconds = null, RendererError = null });
        if (_quitting || _executing || _recordingShortcut || (!external && _shortcuts is null) || _popup is not null)
        {
            RecordPieStage(attempt, PieStage.Blocked, null, null);
            return;
        }
        IEnumerable<PieMenu> menus = external
            ? _document!.Menus.Where(menu => MenuShortcut.TryParse(menu.Shortcut, out var bound, out _) && bound == shortcut)
            : _boundMenus.GetValueOrDefault(shortcut) ?? [];
        if (SelectMenu(menus) is { } target)
            ShowPopup(target, diagnosticAttempt: attempt);
        else
        {
            RecordPieStage(attempt, PieStage.NoMatchingMenu, null, null);
            if (external) ExitWhenIdle();
        }
    }

    private void PublishDiagnostic(PieDiagnostic state)
    {
        _diagnostic = state with { RecordedUtc = DateTimeOffset.UtcNow };
        _diagnosticWriter?.Publish(_diagnostic);
    }

    private void RecordPieStage(int attempt, PieStage stage, double? milliseconds, int? error)
    {
        if (attempt == 0 || attempt != _diagnostic.TriggerCount) return;
        PublishDiagnostic(_diagnostic with
        {
            Stage = stage, PresentationMilliseconds = milliseconds, RendererError = error,
            RendererReady = stage == PieStage.RendererFailed ? false : _renderer?.IsReady == true
        });
    }

    private static PieMenu? SelectMenu(IEnumerable<PieMenu> candidates)
    {
        var context = MenuConditions.Foreground.Read();
        PieMenu? selected = null;
        var best = -1;
        foreach (var menu in candidates)
        {
            var score = menu.Conditions?.Score(context) ?? 0;
            if (score <= best) continue;
            best = score;
            selected = menu;
        }
        return selected;
    }

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _shortcuts?.Dispose();
        _shortcuts = null;
        _renderer?.Dispose();
        _renderer = null;
        Exit();
    }

    private async void ShowPopup(PieMenu menu, MenuDocument? document = null, int diagnosticAttempt = 0)
    {
        if (_popup is not null) return;
        var source = document ?? _document!;
        WebMenuWindow? popup = null;
        try
        {
            popup = GetRenderer(source, CurrentLanguage());
            _popup = popup;
            RecordPieStage(diagnosticAttempt, PieStage.StartingRenderer, null, null);
            await popup.ShowAsync(menu, (item, foreground) => ExecuteItem(item, foreground, source),
                () => { ActivateHost(); }, diagnosticAttempt);
        }
        catch (OperationCanceledException) when (popup?.IsDisposed == true) { }
        catch (Exception error)
        {
            popup?.Dispose();
            RecordPieStage(diagnosticAttempt, PieStage.RendererFailed, null, error.HResult);
            Report(error);
        }
    }

    private async void RunDeveloperCommand(string command)
    {
        try
        {
            if (command is not ("menu-theme" or "sound-theme" or "inspect-menu")) return;
            var language = CurrentLanguage();
            var document = _savedDocument ?? _document ?? MenuStorage.Load(language);
            if (command != "inspect-menu")
            {
                _renderer?.Dispose();
                _renderer = null;
            }
            var renderer = GetRenderer(document, language);
            await renderer.PrepareAsync();
            if (command == "inspect-menu") renderer.Inspect();
        }
        catch (Exception error) { Report(error); }
    }

    private WebMenuWindow GetRenderer(MenuDocument document, MenuLanguage language)
    {
        if (_renderer is { } existing && existing.Matches(document, language)) return existing;
        _renderer?.Dispose();
        var renderer = new WebMenuWindow(document, language, Report, RecordPieStage);
        renderer.Closed += () =>
        {
            if (ReferenceEquals(_popup, renderer)) _popup = null;
            ExitWhenIdle();
        };
        return _renderer = renderer;
    }

    private void QueueRendererWarmup()
    {
        if (_warmQueued) return;
        _warmQueued = DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            _warmQueued = false;
            if (_quitting || _popup is not null || _shortcuts is null || _savedDocument is not { } document) return;
            var renderer = GetRenderer(document, CurrentLanguage());
            try
            {
                await renderer.PrepareAsync();
                if (ReferenceEquals(_renderer, renderer))
                    PublishDiagnostic(_diagnostic with { RendererReady = renderer.IsReady });
            }
            catch (Exception error)
            {
                // A failed speculative load must leave the next shortcut free to retry.
                if (!renderer.IsDisposed) Log(error);
                renderer.Dispose();
                if (ReferenceEquals(_renderer, renderer))
                    PublishDiagnostic(_diagnostic with { RendererReady = false, RendererError = error.HResult });
            }
        });
    }

    private void ExitWhenIdle()
    {
        DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_popup is null && _shortcuts is null && !_executing && !_redirected && !_quitting)
            {
                _renderer?.Dispose();
                _renderer = null;
                Exit();
            }
        });
    }

    private async Task ExecuteItem(PieItem item, nint foreground, MenuDocument document)
    {
        if (_executing) return;
        _executing = true;
        try
        {
            await Task.Delay(35);
            foreach (var action in item.Actions)
            {
                switch (action.Type)
                {
                    case "open-settings":
                        ActivateHost();
                        break;
                    case "open-menu":
                        var menu = document.Menus.FirstOrDefault(m => m.Name == action.Value)
                            ?? throw new InvalidDataException("Menu not found: " + action.Value);
                        ShowPopup(menu, document);
                        return;
                    case "close-menu":
                        break; // The overlay has already faded before the workflow starts.
                    case "close-submenu":
                        return;
                    case "inhibit-shortcuts":
                        InhibitWorkflowShortcuts();
                        break;
                    case "simulate-hotkey":
                    case "execute-macro":
                        InhibitWorkflowShortcuts();
                        await MenuActions.Run(action, foreground);
                        break;
                    default:
                        await MenuActions.Run(action, foreground);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            _executing = false;
            if (_workflowInhibitsShortcuts)
            {
                _workflowInhibitsShortcuts = false;
                RefreshBindings();
            }
            ExitWhenIdle();
        }
    }

    private void InhibitWorkflowShortcuts()
    {
        if (_workflowInhibitsShortcuts) return;
        _workflowInhibitsShortcuts = true;
        RefreshBindings();
    }

    private static string[] ParseRedirected(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var pointer = CommandLineToArgvW("app " + commandLine, out var count);
        if (pointer == 0) return [];
        try
        {
            return Enumerable.Range(1, count - 1).Select(i =>
                System.Runtime.InteropServices.Marshal.PtrToStringUni(
                    System.Runtime.InteropServices.Marshal.ReadIntPtr(pointer, i * nint.Size)) ?? "").ToArray();
        }
        finally { LocalFree(pointer); }
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    private static void Log(Exception error)
    {
        try
        {
            var directory = Path.GetDirectoryName(MenuStorage.PathName)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "PieMenu.log"), $"[{DateTimeOffset.Now:O}] {error}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
