using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Devices.Input;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.System.Profile;
using Windows.UI;
using Button = Microsoft.UI.Xaml.Controls.Button;
using ComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using TextBox = Microsoft.UI.Xaml.Controls.TextBox;
using CheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using Polyline = Microsoft.UI.Xaml.Shapes.Polyline;

namespace GestureSign.WinUI;

public sealed partial class MainWindow : Window
{
    private const int DefaultWindowWidth = 1360;
    private const int DefaultWindowHeight = 800;
    private const int MinimumWindowWidth = 1180;
    private const int MinimumWindowHeight = 760;
    private const int PickOutlineThickness = 12;
    private const int PickOutlineCornerRadius = 18;
    private const byte DarkMicaDimmingOverlayAlpha = 150;
    private const byte LightMicaDimmingOverlayAlpha = 89;
    private const string AppVersion = "8.2.13";
    private const string TouchPadEdgeTopGesture = "TouchPadEdge.Top";
    private const string TouchPadEdgeBottomGesture = "TouchPadEdge.Bottom";
    private const string TouchPadEdgeLeftGesture = "TouchPadEdge.Left";
    private const string TouchPadEdgeRightGesture = "TouchPadEdge.Right";
    private const string TouchPadEdgeTopLeftGesture = "TouchPadEdge.Top.Left";
    private const string TouchPadEdgeTopRightGesture = "TouchPadEdge.Top.Right";
    private const string TouchPadEdgeBottomLeftGesture = "TouchPadEdge.Bottom.Left";
    private const string TouchPadEdgeBottomRightGesture = "TouchPadEdge.Bottom.Right";
    private const string TouchPadEdgeLeftUpGesture = "TouchPadEdge.Left.Up";
    private const string TouchPadEdgeLeftDownGesture = "TouchPadEdge.Left.Down";
    private const string TouchPadEdgeRightUpGesture = "TouchPadEdge.Right.Up";
    private const string TouchPadEdgeRightDownGesture = "TouchPadEdge.Right.Down";
    private const string TouchScreenEdgeTopGesture = "TouchScreenEdge.Top";
    private const string TouchScreenEdgeBottomGesture = "TouchScreenEdge.Bottom";
    private const string TouchScreenEdgeLeftGesture = "TouchScreenEdge.Left";
    private const string TouchScreenEdgeRightGesture = "TouchScreenEdge.Right";
    private const string TouchScreenEdgeTopLeftGesture = "TouchScreenEdge.Top.Left";
    private const string TouchScreenEdgeTopRightGesture = "TouchScreenEdge.Top.Right";
    private const string TouchScreenEdgeBottomLeftGesture = "TouchScreenEdge.Bottom.Left";
    private const string TouchScreenEdgeBottomRightGesture = "TouchScreenEdge.Bottom.Right";
    private const string TouchScreenEdgeLeftUpGesture = "TouchScreenEdge.Left.Up";
    private const string TouchScreenEdgeLeftDownGesture = "TouchScreenEdge.Left.Down";
    private const string TouchScreenEdgeRightUpGesture = "TouchScreenEdge.Right.Up";
    private const string TouchScreenEdgeRightDownGesture = "TouchScreenEdge.Right.Down";
    private static readonly string SystemUiCultureName = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name;

    private LegacyDataStore _legacyData;
    private readonly TrainingPipeServer _trainingPipeServer;
    private readonly DispatcherTimer _optionSaveTimer = new();
    private readonly DispatcherTimer _daemonWatchdogTimer = new();
    private readonly Dictionary<string, string> _pendingOptionUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FrameworkElement, TypedCommandSettingsEditor> _typedCommandSettingsEditors = new();
    private TextBlock? _pendingTrainingStatus;
    private bool _isSavingOptions;
    private string _selectedActionScope = "all";
    private bool _recognitionEnabled = true;
    private bool _updatingRecognitionToggle;
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private TextBox? _activeHotKeyRecorder;
    private TextBox? _activeHotKeySettings;
    private Action<string>? _activeHotKeyRecorded;
    private bool _activeHotKeyUsesArrayKeyCode = true;
    private static bool _isExitingApplication;
    private readonly Style _bodyStrongTextBlockStyle = CreateBodyStrongTextBlockStyle();
    private readonly HashSet<int> _hotKeyRecordingPressedKeys = new();
    private bool _stopHotKeyRecordingWhenReleased;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseHookProc;
    private TaskCompletionSource<PickedWindowInfo?>? _windowPickCompletion;
    private RECT? _pickOutlineRect;
    private IntPtr _pickOutlineHwnd;
    private IntPtr _pickOutlineWindow;
    private readonly DispatcherTimer _kandoMenuRefreshTimer = new();
    private readonly DispatcherTimer _windowModeRefreshTimer = new();
    private DateTime _lastKandoMenusWriteTimeUtc;
    private Action? _refreshKandoMenuList;
    private bool? _lastXboxBigScreenMode;
    private string _uiCultureName = "";
    private static DateTime _lastDaemonStartAttemptUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown unhandled exception"));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception);
            args.SetObserved();
        };
        _legacyData = LegacyDataStore.Load();
        _uiCultureName = _legacyData.Options.CultureName;
        ApplyUiCulture(_uiCultureName);
        _trainingPipeServer = new TrainingPipeServer(points => DispatcherQueue.TryEnqueue(async () => await SaveTrainedGestureAsync(points)));
        _optionSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _optionSaveTimer.Tick += OptionSaveTimer_Tick;
        _kandoMenuRefreshTimer.Interval = TimeSpan.FromSeconds(1);
        _kandoMenuRefreshTimer.Tick += KandoMenuRefreshTimer_Tick;
        _windowModeRefreshTimer.Interval = TimeSpan.FromSeconds(1);
        _windowModeRefreshTimer.Tick += (_, _) => ApplyXboxBigScreenTitleBarMode();
        _daemonWatchdogTimer.Interval = TimeSpan.FromSeconds(3);
        _daemonWatchdogTimer.Tick += async (_, _) => await EnsureDaemonRunningAsync();
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        ApplyMicaDimmingOverlay();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindow();
        RefreshNavigationText();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ShowPage("actions");
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplyMicaDimmingOverlay();
            ConfigureCaptionButtons();
            ShowSelectedPage();
        };
        _ = EnsureDaemonRunningAsync();
        _ = EnsureKandoStartedIfEnabledAsync();
        _daemonWatchdogTimer.Start();
        _windowModeRefreshTimer.Start();
    }

    private void ApplyMicaDimmingOverlay()
    {
        var overlay = IsDark
            ? Color.FromArgb(DarkMicaDimmingOverlayAlpha, 48, 52, 58)
            : Color.FromArgb(LightMicaDimmingOverlayAlpha, 255, 255, 255);
        Root.Background = new SolidColorBrush(overlay);
    }

    private void ReloadData()
    {
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        _ = NotifyDaemonAsync(DaemonCommand.LoadGestures);
        _ = NotifyDaemonAsync(DaemonCommand.LoadConfiguration);
        _legacyData = LegacyDataStore.Load();
        _uiCultureName = _legacyData.Options.CultureName;
        ApplyUiCulture(_uiCultureName);
        RefreshNavigationText();
        ShowSelectedPage();
    }

    private void ReloadActionDataOnly()
    {
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        _legacyData = LegacyDataStore.Load();
        if (Navigation.SelectedItem is NavigationViewItem { Tag: "actions" } && PageHost.Children.FirstOrDefault() is StackPanel root)
        {
            while (root.Children.Count > 1)
                root.Children.RemoveAt(1);
            foreach (var element in BuildActionsContent())
                root.Children.Add(element);
            return;
        }

        ShowSelectedPage();
    }

    private bool IsDark => Root.ActualTheme == ElementTheme.Dark;
    private UiLanguage CurrentLanguage => ResolveUiLanguage(_uiCultureName);
    private Style BodyStrongTextBlockStyle => _bodyStrongTextBlockStyle;

    private string T(string zh, string en) => L(zh, en, zh, zh, zh);

    private string L(string zhCn, string en, string zhTw, string ja, string ko)
        => CurrentLanguage switch
        {
            UiLanguage.English => en,
            UiLanguage.TraditionalChineseTaiwan => zhTw,
            UiLanguage.Japanese => ja,
            UiLanguage.Korean => ko,
            _ => zhCn
        };

    private static string CountText(int count, string unit) => $"{count} {unit}";

    private static UiLanguage ResolveUiLanguage(string cultureName)
    {
        var resolvedCulture = ResolveUiCultureName(cultureName);
        if (resolvedCulture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return UiLanguage.English;
        if (resolvedCulture.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            resolvedCulture.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
            return UiLanguage.TraditionalChineseTaiwan;
        if (resolvedCulture.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return UiLanguage.Japanese;
        if (resolvedCulture.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return UiLanguage.Korean;
        return UiLanguage.SimplifiedChinese;
    }

    private static string ResolveUiCultureName(string cultureName)
    {
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return cultureName.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                   cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW"
                : "zh-CN";
        if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "en-US";
        if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return "ja-JP";
        if (cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return "ko-KR";
        return SystemUiCultureName;
    }

    private static void ApplyUiCulture(string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(ResolveUiCultureName(cultureName));
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
        }
    }

    private void RefreshNavigationText()
    {
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is not string tag)
                continue;

            item.Content = tag switch
            {
                "ignored" => L("忽略", "Ignored", "忽略", "無視", "무시"),
                "gestures" => L("手势", "Gestures", "手勢", "ジェスチャ", "제스처"),
                "quickActions" => L("快捷操作", "Quick Actions", "快捷操作", "クイック操作", "빠른 작업"),
                "touchpad" => L("边缘交互", "Edge Interaction", "邊緣互動", "エッジ操作", "가장자리 상호작용"),
                "options" => L("选项", "Options", "選項", "オプション", "옵션"),
                "about" => L("关于", "About", "關於", "情報", "정보"),
                _ => L("动作", "Actions", "動作", "アクション", "동작")
            };
        }
    }

    private enum UiLanguage
    {
        SimplifiedChinese,
        English,
        TraditionalChineseTaiwan,
        Japanese,
        Korean
    }

    private static Style CreateBodyStrongTextBlockStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, Microsoft.UI.Text.FontWeights.SemiBold));
        return style;
    }

    private static Style? ResourceStyle(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Style;
        }
        catch
        {
            return null;
        }
    }

    private void ConfigureWindow()
    {
        AppWindow.Resize(ScaleLogicalSize(DefaultWindowWidth, DefaultWindowHeight));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
        CenterWindow();
        ConfigureCaptionButtons();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            ApplyXboxBigScreenTitleBarMode(presenter);
            presenter.PreferredMinimumWidth = ScaleLogicalLength(MinimumWindowWidth);
            presenter.PreferredMinimumHeight = ScaleLogicalLength(MinimumWindowHeight);
        }
    }

    private void ApplyXboxBigScreenTitleBarMode()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            ApplyXboxBigScreenTitleBarMode(presenter);
    }

    private void ApplyXboxBigScreenTitleBarMode(OverlappedPresenter presenter)
    {
        var isXboxBigScreenMode = IsXboxBigScreenMode();
        if (_lastXboxBigScreenMode == isXboxBigScreenMode)
            return;

        presenter.SetBorderAndTitleBar(!isXboxBigScreenMode, !isXboxBigScreenMode);
        _lastXboxBigScreenMode = isXboxBigScreenMode;
    }

    private static bool IsXboxBigScreenMode()
    {
        if (string.Equals(AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Xbox", StringComparison.OrdinalIgnoreCase))
            return true;

        return HasActiveGamingConfigurationSession();
    }

    private static bool HasActiveGamingConfigurationSession()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration");
            if (key is null)
                return false;

            return key.GetValue("LastPresenceTimestamp") switch
            {
                int value => value != 0,
                long value => value != 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private int ScaleLogicalLength(int value) => (int)Math.Round(value * GetWindowScale());

    private SizeInt32 ScaleLogicalSize(int width, int height)
        => new(ScaleLogicalLength(width), ScaleLogicalLength(height));

    private double GetWindowScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96d : 1d;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, uint crKey, ref BlendFunction pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool DrawFocusRect(IntPtr hdc, ref RECT rect);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, ref RECT rect, IntPtr updateRegion, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom, int width, int height);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int attributeSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private void CenterWindow()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void ConfigureCaptionButtons()
    {
        var titleBar = AppWindow.TitleBar;
        var foreground = IsDark ? Colors.White : Colors.Black;
        var inactiveForeground = IsDark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(160, 0, 0, 0);
        var hoverBackground = IsDark ? Color.FromArgb(42, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);
        var pressedBackground = IsDark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(45, 0, 0, 0);

        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            ShowPage(tag);
    }

    private void ShowSelectedPage()
    {
        if (Navigation.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (!string.Equals(tag, "quickActions", StringComparison.Ordinal))
        {
            _kandoMenuRefreshTimer.Stop();
            _refreshKandoMenuList = null;
        }

        PageHost.Children.Clear();

        switch (tag)
        {
            case "ignored":
                PageTitle.Text = L("忽略", "Ignored", "忽略", "無視", "무시");
                PageSubtitle.Text = L("设置不参与手势识别的程序和匹配规则。", "Configure applications and matching rules excluded from gesture recognition.", "設定不參與手勢辨識的程式與比對規則。", "ジェスチャ認識から除外するアプリと一致ルールを設定します。", "제스처 인식에서 제외할 프로그램과 매칭 규칙을 설정합니다.");
                PageHost.Children.Add(BuildIgnoredPage());
                break;
            case "gestures":
                PageTitle.Text = L("手势", "Gestures", "手勢", "ジェスチャ", "제스처");
                PageSubtitle.Text = L("查看、导入和整理可用手势。", "View, import, and organize available gestures.", "檢視、匯入與整理可用手勢。", "利用可能なジェスチャを表示、インポート、整理します。", "사용 가능한 제스처를 보고 가져오고 정리합니다.");
                PageHost.Children.Add(BuildGesturesPage());
                break;
            case "quickActions":
                PageTitle.Text = L("快捷操作", "Quick Actions", "快捷操作", "クイック操作", "빠른 작업");
                PageSubtitle.Text = L("用独立快捷键唤起 Kando 圆环菜单。", "Open the Kando radial menu with dedicated shortcuts.", "使用獨立快速鍵叫出 Kando 環形選單。", "専用ショートカットで Kando ラジアルメニューを開きます。", "전용 단축키로 Kando 원형 메뉴를 엽니다.");
                PageHost.Children.Add(BuildQuickActionsPage());
                break;
            case "touchpad":
                PageTitle.Text = L("边缘交互", "Edge Interaction", "邊緣互動", "エッジ操作", "가장자리 상호작용");
                PageSubtitle.Text = L("设置触控板和触摸屏边缘点击、滑动动作。", "Configure touchpad and touchscreen edge taps and swipes.", "設定觸控板與觸控螢幕邊緣點擊、滑動動作。", "タッチパッドとタッチスクリーンのエッジタップ、スワイプ操作を設定します。", "터치패드와 터치스크린 가장자리 탭 및 스와이프 동작을 설정합니다.");
                PageHost.Children.Add(BuildTouchPadPage());
                break;
            case "options":
                PageTitle.Text = L("选项", "Options", "選項", "オプション", "옵션");
                PageSubtitle.Text = L("调整识别方式、轨迹反馈、启动项和设备开关。", "Adjust recognition, visual feedback, startup, and device switches.", "調整辨識方式、軌跡回饋、啟動項與裝置開關。", "認識方式、軌跡表示、スタートアップ、デバイス設定を調整します。", "인식 방식, 궤적 표시, 시작 항목 및 장치 스위치를 조정합니다.");
                PageHost.Children.Add(BuildOptionsPage());
                break;
            case "about":
                PageTitle.Text = L("关于", "About", "關於", "情報", "정보");
                PageSubtitle.Text = L("GestureSign 的版本、项目和维护信息。", "Version, project, and maintenance information for GestureSign.", "GestureSign 的版本、專案與維護資訊。", "GestureSign のバージョン、プロジェクト、メンテナンス情報。", "GestureSign의 버전, 프로젝트 및 유지 관리 정보입니다.");
                PageHost.Children.Add(BuildAboutPage());
                break;
            default:
                PageTitle.Text = L("动作", "Actions", "動作", "アクション", "동작");
                PageSubtitle.Text = L("按程序管理手势动作。", "Manage gesture actions by application.", "依程式管理手勢動作。", "アプリごとにジェスチャアクションを管理します。", "프로그램별로 제스처 동작을 관리합니다.");
                PageHost.Children.Add(BuildActionsPage());
                break;
        }
    }

    private UIElement BuildActionsPage()
    {
        var root = NewSection();
        root.Children.Add(NewRecognitionCard());
        foreach (var element in BuildActionsContent())
            root.Children.Add(element);
        return root;
    }

    private IEnumerable<UIElement> BuildActionsContent()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var appsPanel = NewCardPanel(12);
        var userApps = _legacyData.Applications.Where(app => app.Type != "忽略").ToList();
        appsPanel.Children.Add(NewCardHeader(L("程序", "Applications", "程式", "アプリ", "프로그램"), $"{L("添加、编辑、删除或按分组管理匹配程序。数据源:", "Add, edit, delete, or group matching applications. Source:", "新增、編輯、刪除或依群組管理比對程式。資料來源:", "一致するアプリを追加、編集、削除、グループ管理します。データ元:", "매칭 프로그램을 추가, 편집, 삭제하거나 그룹별로 관리합니다. 데이터 원본:")} {_legacyData.DataSource}", L("添加程序", "Add App", "新增程式", "アプリを追加", "프로그램 추가"), "添加程序"));
        appsPanel.Children.Add(NewListRow(L("全部动作", "All Actions", "全部動作", "すべてのアクション", "모든 동작"), CountText(userApps.Sum(app => app.Actions.Count), L("个动作", "actions", "個動作", "個のアクション", "개 동작")), null, _selectedActionScope == "all", () =>
        {
            _selectedActionScope = "all";
            ShowSelectedPage();
        }));
        foreach (var group in userApps.GroupBy(app => string.IsNullOrWhiteSpace(app.Group) ? "(默认)" : app.Group))
        {
            var groupKey = $"group:{group.Key}";
            if (ShouldShowApplicationGroup(group.Key))
            {
                appsPanel.Children.Add(NewListRow($"{group.Key}  {CountText(group.Count(), L("程序", "apps", "程式", "アプリ", "개 프로그램"))}", CountText(group.Sum(app => app.Actions.Count), L("个动作", "actions", "個動作", "個のアクション", "개 동작")), null, _selectedActionScope == groupKey, () =>
                {
                    _selectedActionScope = groupKey;
                    ShowSelectedPage();
                }));
            }
            foreach (var app in group)
            {
                var appKey = ActionScopeKey(app);
                var buttons = NewInlineButtons(
                    (L("编辑", "Edit", "編輯", "編集", "편집"), async () => await EditApplicationAsync(app)),
                    (app.IsEnabled ? L("停用", "Disable", "停用", "無効化", "사용 안 함") : L("启用", "Enable", "啟用", "有効化", "사용"), async () => await ToggleEnabledAsync(app.Source)),
                    (L("新动作", "New Action", "新增動作", "新規アクション", "새 동작"), async () => await AddActionAsync(app)),
                    (L("删除", "Delete", "刪除", "削除", "삭제"), async () => await DeleteApplicationAsync(app)));
                appsPanel.Children.Add(NewApplicationRow(ApplicationDisplayName(app.Name), $"{MatchSummary(app)} · {CountText(app.Actions.Count, L("个动作", "actions", "個動作", "個のアクション", "개 동작"))}", buttons, _selectedActionScope == appKey, () =>
                {
                    _selectedActionScope = appKey;
                    ShowSelectedPage();
                }));
            }
        }

        var actionsPanel = NewCardPanel(12);
        var selectedApps = FilterApplicationsByScope(userApps).ToList();
        var allActions = selectedApps.SelectMany(app => app.Actions.Select(action => (Application: app, Action: action))).ToList();
        actionsPanel.Children.Add(NewCardHeader(ActionScopeTitle(userApps), $"{L("当前范围", "Current scope", "目前範圍", "現在の範囲", "현재 범위")} {CountText(selectedApps.Count, L("个程序", "apps", "個程式", "個のアプリ", "개 프로그램"))}、{CountText(allActions.Count, L("个动作", "actions", "個動作", "個のアクション", "개 동작"))}", L("新动作", "New Action", "新增動作", "新規アクション", "새 동작"), "新动作"));
        // actionsPanel.Children.Add(NewSmallCommandBar([(L("导入", "Import", "匯入", "インポート", "가져오기"), "导入"), (L("导出", "Export", "匯出", "エクスポート", "내보내기"), "导出"), (L("备份", "Backup", "備份", "バックアップ", "백업"), "备份"), (L("恢复", "Restore", "還原", "復元", "복원"), "恢复")]));
        foreach (var action in allActions.Take(12))
            actionsPanel.Children.Add(NewActionRow(action.Application, action.Action));
        if (allActions.Count > 12)
            actionsPanel.Children.Add(NewListRow(L("更多动作", "More Actions", "更多動作", "その他のアクション", "더 많은 동작"), $"{L("还有", "There are", "還有", "残り", "남은")} {CountText(allActions.Count - 12, L("个动作", "actions", "個動作", "個のアクション", "개 동작"))} {L("将在虚拟化列表接入后显示。", "to show after the virtualized list is connected.", "會在虛擬化清單接入後顯示。", "は仮想化リスト接続後に表示されます。", "은 가상화 목록 연결 후 표시됩니다.")}", null));

        var appsCard = NewCard(appsPanel, new Thickness(14));
        var actionsCard = NewCard(actionsPanel, new Thickness(14));
        Grid.SetColumn(actionsCard, 1);
        grid.Children.Add(appsCard);
        grid.Children.Add(actionsCard);
        yield return grid;

        yield return NewDialogMapCard();
    }

    private UIElement BuildIgnoredPage()
    {
        var root = NewSection();
        root.Children.Add(NewCard(NewCardHeader(L("忽略列表", "Ignored List", "忽略清單", "無視リスト", "무시 목록"), L("按窗口标题、窗口类名或可执行文件匹配。", "Match by window title, window class, or executable file.", "依視窗標題、視窗類別或可執行檔比對。", "ウィンドウタイトル、クラス名、実行ファイルで一致します。", "창 제목, 창 클래스 또는 실행 파일로 매칭합니다."), L("添加忽略项", "Add Ignored Item", "新增忽略項", "無視項目を追加", "무시 항목 추가"), "添加忽略项")));

        var table = NewCardPanel(10);
        var ignoredApps = _legacyData.Applications.Where(app => app.Type == "忽略").ToList();
        table.Children.Add(NewTableHeader([L("启用", "Enabled", "啟用", "有効", "사용"), L("匹配类型", "Match Type", "比對類型", "一致タイプ", "매칭 유형"), L("程序名称", "App Name", "程式名稱", "アプリ名", "프로그램 이름"), L("匹配文本", "Match Text", "比對文字", "一致テキスト", "매칭 텍스트"), L("正则", "Regex", "正則", "正規表現", "정규식")]));
        foreach (var app in ignoredApps)
            table.Children.Add(NewTableRow([app.IsEnabled ? L("开", "On", "開", "オン", "켬") : L("关", "Off", "關", "オフ", "끔"), MatchUsingText(app.MatchUsing), app.Name, app.MatchString, app.IsRegEx ? L("是", "Yes", "是", "はい", "예") : L("否", "No", "否", "いいえ", "아니요")], false, NewInlineButtons((L("编辑", "Edit", "編輯", "編集", "편집"), async () => await EditApplicationAsync(app)), (app.IsEnabled ? L("停用", "Disable", "停用", "無効化", "사용 안 함") : L("启用", "Enable", "啟用", "有効化", "사용"), async () => await ToggleEnabledAsync(app.Source)), (L("删除", "Delete", "刪除", "削除", "삭제"), async () => await DeleteApplicationAsync(app)))));
        if (ignoredApps.Count == 0)
            table.Children.Add(NewTableRow(["-", "-", L("暂无忽略项", "No ignored items", "暫無忽略項", "無視項目はありません", "무시 항목 없음"), L("可以从这里添加窗口标题、类名或 exe 匹配", "Add title, class, or exe matches here.", "可在此新增標題、類別或 exe 比對。", "ここでタイトル、クラス、exe の一致を追加できます。", "여기서 제목, 클래스 또는 exe 매칭을 추가할 수 있습니다."), "-"]));
        table.Children.Add(NewSmallCommandBar([(L("导入", "Import", "匯入", "インポート", "가져오기"), "导入"), (L("导出", "Export", "匯出", "エクスポート", "내보내기"), "导出"), (L("下载列表", "Download List", "下載清單", "リストをダウンロード", "목록 다운로드"), "下载列表")]));
        root.Children.Add(NewCard(table, new Thickness(14)));
        return root;
    }

    private IEnumerable<LegacyApplication> FilterApplicationsByScope(IReadOnlyList<LegacyApplication> applications)
    {
        if (_selectedActionScope.StartsWith("group:", StringComparison.Ordinal))
        {
            var groupName = _selectedActionScope["group:".Length..];
            return applications.Where(app => string.Equals(string.IsNullOrWhiteSpace(app.Group) ? "(默认)" : app.Group, groupName, StringComparison.Ordinal));
        }

        if (_selectedActionScope.StartsWith("app:", StringComparison.Ordinal))
            return applications.Where(app => string.Equals(ActionScopeKey(app), _selectedActionScope, StringComparison.Ordinal));

        return applications;
    }

    private string ActionScopeTitle(IReadOnlyList<LegacyApplication> applications)
    {
        if (_selectedActionScope.StartsWith("group:", StringComparison.Ordinal))
            return $"{_selectedActionScope["group:".Length..]} {L("分组", "Group", "群組", "グループ", "그룹")}";

        if (_selectedActionScope.StartsWith("app:", StringComparison.Ordinal))
            return ApplicationDisplayName(applications.FirstOrDefault(app => string.Equals(ActionScopeKey(app), _selectedActionScope, StringComparison.Ordinal))?.Name ?? "") ?? L("程序动作", "App Actions", "程式動作", "アプリアクション", "프로그램 동작");

        return L("全部动作", "All Actions", "全部動作", "すべてのアクション", "모든 동작");
    }

    private LegacyApplication? ResolveDefaultActionTarget()
    {
        var userApps = _legacyData.Applications
            .Where(app => app.Type != "忽略")
            .ToList();

        if (_selectedActionScope.StartsWith("app:", StringComparison.Ordinal))
            return userApps.FirstOrDefault(app => string.Equals(ActionScopeKey(app), _selectedActionScope, StringComparison.Ordinal));

        if (_selectedActionScope.StartsWith("group:", StringComparison.Ordinal))
            return FilterApplicationsByScope(userApps).FirstOrDefault();

        return userApps.FirstOrDefault();
    }

    private LegacyApplication? FindMatchingApplication(LegacyApplication app)
        => _legacyData.Applications.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Source, app.Source) ||
            string.Equals(candidate.Name, app.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Type, app.Type, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.MatchString, app.MatchString, StringComparison.OrdinalIgnoreCase));

    private static string ActionScopeKey(LegacyApplication app)
        => $"app:{app.Name}|{app.MatchUsing}|{app.MatchString}";

    private static bool ShouldShowApplicationGroup(string groupName)
        => !string.Equals(groupName, "(默认)", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(groupName, "Internet", StringComparison.OrdinalIgnoreCase);

    private UIElement BuildGesturesPage()
    {
        var root = NewSection();
        var header = NewCardPanel(10);
        header.Children.Add(NewCardHeader(L("手势库", "Gesture Library", "手勢庫", "ジェスチャライブラリ", "제스처 라이브러리"), L("支持大图标、绘制训练和详细信息视图。", "Supports large icons, drawing training, and detailed views.", "支援大圖示、繪製訓練與詳細資訊檢視。", "大きなアイコン、描画トレーニング、詳細ビューに対応します。", "큰 아이콘, 그리기 훈련 및 상세 보기 지원."), L("新建手势", "New Gesture", "新增手勢", "新規ジェスチャ", "새 제스처"), "新建手势"));
        header.Children.Add(NewSmallCommandBar([(L("绘制手势", "Draw Gesture", "繪製手勢", "ジェスチャを描画", "제스처 그리기"), "绘制手势"), (L("后台训练手势", "Background Training", "背景訓練手勢", "バックグラウンド学習", "백그라운드 훈련"), "后台训练手势"), (L("导入手势文件", "Import Gesture File", "匯入手勢檔案", "ジェスチャファイルをインポート", "제스처 파일 가져오기"), "导入手势文件"), (L("导出手势文件", "Export Gesture File", "匯出手勢檔案", "ジェスチャファイルをエクスポート", "제스처 파일 내보내기"), "导出手势文件")]));
        root.Children.Add(NewCard(header));

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var groupedGestures = _legacyData.Gestures
            .GroupBy(gesture => gesture.FingerCount)
            .OrderBy(group => group.Key)
            .ToList();

        var twoFinger = NewGestureGroup(L("1-2 指手势", "1-2 Finger Gestures", "1-2 指手勢", "1-2 本指ジェスチャ", "1-2 손가락 제스처"), groupedGestures.Where(group => group.Key <= 2).SelectMany(group => group).Take(12).ToArray());
        var threeFinger = NewGestureGroup(L("3 指手势", "3 Finger Gestures", "3 指手勢", "3 本指ジェスチャ", "3 손가락 제스처"), groupedGestures.Where(group => group.Key == 3).SelectMany(group => group).Take(12).ToArray());
        var custom = NewGestureGroup(L("更多手势", "More Gestures", "更多手勢", "その他のジェスチャ", "더 많은 제스처"), groupedGestures.Where(group => group.Key >= 4).SelectMany(group => group).Take(16).ToArray());

        Grid.SetColumn(threeFinger, 1);
        Grid.SetColumn(custom, 0);
        Grid.SetColumnSpan(custom, 2);
        Grid.SetRow(custom, 1);
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(twoFinger);
        grid.Children.Add(threeFinger);
        grid.Children.Add(custom);
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildTouchPadPage()
    {
        var root = NewSection();
        root.Children.Add(NewSettingsGroup(L("边缘识别", "Edge Recognition", "邊緣辨識", "エッジ認識", "가장자리 인식"),
        [
            NewToggleRow(L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "タッチパッドジェスチャを有効にする", "터치패드 제스처 사용"), _legacyData.Options.RegisterTouchPad, "RegisterTouchPad"),
            NewToggleRow(L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows のタッチパッドシステムジェスチャを優先", "Windows 터치패드 시스템 제스처 우선 사용"), _legacyData.Options.PreferWindowsTouchPadGestures, "PreferWindowsTouchPadGestures")
        ]));

        root.Children.Add(NewTouchPadMapCard());
        root.Children.Add(NewTouchScreenMapCard());
        return root;
    }

    private FrameworkElement NewTouchPadMapCard()
    {
        var panel = NewCardPanel(14);
        panel.Children.Add(new TextBlock
        {
            Text = L("触控板边缘", "Touchpad Edges", "觸控板邊緣", "タッチパッドのエッジ", "터치패드 가장자리"),
            Style = BodyStrongTextBlockStyle
        });

        var map = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            MinHeight = 700
        };
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });

        var edges = TouchPadEdges();
        var top = NewTouchPadZone(edges[0]);
        var bottom = NewTouchPadZone(edges[1]);
        var left = NewTouchPadZone(edges[2]);
        var right = NewTouchPadZone(edges[3]);

        Grid.SetColumn(top, 1);
        map.Children.Add(top);

        Grid.SetRow(left, 1);
        map.Children.Add(left);

        var center = NewTouchPadCenter();
        Grid.SetColumn(center, 1);
        Grid.SetRow(center, 1);
        map.Children.Add(center);

        Grid.SetColumn(right, 2);
        Grid.SetRow(right, 1);
        map.Children.Add(right);

        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 2);
        map.Children.Add(bottom);

        var cornerCells = new[]
        {
            (Column: 0, Row: 0),
            (Column: 2, Row: 0),
            (Column: 0, Row: 2),
            (Column: 2, Row: 2)
        };
        foreach (var cell in cornerCells)
        {
            var corner = NewTouchPadMapFiller();
            Grid.SetColumn(corner, cell.Column);
            Grid.SetRow(corner, cell.Row);
            map.Children.Add(corner);
        }

        panel.Children.Add(new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = map
        });

        return NewCard(panel, new Thickness(14));
    }

    private FrameworkElement NewTouchScreenMapCard()
    {
        var panel = NewCardPanel(14);
        panel.Children.Add(new TextBlock
        {
            Text = L("触摸屏边缘", "Touchscreen Edges", "觸控螢幕邊緣", "タッチスクリーンのエッジ", "터치스크린 가장자리"),
            Style = BodyStrongTextBlockStyle
        });

        var map = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            MinHeight = 700
        };
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });

        var edges = TouchScreenEdges();
        var top = NewTouchPadZone(edges[0]);
        var bottom = NewTouchPadZone(edges[1]);
        var left = NewTouchPadZone(edges[2]);
        var right = NewTouchPadZone(edges[3]);

        Grid.SetColumn(top, 1);
        map.Children.Add(top);

        Grid.SetRow(left, 1);
        map.Children.Add(left);

        var center = NewTouchScreenCenter();
        Grid.SetColumn(center, 1);
        Grid.SetRow(center, 1);
        map.Children.Add(center);

        Grid.SetColumn(right, 2);
        Grid.SetRow(right, 1);
        map.Children.Add(right);

        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 2);
        map.Children.Add(bottom);

        var cornerCells = new[]
        {
            (Column: 0, Row: 0),
            (Column: 2, Row: 0),
            (Column: 0, Row: 2),
            (Column: 2, Row: 2)
        };
        foreach (var cell in cornerCells)
        {
            var corner = NewTouchPadMapFiller();
            Grid.SetColumn(corner, cell.Column);
            Grid.SetRow(corner, cell.Row);
            map.Children.Add(corner);
        }

        panel.Children.Add(new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = map
        });

        return NewCard(panel, new Thickness(14));
    }

    private FrameworkElement NewTouchPadZone(TouchPadEdgeZone zone)
    {
        var isHorizontalZone = zone.Marker == TouchPadEdgeMarker.Horizontal;
        var orderedActions = OrderedTouchPadEdgeActions(zone, isHorizontalZone);
        var content = NewCardPanel(8);
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(new TextBlock
        {
            Text = zone.Title,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = BodyStrongTextBlockStyle
        });

        if (isHorizontalZone)
        {
            var actions = new Grid
            {
                ColumnSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            for (var index = 0; index < orderedActions.Count; index++)
            {
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var button = NewTouchPadGestureButton(orderedActions[index]);
                Grid.SetColumn(button, index);
                actions.Children.Add(button);
            }

            content.Children.Add(actions);
        }
        else
        {
            foreach (var item in orderedActions)
                content.Children.Add(NewTouchPadGestureButton(item));
        }

        return new Border
        {
            Background = TouchPadZoneBrush(zone.Actions.Any(item => GetGlobalTouchPadAction(item.GestureName)?.IsEnabled == true)),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = content
        };
    }

    private static IReadOnlyList<TouchPadEdgeAction> OrderedTouchPadEdgeActions(TouchPadEdgeZone zone, bool isHorizontalZone)
    {
        if (zone.Actions.Count != 3)
            return zone.Actions;

        var tap = zone.Actions.FirstOrDefault(action => action.GestureName.Count(ch => ch == '.') == 1);
        if (tap is null)
            return zone.Actions;

        var first = zone.Actions.FirstOrDefault(action =>
            action != tap &&
            action.GestureName.EndsWith(isHorizontalZone ? ".Left" : ".Up", StringComparison.OrdinalIgnoreCase));
        var last = zone.Actions.FirstOrDefault(action =>
            action != tap &&
            action.GestureName.EndsWith(isHorizontalZone ? ".Right" : ".Down", StringComparison.OrdinalIgnoreCase));

        return first is not null && last is not null
            ? new[] { first, tap, last }
            : zone.Actions;
    }

    private Button NewTouchPadGestureButton(TouchPadEdgeAction item)
    {
        var action = GetGlobalTouchPadAction(item.GestureName);
        var command = action?.Commands.FirstOrDefault();
        var title = new TextBlock
        {
            Text = item.Title,
            Style = ResourceStyle("BodyTextBlockStyle"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        var summary = new TextBlock
        {
            Text = TouchPadCommandSummary(action, command),
            FontSize = 12,
            Opacity = action?.IsEnabled == false ? 0.5 : 0.68,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        };

        var stack = NewCardPanel(2);
        stack.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(title);
        stack.Children.Add(summary);

        var button = new Button
        {
            Content = stack,
            Background = TouchPadMiniZoneBrush(action),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinHeight = 58,
            Padding = new Thickness(8, 5, 8, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += async (_, _) => await RunUiActionAsync(() => ConfigureTouchPadEdgeCommandAsync(item.GestureName, item.Title));
        return button;
    }

    private FrameworkElement NewTouchPadCenter()
    {
        var panel = NewCardPanel(8);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE815",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = L("触控板", "Touchpad", "觸控板", "タッチパッド", "터치패드"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = ResourceStyle("SubtitleTextBlockStyle")
        });

        return new Border
        {
            Background = TouchPadCenterBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
        };
    }

    private FrameworkElement NewTouchScreenCenter()
    {
        var panel = NewCardPanel(8);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE815",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = L("触摸屏", "Touchscreen", "觸控螢幕", "タッチスクリーン", "터치스크린"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = ResourceStyle("SubtitleTextBlockStyle")
        });

        return new Border
        {
            Background = TouchPadCenterBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
        };
    }

    private FrameworkElement NewTouchPadMapFiller()
    {
        return new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
    }

    private async Task ConfigureTouchPadEdgeCommandAsync(string gestureName, string title)
    {
        var existingAction = GetGlobalTouchPadAction(gestureName);
        var existingCommand = existingAction?.Commands.FirstOrDefault();
        var name = new TextBox
        {
            PlaceholderText = "命令名称",
            Text = existingCommand?.Name ?? "发送快捷键"
        };
        var plugin = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            SelectedIndex = existingCommand is null ? 0 : PluginIndex(existingCommand.PluginClass)
        };
        AddPluginItems(plugin);
        var pluginClass = new TextBox
        {
            PlaceholderText = "自定义插件类名",
            Text = existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var settings = new TextBox
        {
            Text = existingCommand?.Settings ?? "",
            PlaceholderText = "命令设置 JSON，可留空",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 80
        };
        var hotkey = NewHotKeyRecorderWithClear(settings, settings.Text);
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var enabled = new CheckBox
        {
            Content = "启用这个边缘",
            IsChecked = existingAction?.IsEnabled ?? true,
            Margin = new Thickness(0, 8, 0, 0)
        };

        void UpdateEditor(bool resetSettings)
        {
            var selectedClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(selectedClass))
                pluginClass.Text = selectedClass;

            if (resetSettings)
                settings.Text = PluginSettingsTemplate(pluginClass.Text);

            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings, appPicker);
        }

        plugin.SelectionChanged += (_, _) => UpdateEditor(true);

        var panel = NewCardPanel(8);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(settings);
        panel.Children.Add(enabled);
        UpdateEditor(false);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"编辑{title}",
            Content = NewDialogScrollContent(panel),
            PrimaryButtonText = "保存",
            SecondaryButtonText = existingAction is null ? "" : "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            await DeleteTouchPadEdgeAsync(gestureName, title, confirm: false);
            return;
        }

        if (result != ContentDialogResult.Primary)
            return;

        var pluginClassValue = pluginClass.Text.Trim();
        if (string.IsNullOrWhiteSpace(pluginClassValue))
        {
            await ShowInfoDialog("插件类名为空", "请选择一个操作，或填写自定义插件类名。");
            return;
        }

        var globalApp = EnsureGlobalApplication();
        var action = globalApp.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            _legacyData.AddAction(globalApp, title, gestureName);
            _legacyData = LegacyDataStore.Load();
            globalApp = EnsureGlobalApplication();
            action = globalApp.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        }

        if (action is null)
        {
            await ShowInfoDialog("保存失败", "没有找到可写入的全局动作。");
            return;
        }

        var command = action.Commands.FirstOrDefault();
        if (command is null)
            _legacyData.AddCommand(action, name.Text, pluginClassValue, settings.Text);
        else
            _legacyData.UpdateCommand(command, name.Text, pluginClassValue, settings.Text, true);

        _legacyData = LegacyDataStore.Load();
        action = GetGlobalTouchPadAction(gestureName);
        if (action is not null)
            _legacyData.SetEnabled(action.Source, enabled.IsChecked ?? true);

        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private async Task ToggleTouchPadEdgeAsync(string gestureName)
    {
        var action = GetGlobalTouchPadAction(gestureName);
        if (action is null)
            return;

        _legacyData.SetEnabled(action.Source, !action.IsEnabled);
        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private async Task DeleteTouchPadEdgeAsync(string gestureName, string title, bool confirm = true)
    {
        var globalApp = GetGlobalApplication();
        var action = globalApp?.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        if (globalApp is null || action is null)
            return;

        if (confirm && !await ConfirmDialogAsync("清空边缘动作", $"确定清空{title}？", "清空"))
            return;

        _legacyData.DeleteAction(globalApp, action);
        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private LegacyApplication EnsureGlobalApplication()
    {
        var globalApp = GetGlobalApplication();
        if (globalApp is not null)
            return globalApp;

        _legacyData.EnsureGlobalApplication();
        _legacyData = LegacyDataStore.Load();
        return GetGlobalApplication()
            ?? throw new InvalidOperationException("无法创建全局动作配置。");
    }

    private LegacyApplication? GetGlobalApplication()
        => _legacyData.Applications.FirstOrDefault(app => app.Type == "全局");

    private LegacyAction? GetGlobalTouchPadAction(string gestureName)
        => GetGlobalApplication()?.Actions.FirstOrDefault(action => string.Equals(action.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));

    private string TouchPadCommandSummary(LegacyAction? action, LegacyCommand? command)
    {
        if (action is null)
            return L("未设置", "Not set", "未設定", "未設定", "설정 안 됨");

        if (command is null)
            return action.IsEnabled
                ? L("未设置命令", "No command", "未設定命令", "コマンド未設定", "명령 없음")
                : L("已停用", "Disabled", "已停用", "無効", "사용 안 함");

        var hotKey = HotKeyDisplayText(command.Settings);
        if (!string.IsNullOrWhiteSpace(hotKey))
            return action.IsEnabled ? hotKey : $"{hotKey} · {L("已停用", "Disabled", "已停用", "無効", "사용 안 함")}";

        if (!action.IsEnabled)
            return $"{PluginName(command.PluginClass)} · {L("已停用", "Disabled", "已停用", "無効", "사용 안 함")}";

        return $"{PluginName(command.PluginClass)} · {(command.IsEnabled ? L("启用", "Enabled", "啟用", "有効", "사용") : L("停用", "Disabled", "停用", "無効", "사용 안 함"))}";
    }

    private SolidColorBrush TouchPadSurfaceBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 61, 64, 67))
            : new SolidColorBrush(Color.FromArgb(255, 224, 234, 242));
    }

    private SolidColorBrush TouchPadCenterBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 72, 76, 79))
            : new SolidColorBrush(Color.FromArgb(255, 238, 244, 249));
    }

    private SolidColorBrush TouchPadZoneBrush(bool hasEnabledAction)
    {
        if (hasEnabledAction)
        {
            return IsDark
                ? new SolidColorBrush(Color.FromArgb(255, 38, 61, 80))
                : new SolidColorBrush(Color.FromArgb(255, 216, 235, 250));
        }

        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 39, 40, 42))
            : new SolidColorBrush(Color.FromArgb(255, 246, 249, 252));
    }

    private SolidColorBrush TouchPadMiniZoneBrush(LegacyAction? action)
    {
        if (action?.IsEnabled == true)
        {
            return IsDark
                ? new SolidColorBrush(Color.FromArgb(255, 52, 77, 98))
                : new SolidColorBrush(Color.FromArgb(255, 226, 241, 252));
        }

        return SubtleBrush();
    }

    private IReadOnlyList<TouchPadEdgeZone> TouchPadEdges()
        =>
        [
            new(L("上边缘", "Top Edge", "上邊緣", "上エッジ", "위쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeTopGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchPadEdgeTopLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchPadEdgeTopRightGesture)
            ]),
            new(L("下边缘", "Bottom Edge", "下邊緣", "下エッジ", "아래쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeBottomGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchPadEdgeBottomLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchPadEdgeBottomRightGesture)
            ]),
            new(L("左边缘", "Left Edge", "左邊緣", "左エッジ", "왼쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeLeftGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchPadEdgeLeftUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchPadEdgeLeftDownGesture)
            ]),
            new(L("右边缘", "Right Edge", "右邊緣", "右エッジ", "오른쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeRightGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchPadEdgeRightUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchPadEdgeRightDownGesture)
            ])
        ];

    private IReadOnlyList<TouchPadEdgeZone> TouchScreenEdges()
        =>
        [
            new(L("上边缘", "Top Edge", "上邊緣", "上エッジ", "위쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeTopGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchScreenEdgeTopLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchScreenEdgeTopRightGesture)
            ]),
            new(L("下边缘", "Bottom Edge", "下邊緣", "下エッジ", "아래쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeBottomGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchScreenEdgeBottomLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchScreenEdgeBottomRightGesture)
            ]),
            new(L("左边缘", "Left Edge", "左邊緣", "左エッジ", "왼쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeLeftGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchScreenEdgeLeftUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchScreenEdgeLeftDownGesture)
            ]),
            new(L("右边缘", "Right Edge", "右邊緣", "右エッジ", "오른쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeRightGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchScreenEdgeRightUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchScreenEdgeRightDownGesture)
            ])
        ];

    private FrameworkElement NewTouchPadEdgeMarker(TouchPadEdgeMarker marker)
    {
        var isHorizontal = marker == TouchPadEdgeMarker.Horizontal;
        return new Border
        {
            Width = isHorizontal ? 34 : 4,
            Height = isHorizontal ? 4 : 34,
            CornerRadius = new CornerRadius(2),
            Background = IsDark
                ? new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(210, 24, 32, 38)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private enum TouchPadEdgeMarker
    {
        None,
        Horizontal,
        Vertical
    }

    private sealed record TouchPadEdgeZone(string Title, TouchPadEdgeMarker Marker, IReadOnlyList<TouchPadEdgeAction> Actions);

    private sealed record TouchPadEdgeAction(string Title, string GestureName);

    private UIElement BuildOptionsPage()
    {
        var root = NewSection();
        var options = _legacyData.Options;
        root.Children.Add(NewSettingsGroup(L("视觉反馈", "Visual Feedback", "視覺回饋", "視覚フィードバック", "시각 피드백"),
        [
            NewToggleRow(L("显示手势轨迹", "Show gesture trail", "顯示手勢軌跡", "ジェスチャ軌跡を表示", "제스처 궤적 표시"), options.VisualFeedbackWidth > 0, "VisualFeedbackWidth", options.VisualFeedbackWidth == 0 ? "9" : options.VisualFeedbackWidth.ToString(), "0"),
            NewToggleRow(L("显示触发的手势操作", "Show triggered gesture action", "顯示觸發的手勢動作", "実行したジェスチャアクションを表示", "실행된 제스처 동작 표시"), options.ShowGestureActionHint, "ShowGestureActionHint"),
            NewSliderRow(L("轨迹透明度", "Trail opacity", "軌跡透明度", "軌跡の透明度", "궤적 투명도"), options.Opacity, 0.05, 1, 0.01, "Opacity", value => value.ToString("0.00", CultureInfo.InvariantCulture), value => $"{Math.Round(value * 100)}%"),
            NewSliderRow(L("轨迹宽度", "Trail width", "軌跡寬度", "軌跡の幅", "궤적 너비"), options.VisualFeedbackWidth, 0, 30, 1, "VisualFeedbackWidth", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewSliderRow(L("最小点距离", "Minimum point distance", "最小點距離", "最小ポイント距離", "최소 지점 거리"), options.MinimumPointDistance, 1, 100, 1, "MinimumPointDistance", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewVisualFeedbackColorRow(options.VisualFeedbackColor)
        ]));

        root.Children.Add(NewSettingsGroup(L("输入设备", "Input Devices", "輸入裝置", "入力デバイス", "입력 장치"),
        [
            NewToggleRow(L("启用鼠标手势", "Enable mouse gestures", "啟用滑鼠手勢", "マウスジェスチャを有効にする", "마우스 제스처 사용"), NormalizeDrawingButton(options.DrawingButton) != 0, "DrawingButton", NormalizeDrawingButton(options.DrawingButton, 2097152).ToString(CultureInfo.InvariantCulture), "0"),
            NewToggleRow(L("Edge 优先使用自带鼠标手势", "Prefer built-in Edge mouse gestures", "Edge 優先使用內建滑鼠手勢", "Edge 内蔵マウスジェスチャを優先", "Edge 기본 마우스 제스처 우선 사용"), options.PreferEdgeMouseGestures, "PreferEdgeMouseGestures"),
            NewComboRow(L("绘制按钮", "Drawing button", "繪製按鈕", "描画ボタン", "그리기 버튼"), [L("右键", "Right button", "右鍵", "右ボタン", "오른쪽 버튼"), L("中键", "Middle button", "中鍵", "中央ボタン", "가운데 버튼"), "X1", "X2"], ["2097152", "4194304", "8388608", "16777216"], "DrawingButton", DrawingButtonIndex(NormalizeDrawingButton(options.DrawingButton, 2097152))),
            NewToggleRow(L("启用触摸屏手势", "Enable touchscreen gestures", "啟用觸控螢幕手勢", "タッチスクリーンジェスチャを有効にする", "터치스크린 제스처 사용"), options.RegisterTouchScreen, "RegisterTouchScreen"),
            NewToggleRow(L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "タッチパッドジェスチャを有効にする", "터치패드 제스처 사용"), options.RegisterTouchPad, "RegisterTouchPad"),
            NewToggleRow(L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows のタッチパッドジェスチャを優先", "Windows 터치패드 제스처 우선 사용"), options.PreferWindowsTouchPadGestures, "PreferWindowsTouchPadGestures"),
            NewToggleRow(L("启用触控笔手势", "Enable pen gestures", "啟用觸控筆手勢", "ペンジェスチャを有効にする", "펜 제스처 사용"), options.PenGestureButton != 0, "PenGestureButton", options.PenGestureButton == 0 ? "4" : options.PenGestureButton.ToString(CultureInfo.InvariantCulture), "0"),
            NewPenButtonRow(options.PenGestureButton)
        ]));

        root.Children.Add(NewSettingsGroup(L("系统", "System", "系統", "システム", "시스템"),
        [
            NewComboRow(L("语言", "Language", "語言", "言語", "언어"), [L("跟随系统", "Follow system", "跟隨系統", "システムに合わせる", "시스템 설정 따르기"), "简体中文", "English", "繁體中文（台灣）", "日本語", "한국어"], ["", "zh-CN", "en-US", "zh-TW", "ja-JP", "ko-KR"], "CultureName", CultureIndex(_uiCultureName)),
            NewToggleRow(L("启用初始超时", "Enable initial timeout", "啟用初始逾時", "初期タイムアウトを有効にする", "초기 시간 제한 사용"), options.InitialTimeout > 0, "InitialTimeout", options.InitialTimeout == 0 ? "1000" : options.InitialTimeout.ToString(), "0"),
            NewSliderRow(L("初始超时", "Initial timeout", "初始逾時", "初期タイムアウト", "초기 시간 제한"), options.InitialTimeout / 1000d, 0, 2, 0.1, "InitialTimeout", value => ((int)Math.Round(value * 1000)).ToString(CultureInfo.InvariantCulture), value => CurrentLanguage == UiLanguage.English ? $"{value:0.0} sec" : $"{value:0.0} 秒"),
            NewStartupToggleRow(),
            NewAdminStartupToggleRow(options.RunAsAdmin),
            NewToggleRow(L("排除全屏游戏/应用", "Ignore fullscreen games/apps", "排除全螢幕遊戲/應用程式", "全画面ゲーム/アプリを除外", "전체 화면 게임/앱 제외"), options.IgnoreFullScreen, "IgnoreFullScreen"),
            NewToggleRow(L("排除全屏播放视频（试验）", "Ignore fullscreen video playback (experimental)", "排除全螢幕影片播放（實驗）", "全画面動画再生を除外（実験）", "전체 화면 동영상 재생 제외(실험)"), options.IgnoreFullScreenVideo, "IgnoreFullScreenVideo"),
            NewToggleRow(L("使用笔时忽略触摸输入", "Ignore touch input while using pen", "使用筆時忽略觸控輸入", "ペン使用中はタッチ入力を無視", "펜 사용 중 터치 입력 무시"), options.IgnoreTouchInputWhenUsingPen, "IgnoreTouchInputWhenUsingPen"),
            NewToggleRow(L("显示托盘图标", "Show tray icon", "顯示系統匣圖示", "トレイアイコンを表示", "트레이 아이콘 표시"), options.ShowTrayIcon, "ShowTrayIcon"),
            NewOneDriveSyncRow(),
            NewOpenSettingsHotKeyRow(options.OpenSettingsHotKey),
            NewToggleRow(L("错误日志提示", "Error log notifications", "錯誤記錄提示", "エラーログ通知", "오류 로그 알림"), options.SendErrorReport, "SendErrorReport"),
            NewButtonRow(L("配置文件", "Configuration files", "設定檔", "設定ファイル", "구성 파일"), [L("备份", "Backup", "備份", "バックアップ", "백업"), L("恢复", "Restore", "還原", "復元", "복원"), L("打开配置文件夹", "Open config folder", "開啟設定檔資料夾", "設定フォルダーを開く", "구성 폴더 열기")]),
            NewButtonRow(L("退出", "Exit", "結束", "終了", "종료"), [L("退出", "Exit", "結束", "終了", "종료")])
        ]));

        return root;
    }

    private UIElement BuildQuickActionsPage()
    {
        var root = NewSection();
        var options = _legacyData.Options;

        root.Children.Add(NewKandoPowerToysPreviewCard());
        root.Children.Add(NewKandoPowerToysToggleRow(options.KandoEnabled));
        root.Children.Add(NewKandoSettingsHotKeyRow(options.KandoSettingsHotKey));
        root.Children.Add(NewKandoOpenSettingsRow());
        return root;
    }

    private FrameworkElement NewKandoPowerToysPreviewCard()
    {
        var image = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/kando-preview.gif")),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        var surfaceColor = IsDark
            ? Color.FromArgb(255, 24, 26, 30)
            : Color.FromArgb(255, 238, 244, 250);
        var feather = new Border
        {
            Height = 180,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(0, surfaceColor.R, surfaceColor.G, surfaceColor.B), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(28, surfaceColor.R, surfaceColor.G, surfaceColor.B), Offset = 0.28 },
                    new GradientStop { Color = Color.FromArgb(96, surfaceColor.R, surfaceColor.G, surfaceColor.B), Offset = 0.55 },
                    new GradientStop { Color = Color.FromArgb(190, surfaceColor.R, surfaceColor.G, surfaceColor.B), Offset = 0.82 },
                    new GradientStop { Color = surfaceColor, Offset = 1 }
                }
            }
        };

        var preview = new Grid();
        preview.Children.Add(image);
        preview.Children.Add(feather);

        var frame = new Border
        {
            Height = 320,
            Background = new SolidColorBrush(surfaceColor),
            Child = preview
        };

        return NewCard(frame, new Thickness(0));
    }

    private FrameworkElement NewKandoPowerToysToggleRow(bool isEnabled)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isEnabled,
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += async (_, _) =>
        {
            if (toggle.IsOn)
                await RunUiActionAsync(EnableKandoQuickActionsAsync);
            else
                await RunUiActionAsync(DisableKandoQuickActionsAsync);
        };

        return NewPowerToysSettingCard("\uE945", L("快捷操作", "Quick Actions", "快捷操作", "クイック操作", "빠른 작업"), null, toggle);
    }

    private FrameworkElement NewKandoSettingsHotKeyRow(string existingSettings)
    {
        var settings = new TextBox { Text = existingSettings, Visibility = Visibility.Collapsed };
        var recorder = NewHotKeyRecorder(settings, existingSettings, onRecorded: value => UpdateOptionAndReloadNow("KandoSettingsHotKey", value));
        recorder.Margin = new Thickness(0);
        recorder.MinWidth = 260;
        recorder.MaxWidth = 360;
        recorder.HorizontalAlignment = HorizontalAlignment.Right;

        settings.TextChanged += (_, _) => UpdateOptionAndReloadNow("KandoSettingsHotKey", settings.Text);

        var clear = NewPillButton(L("清除", "Clear", "清除", "クリア", "지우기"), false);
        clear.Click += (_, _) =>
        {
            if (ReferenceEquals(_activeHotKeyRecorder, recorder))
                StopHotKeyRecording();
            settings.Text = "";
            recorder.Text = "";
            UpdateOptionAndReloadNow("KandoSettingsHotKey", "");
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(recorder);
        panel.Children.Add(clear);

        return NewPowerToysSettingCard("\uE765", L("打开 Kando 设置页面的快捷键", "Hotkey to open Kando settings", "開啟 Kando 設定頁面的快速鍵", "Kando 設定を開くショートカット", "Kando 설정 열기 단축키"), null, panel);
    }

    private FrameworkElement NewKandoOpenSettingsRow()
    {
        var button = NewPillButton(L("打开 Kando 设置", "Open Kando Settings", "開啟 Kando 設定", "Kando 設定を開く", "Kando 설정 열기"), false);
        button.Click += async (_, _) => await RunUiActionAsync(OpenKandoSettingsAsync);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Center;

        return NewPowerToysSettingCard("\uE713", L("Kando 设置", "Kando Settings", "Kando 設定", "Kando 設定", "Kando 설정"), L("配置菜单、触发方式、外观等", "Configure menus, triggers, appearance, and more.", "設定選單、觸發方式、外觀等。", "メニュー、トリガー、外観などを設定します。", "메뉴, 트리거, 모양 등을 설정합니다."), button);
    }

    private FrameworkElement NewPowerToysSettingCard(string glyph, string title, string? subtitle, FrameworkElement control)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16,
            MinHeight = 64
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 24,
            Width = 36,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);

        var text = NewCardPanel(2);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = ResourceStyle("BodyTextBlockStyle"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);

        return NewCard(grid, new Thickness(16, 12, 16, 12));
    }

    private FrameworkElement NewKandoMenuPicker(LegacyOptions options, TextBox hotKeySettings, TextBox hotKeyRecorder)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = "Kando 菜单",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var content = new StackPanel { Spacing = 8 };
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        var selectedMenuName = options.KandoMenuName;

        void ApplyKandoShortcutIfAvailable(KandoMenuInfo selected, bool overwriteExisting)
        {
            if (!TryCreateHotKeySettingsFromKandoShortcut(selected.Shortcut, out var settings))
                return;

            if (!overwriteExisting && !string.IsNullOrWhiteSpace(hotKeySettings.Text))
                return;

            hotKeySettings.Text = settings;
            hotKeyRecorder.Text = HotKeyDisplayText(settings);
            UpdateOptionAndReloadNow("KandoHotKey", settings);
        }

        void SelectMenu(KandoMenuInfo selected)
        {
            selectedMenuName = selected.Name;
            UpdateOptionAndReloadNow("KandoMenuName", selected.Name);
            UpdateOptionAndReloadNow("KandoTrigger", "");
            ApplyKandoShortcutIfAvailable(selected, overwriteExisting: true);
            RenderMenuList(applyDefaultShortcut: false);
        }

        void RenderMenuList(bool applyDefaultShortcut)
        {
            var menus = ReadKandoMenus();
            content.Children.Clear();
            if (menus.Count == 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "没有读取到 Kando 菜单。请先打开 Kando 设置创建菜单，或检查 %APPDATA%\\kando\\menus.json。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72
                });
                content.Children.Add(NewLightTextOption("菜单名称", selectedMenuName, "KandoMenuName", "例如: 示例菜单"));
                return;
            }

            var selectedIndex = KandoMenuIndex(menus, selectedMenuName);
            selectedMenuName = menus[selectedIndex].Name;

            var list = new StackPanel { Spacing = 8 };
            for (var index = 0; index < menus.Count; index++)
            {
                var menu = menus[index];
                var isSelected = index == selectedIndex;
                var shortcutText = $"快捷键: {DisplayFallback(menu.Shortcut)}";
                var trailing = new TextBlock
                {
                    Text = isSelected ? "已选择" : "选择",
                    Opacity = isSelected ? 0.9 : 0.62,
                    VerticalAlignment = VerticalAlignment.Center
                };
                list.Children.Add(NewKandoMenuRow(menu.Name, shortcutText, trailing, isSelected, () => SelectMenu(menu)));
            }

            content.Children.Add(list);
            if (applyDefaultShortcut)
                ApplyKandoShortcutIfAvailable(menus[selectedIndex], overwriteExisting: false);
        }

        _refreshKandoMenuList = () => RenderMenuList(applyDefaultShortcut: false);
        _lastKandoMenusWriteTimeUtc = GetKandoMenusWriteTimeUtc();
        _kandoMenuRefreshTimer.Start();
        RenderMenuList(applyDefaultShortcut: true);
        return grid;
    }

    private FrameworkElement NewKandoHotKeyOption(TextBox settings, out TextBox recorder)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = "唤起快捷键",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var recorderBox = NewHotKeyRecorder(settings, settings.Text, onRecorded: value => UpdateOptionAndReloadNow("KandoHotKey", value));
        recorderBox.Margin = new Thickness(0);
        recorderBox.HorizontalAlignment = HorizontalAlignment.Stretch;

        var clear = NewPillButton("清除", false);
        clear.Click += (_, _) =>
        {
            if (ReferenceEquals(_activeHotKeyRecorder, recorderBox))
                StopHotKeyRecording();
            settings.Text = "";
            recorderBox.Text = "";
            UpdateOptionAndReloadNow("KandoHotKey", "");
        };

        var panel = new Grid { ColumnSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(recorderBox);
        Grid.SetColumn(clear, 1);
        panel.Children.Add(clear);
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);
        recorder = recorderBox;
        return grid;
    }

    private FrameworkElement NewLightTextOption(string title, string value, string configKey, string placeholder)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var text = new TextBox
        {
            Text = value,
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        text.LostFocus += (_, _) => UpdateOptionAndReloadNow(configKey, text.Text.Trim());
        text.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
                UpdateOptionAndReloadNow(configKey, text.Text.Trim());
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private UIElement BuildAboutPage()
    {
        var root = NewSection();
        var content = NewCardPanel();
        content.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/logo.png")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(new TextBlock { Text = "GestureSign V2", Style = ResourceStyle("TitleTextBlockStyle"), Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { Text = $"WinUI 3 前端重构预览\n版本：{AppVersion}", Opacity = 0.72, Margin = new Thickness(0, 4, 0, 0) });
        content.Children.Add(new TextBlock { Text = "作者: TransposonY\n发现问题或建议欢迎反馈: 553078206@qq.com\nQQ 交流群: 576981420", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        content.Children.Add(NewSmallCommandBar(["打开官网", "Windows 应用商店版", "发送反馈", "查看日志"]));
        root.Children.Add(NewCard(content));
        root.Children.Add(NewInfoCard("Project Page", "https://github.com/TransposonY/GestureSign", "Thanks: highsign, MahApps.Metro, WGestures."));
        return root;
    }

    private StackPanel NewSection() => new() { Spacing = 14 };

    private StackPanel NewCardPanel(double spacing = 6) => new() { Spacing = spacing };

    private FrameworkElement NewDialogField(string title, string description, FrameworkElement control)
    {
        var panel = NewCardPanel(4);
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = BodyStrongTextBlockStyle,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Opacity = 0.68,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        control.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(control);
        return panel;
    }

    private static Grid NewTwoColumnRow(FrameworkElement left, FrameworkElement right)
    {
        var row = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Left;
        right.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(left);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        return row;
    }

    private Border NewCard(UIElement content, Thickness? padding = null)
    {
        return new Border
        {
            Padding = padding ?? new Thickness(20),
            Background = CardBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
    }

    private SolidColorBrush CardBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(24, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(235, 250, 252, 255));
    }

    private SolidColorBrush SubtleBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(255, 242, 246, 250));
    }

    private SolidColorBrush SelectionBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(42, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(255, 228, 239, 249));
    }

    private SolidColorBrush BorderBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(255, 224, 228, 233));
    }

    private FrameworkElement NewRecognitionCard()
    {
        return NewRecognitionCardDynamic();
    }

    private FrameworkElement NewRecognitionCardDynamic()
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = L("手势识别", "Gesture Recognition", "手勢辨識", "ジェスチャ認識", "제스처 인식"), Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = L("移动到动作页后，这里负责控制后台识别服务的启停。", "After opening Actions, this controls the background recognition service.", "移到動作頁後，這裡負責控制背景辨識服務的啟停。", "アクションページでは、ここでバックグラウンド認識サービスを制御します。", "동작 페이지에서 백그라운드 인식 서비스 시작/중지를 제어합니다."), Opacity = 0.68 });
        grid.Children.Add(text);

        var toggle = new ToggleSwitch
        {
            IsOn = _recognitionEnabled,
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += async (_, _) =>
        {
            if (_updatingRecognitionToggle)
                return;

            _recognitionEnabled = toggle.IsOn;
            var command = toggle.IsOn ? DaemonCommand.EnableRecognition : DaemonCommand.DisableRecognition;
            if (!await NotifyDaemonAsync(command))
            {
                _recognitionEnabled = !toggle.IsOn;
                _updatingRecognitionToggle = true;
                try
                {
                    toggle.IsOn = _recognitionEnabled;
                }
                finally
                {
                    _updatingRecognitionToggle = false;
                }
            }
        };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return NewCard(grid);
    }

    private FrameworkElement NewRecognitionCardLegacy()
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = L("手势识别", "Gesture Recognition", "手勢辨識", "ジェスチャ認識", "제스처 인식"), Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = L("移动到动作页后，这里负责控制后台识别服务的启停。", "After opening Actions, this controls the background recognition service.", "移到動作頁後，這裡負責控制背景辨識服務的啟停。", "アクションページでは、ここでバックグラウンド認識サービスを制御します。", "동작 페이지에서 백그라운드 인식 서비스 시작/중지를 제어합니다."), Opacity = 0.68 });
        grid.Children.Add(text);

        var toggle = new TextBlock { Text = L("开", "On", "開", "オン", "켬"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return NewCard(grid);
    }

    private FrameworkElement NewCardHeader(string title, string subtitle, string buttonText)
        => NewCardHeader(title, subtitle, buttonText, buttonText);

    private FrameworkElement NewCardHeader(string title, string subtitle, string buttonText, string command)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = title, Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);

        var button = NewPillButton(buttonText, true, command);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private FrameworkElement NewSmallCommandBar(string[] commands)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var command in commands)
            panel.Children.Add(NewPillButton(command));
        return panel;
    }

    private FrameworkElement NewSmallCommandBar((string Text, string Command)[] commands)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var command in commands)
            panel.Children.Add(NewPillButton(command.Text, true, command.Command));
        return panel;
    }

    private FrameworkElement NewRunningProcessPicker(TextBox name, TextBox matchText, ComboBox matchUsing)
    {
        var processes = Process.GetProcesses()
            .Where(process => !string.IsNullOrWhiteSpace(process.ProcessName))
            .Select(process =>
            {
                try
                {
                    return new RunningProcessInfo(process.ProcessName, Path.GetFileName(process.MainModule?.FileName ?? $"{process.ProcessName}.exe"));
                }
                catch
                {
                    return new RunningProcessInfo(process.ProcessName, $"{process.ProcessName}.exe");
                }
            })
            .DistinctBy(process => process.FileName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(process => process.Name)
            .Take(160)
            .ToList();

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var combo = new ComboBox { Width = 240, PlaceholderText = L("选择运行中的程序", "Select a running app", "選擇執行中的程式", "実行中のアプリを選択", "실행 중인 프로그램 선택") };
        foreach (var process in processes)
            combo.Items.Add($"{process.Name} ({process.FileName})");
        var apply = NewPillButton(L("使用", "Use", "使用", "使用", "사용"), false);
        apply.Click += (_, _) =>
        {
            var selected = combo.SelectedIndex >= 0 && combo.SelectedIndex < processes.Count ? processes[combo.SelectedIndex] : null;
            if (selected is null)
                return;
            name.Text = selected.Name;
            matchText.Text = selected.FileName;
            matchUsing.SelectedIndex = 1;
        };
        panel.Children.Add(combo);
        panel.Children.Add(apply);
        return panel;
    }

    private FrameworkElement NewWindowPicker(TextBox name, TextBox matchText, ComboBox matchUsing)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var mode = new ComboBox { Width = 120, SelectedIndex = 0 };
        mode.Items.Add("exe");
        mode.Items.Add("标题");
        mode.Items.Add("类名");
        var pick = NewPillButton("拾取鼠标所在窗口", false);
        pick.Click += async (_, _) =>
        {
            pick.IsEnabled = false;
            pick.Content = "移动鼠标并左键单击目标窗口";
            var info = await PickWindowByClickAsync();
            pick.Content = "拾取鼠标所在窗口";
            pick.IsEnabled = true;
            if (info is null)
            {
                await ShowInfoDialog("拾取失败", "没有拾取到目标窗口。请重新点击拾取，然后在目标窗口上左键单击。");
                return;
            }

            name.Text = string.IsNullOrWhiteSpace(info.Title) ? info.FileName : info.Title;
            matchUsing.SelectedIndex = mode.SelectedIndex switch { 1 => 0, 2 => 2, _ => 1 };
            matchText.Text = mode.SelectedIndex switch { 1 => info.Title, 2 => info.ClassName, _ => info.FileName };
        };
        panel.Children.Add(mode);
        panel.Children.Add(pick);
        return panel;
    }

    private FrameworkElement NewConditionBuilder(TextBox condition)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var finger = new ComboBox { Width = 90, SelectedIndex = 0 };
        for (var i = 1; i <= 10; i++)
            finger.Items.Add($"{i}");
        var variable = new ComboBox { Width = 130, SelectedIndex = 0 };
        foreach (var item in new[] { "start_X", "start_X%", "start_Y", "start_Y%", "end_X", "end_X%", "end_Y", "end_Y%", "ID" })
            variable.Items.Add(item);
        var insert = NewPillButton("插入变量", false);
        insert.Click += (_, _) =>
        {
            var token = $"finger_{finger.SelectedItem}_{variable.SelectedItem}";
            var index = Math.Clamp(condition.SelectionStart, 0, condition.Text.Length);
            condition.Text = condition.Text.Insert(index, token);
            condition.SelectionStart = index + token.Length;
            condition.Focus(FocusState.Programmatic);
        };
        panel.Children.Add(finger);
        panel.Children.Add(variable);
        panel.Children.Add(insert);
        return panel;
    }

    private Button NewPillButton(string text)
        => NewPillButton(text, true);

    private Button NewPillButton(string text, bool attachDefaultHandler)
        => NewPillButton(text, attachDefaultHandler, text);

    private Button NewPillButton(string text, bool attachDefaultHandler, string command)
    {
        var button = new Button
        {
            Content = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (attachDefaultHandler)
            button.Click += (_, _) => HandleCommand(command);
        return button;
    }

    private FrameworkElement NewInlineButtons(params (string Text, Func<Task> Action)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in buttons)
        {
            var button = NewPillButton(item.Text, false);
            button.Click += async (_, _) => await RunUiActionAsync(item.Action);
            panel.Children.Add(button);
        }

        return panel;
    }

    private FrameworkElement NewInlineButtonsWithContext(params (string Text, Func<Button, Task> Action)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in buttons)
        {
            var button = NewPillButton(item.Text, false);
            button.Click += async (_, _) => await RunUiActionAsync(() => item.Action(button));
            panel.Children.Add(button);
        }

        return panel;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogException(ex);
            await ShowInfoDialog("操作失败", ex.Message);
        }
    }

    private void LogException(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(_legacyData.LocalPath);
            File.AppendAllText(Path.Combine(_legacyData.LocalPath, "GestureSign.WinUI.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
        }
        catch
        {
        }
    }

    private async void HandleCommand(string command)
    {
        try
        {
            switch (command)
            {
                case "打开配置文件夹":
                    Directory.CreateDirectory(_legacyData.RoamingPath);
                    Process.Start(new ProcessStartInfo("explorer.exe", _legacyData.RoamingPath) { UseShellExecute = true });
                    break;
                case "备份":
                    var backupPath = _legacyData.CreateBackup();
                    await ShowInfoDialog("备份完成", backupPath);
                    break;
                case "恢复":
                    await RestoreArchiveAsync();
                    break;
                case "退出":
                case "Exit":
                case "結束":
                case "終了":
                case "종료":
                    await ExitAllGestureSignProcessesAsync();
                    break;
                case "导入":
                    await ImportActionsAsync();
                    break;
                case "导出":
                    await ExportActionsAsync();
                    break;
                case "添加程序":
                    await AddApplicationAsync(false);
                    break;
                case "添加忽略项":
                    await AddApplicationAsync(true);
                    break;
                case "新动作":
                    await AddActionAsync(ResolveDefaultActionTarget());
                    break;
                case "导入手势文件":
                    await ImportGesturesAsync();
                    break;
                case "导出手势文件":
                    await ExportGesturesAsync();
                    break;
                case "新建手势":
                    await AddGestureAsync();
                    break;
                case "绘制手势":
                    await DrawGestureAsync(null);
                    break;
                case "后台训练手势":
                    await StartDaemonGestureTrainingAsync();
                    break;
                case "下载列表":
                    await DownloadSharedSettingsAsync();
                    break;
                case "查看日志":
                    await ShowLogAsync();
                    break;
                case "发送反馈":
                    await SendFeedbackAsync();
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            await ShowInfoDialog("操作失败", ex.Message);
        }
    }

    private async Task AddApplicationAsync(bool ignored)
    {
        var name = new TextBox { PlaceholderText = ignored ? "忽略项名称" : "程序名称", Text = ignored ? "新忽略项" : "新程序" };
        var matchText = new TextBox { PlaceholderText = "窗口标题、类名或 exe", Margin = new Thickness(0, 8, 0, 0) };
        var group = new TextBox { PlaceholderText = "分组，可留空", Margin = new Thickness(0, 8, 0, 0) };
        var matchUsing = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 1 };
        matchUsing.Items.Add("窗口标题");
        matchUsing.Items.Add("可执行文件");
        matchUsing.Items.Add("窗口类");
        var regex = new CheckBox { Content = "使用正则匹配", Margin = new Thickness(0, 8, 0, 0) };
        var panel = NewCardPanel(12);
        panel.Children.Add(NewDialogField("程序名称", "用于在动作页列表中显示，建议填写容易识别的名称。", name));
        panel.Children.Add(NewDialogField("匹配文本", "后台会用这里的文本匹配窗口。可执行文件示例: msedge.exe；多个程序可用 | 分隔。", matchText));
        panel.Children.Add(NewDialogField("从运行中程序选择", "自动填入程序名称和可执行文件名，适合普通桌面程序。", NewRunningProcessPicker(name, matchText, matchUsing)));
        panel.Children.Add(NewDialogField("拾取窗口", "点击后在目标窗口上单击，可按 exe、标题或类名提取匹配信息。", NewWindowPicker(name, matchText, matchUsing)));
        if (!ignored)
            panel.Children.Add(NewDialogField("分组", "可留空。相同分组会在动作页中归在一起，方便管理。", group));
        panel.Children.Add(NewDialogField("匹配方式", "可执行文件最稳定；窗口标题适合标题固定的窗口；窗口类适合系统窗口或特殊程序。", matchUsing));
        panel.Children.Add(NewDialogField("正则匹配", "开启后匹配文本会作为正则表达式处理，例如 chrome|firefox 可匹配多个浏览器。", regex));

        if (!await ConfirmDialogAsync(ignored ? "添加忽略项" : "添加程序", panel, "添加"))
            return;

        var matchUsingValue = matchUsing.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 0, _ => 2 };
        if (ignored)
            _legacyData.AddIgnoredApplication(name.Text, matchUsingValue, matchText.Text, regex.IsChecked ?? false);
        else
            _legacyData.AddUserApplication(name.Text, matchUsingValue, matchText.Text, group.Text, regex.IsChecked ?? false);
        ReloadData();
    }

    private async Task EditApplicationAsync(LegacyApplication app)
    {
        var name = new TextBox { PlaceholderText = "名称", Text = app.Name };
        var matchText = new TextBox { PlaceholderText = "窗口标题、类名或 exe", Text = app.MatchString, Margin = new Thickness(0, 8, 0, 0) };
        var group = new TextBox { PlaceholderText = "分组，可留空", Text = app.Group, Margin = new Thickness(0, 8, 0, 0) };
        var matchUsing = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = app.MatchUsing switch { 1 => 0, 0 => 2, 3 => 2, _ => 1 } };
        matchUsing.Items.Add("窗口标题");
        matchUsing.Items.Add("可执行文件");
        matchUsing.Items.Add("窗口类");
        var regex = new CheckBox { Content = "使用正则匹配", IsChecked = app.IsRegEx, Margin = new Thickness(0, 8, 0, 0) };
        var enabled = new CheckBox { Content = "启用", IsChecked = app.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var limitFingers = new TextBox { PlaceholderText = "限制手指数，0 表示不限", Text = app.LimitNumberOfFingers.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };
        var blockThreshold = new TextBox { PlaceholderText = "触摸阻断阈值", Text = app.BlockTouchInputThreshold.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };

        var panel = NewCardPanel(12);
        panel.Children.Add(NewDialogField("程序名称", "用于在动作页列表中显示，建议填写容易识别的名称。", name));
        panel.Children.Add(NewDialogField("匹配文本", "后台会用这里的文本匹配窗口。可执行文件示例: msedge.exe；多个程序可用 | 分隔。", matchText));
        panel.Children.Add(NewDialogField("从运行中程序选择", "自动填入程序名称和可执行文件名，适合普通桌面程序。", NewRunningProcessPicker(name, matchText, matchUsing)));
        panel.Children.Add(NewDialogField("拾取窗口", "点击后在目标窗口上单击，可按 exe、标题或类名提取匹配信息。", NewWindowPicker(name, matchText, matchUsing)));
        if (app.Type != "忽略")
        {
            panel.Children.Add(NewDialogField("分组", "可留空。相同分组会在动作页中归在一起，方便管理。", group));
            panel.Children.Add(NewDialogField("限制手指数", "该程序允许识别的最大触点数。填 0 表示不限制；填 2 表示只响应 1 指和 2 指手势，忽略更多触点。", limitFingers));
            panel.Children.Add(NewDialogField("触摸阻断阈值", "触摸屏/触控板专用。开始手势后达到这个触点数时阻止原始触摸输入，避免页面同时滚动或点击；鼠标手势通常不受影响。", blockThreshold));
        }
        panel.Children.Add(NewDialogField("匹配方式", "可执行文件最稳定；窗口标题适合标题固定的窗口；窗口类适合系统窗口或特殊程序。", matchUsing));
        panel.Children.Add(NewDialogField("正则匹配", "开启后匹配文本会作为正则表达式处理，例如 chrome|firefox 可匹配多个浏览器。", regex));
        panel.Children.Add(NewDialogField("启用状态", "关闭后该程序分组不会参与手势匹配，已有动作会保留。", enabled));

        if (!await ConfirmDialogAsync($"编辑 {app.Name}", panel, "保存"))
            return;

        var matchUsingValue = matchUsing.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 0, _ => 2 };
        _legacyData.UpdateApplication(app, name.Text, matchUsingValue, matchText.Text, group.Text, regex.IsChecked ?? false, enabled.IsChecked ?? true, ParseInt(limitFingers.Text, app.LimitNumberOfFingers), ParseInt(blockThreshold.Text, app.BlockTouchInputThreshold));
        ReloadData();
    }

    private async Task DeleteApplicationAsync(LegacyApplication app)
    {
        if (!await ConfirmDialogAsync("删除确认", $"确定删除 {app.Name}？", "删除"))
            return;
        _legacyData.DeleteApplication(app);
        ReloadData();
    }

    private async Task ToggleEnabledAsync(System.Text.Json.Nodes.JsonObject source, Button? toggleButton = null)
    {
        var isEnabled = source.BoolValue("IsEnabled", true);
        var newEnabled = !isEnabled;
        _legacyData.SetEnabled(source, newEnabled);
        if (toggleButton != null)
            toggleButton.Content = newEnabled ? "停用" : "启用";
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private async Task AddActionAsync(LegacyApplication? app)
    {
        if (app is null)
        {
            await ShowInfoDialog("没有可用程序", "请先添加一个程序。");
            return;
        }

        var name = new TextBox { PlaceholderText = "动作名称", Text = "新动作" };
        var gesture = new TextBox { PlaceholderText = "手势名称，例如 3Right", Margin = new Thickness(0, 8, 0, 0) };
        var drawnPointPatterns = new List<List<(double X, double Y)>>();
        var drawPanel = NewInlineGestureDrawingPanel(drawnPointPatterns, out var showRecordedGesture, out var clearGestureButton);
        var trainingStatus = new TextBlock { Text = "可以直接绘制单指或多指图案，也可以用触控板录制真实轨迹。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trainByTouchpad = NewPillButton("用触控板或触控录制", false);
        trainByTouchpad.Click += async (_, _) =>
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            SetGestureText(gesture, gestureName);
            await StartGestureTrainingForNameAsync(gestureName, trainingStatus, showRecordedGesture);
        };
        var commandName = new TextBox { PlaceholderText = "命令名称", Text = "发送快捷键", Margin = new Thickness(0, 8, 0, 0) };
        var commandPlugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 0 };
        AddPluginItems(commandPlugin);
        var commandPluginClass = new TextBox { PlaceholderText = "自定义插件类名", Text = PluginClassFromIndex(0), Margin = new Thickness(0, 8, 0, 0) };
        var commandSettings = new TextBox { PlaceholderText = "命令设置 JSON，可留空", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var commandHotkey = NewHotKeyRecorder(commandSettings, "");
        var commandAppPicker = NewCommandAppPicker(commandPlugin, commandPluginClass, commandSettings);
        var commandTypedSettings = NewTypedCommandSettingsEditor(commandPluginClass, commandSettings);
        var commandPreview = NewCardPanel(6);
        void RefreshCommandPreview()
        {
            commandPreview.Children.Clear();
            var pluginClassValue = commandPluginClass.Text.Trim();
            var commandTitle = string.IsNullOrWhiteSpace(commandName.Text)
                ? PluginName(pluginClassValue)
                : DisplayName(commandName.Text);
            var commandSubtitle = CommandPreviewText(pluginClassValue, commandSettings.Text);
            commandPreview.Children.Add(NewListRow(commandTitle, commandSubtitle, null));
        }
        void UpdateCommandEditor()
        {
            var pluginClassValue = PluginClassFromIndex(commandPlugin.SelectedIndex);
            commandPluginClass.Text = pluginClassValue;
            if (!pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(commandSettings.Text))
                commandSettings.Text = PluginSettingsTemplate(pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, commandPluginClass, commandHotkey, commandSettings, commandAppPicker);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, pluginClassValue, commandSettings.Text);
            RefreshCommandPreview();
        }
        commandPlugin.SelectionChanged += (_, _) =>
        {
            var pluginClassValue = PluginClassFromIndex(commandPlugin.SelectedIndex);
            commandPluginClass.Text = pluginClassValue;
            commandSettings.Text = pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) ? "" : PluginSettingsTemplate(pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, commandPluginClass, commandHotkey, commandSettings, commandAppPicker);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, pluginClassValue, commandSettings.Text);
            RefreshCommandPreview();
        };
        commandName.TextChanged += (_, _) => RefreshCommandPreview();
        commandPluginClass.TextChanged += (_, _) =>
        {
            UpdateCommandEditorVisibility(commandPluginClass.Text, commandPluginClass, commandHotkey, commandSettings, commandAppPicker);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, commandPluginClass.Text, commandSettings.Text);
            RefreshCommandPreview();
        };
        commandSettings.TextChanged += (_, _) => RefreshCommandPreview();
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        panel.Children.Add(NewBuiltInGesturePicker(gesture));
        panel.Children.Add(new TextBlock { Text = "手势图案", Opacity = 0.68, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(drawPanel);
        panel.Children.Add(NewTwoColumnRow(clearGestureButton, trainByTouchpad));
        panel.Children.Add(trainingStatus);
        panel.Children.Add(new TextBlock { Text = "要执行的命令", Opacity = 0.68, Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(commandName);
        panel.Children.Add(commandPlugin);
        panel.Children.Add(commandPluginClass);
        panel.Children.Add(commandHotkey);
        panel.Children.Add(commandAppPicker);
        panel.Children.Add(commandTypedSettings);
        panel.Children.Add(commandSettings);
        panel.Children.Add(commandPreview);
        UpdateCommandEditor();
        if (!await ConfirmDialogAsync($"给 {app.Name} 添加动作", panel, "添加"))
            return;

        var validDrawnPointPatterns = drawnPointPatterns
            .Where(pattern => pattern.Count >= 2)
            .Cast<IReadOnlyList<(double X, double Y)>>()
            .ToList();
        if (validDrawnPointPatterns.Count > 0)
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            var existingGesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, gestureName, StringComparison.OrdinalIgnoreCase));
            if (existingGesture is null)
                _legacyData.AddGestureFromPointPatterns(gestureName, validDrawnPointPatterns);
            else
                _legacyData.UpdateGesturePointPatterns(existingGesture, validDrawnPointPatterns);
            SetGestureText(gesture, gestureName);
            _legacyData = LegacyDataStore.Load();
        }

        var finalGestureName = ResolveGestureName(gesture, "");
        if (string.IsNullOrWhiteSpace(finalGestureName))
        {
            await ShowInfoDialog("缺少手势", "请先选择、输入或绘制一个手势。");
            return;
        }
        SetGestureText(gesture, finalGestureName);

        var targetApp = FindMatchingApplication(app);
        if (targetApp is null)
        {
            await ShowInfoDialog("程序分组已变化", "刚才录制手势后配置已刷新，请重新打开该分组再添加动作。");
            ReloadData();
            return;
        }

        var actionName = name.Text;
        var gestureNameValue = finalGestureName;
        var commandPluginClassValue = commandPluginClass.Text.Trim();
        var commandSettingsValue = commandSettings.Text;
        var addInitialCommand = ShouldCreateCommand(commandPluginClassValue, commandSettingsValue);
        _legacyData.AddAction(targetApp, name.Text, finalGestureName);
        if (addInitialCommand)
        {
            _legacyData = LegacyDataStore.Load();
            targetApp = FindMatchingApplication(app);
            var createdAction = targetApp?.Actions.LastOrDefault(candidate =>
                string.Equals(candidate.Name, actionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.GestureName, gestureNameValue, StringComparison.OrdinalIgnoreCase));
            if (createdAction is not null)
                _legacyData.AddCommand(createdAction, commandName.Text, commandPluginClassValue, commandSettingsValue);
        }
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadData();
    }

    private async Task EditActionAsync(LegacyAction action)
    {
        var name = new TextBox { PlaceholderText = "动作名称", Text = DisplayName(action.Name) };
        var gesture = new TextBox { PlaceholderText = "手势名称", Margin = new Thickness(0, 8, 0, 0) };
        SetGestureText(gesture, action.GestureName);
        var condition = new TextBox { PlaceholderText = "触发条件，可留空", Text = action.Condition, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var enabled = new CheckBox { Content = "启用", IsChecked = action.IsEnabled };
        var activateWindow = new CheckBox { Content = "执行前激活目标窗口", IsChecked = action.ActivateWindow };
        var mouseHotkey = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = MouseActionIndex(action.MouseHotkey) };
        foreach (var item in new[] { "无鼠标快捷键", "滚轮前", "滚轮后", "左键", "右键", "中键", "X1 键", "X2 键" })
            mouseHotkey.Items.Add(item);
        var ignoredDevices = new TextBox { PlaceholderText = "忽略设备位掩码，0 表示全部设备可触发", Text = action.IgnoredDevices.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };
        var hotkeyJson = new TextBox { Text = action.HotkeyJson };
        var hotkeyRecorder = NewHotKeyRecorderWithClear(hotkeyJson, action.HotkeyJson, usesArrayKeyCode: false);
        var continuousGestureJson = new TextBox { PlaceholderText = "连续手势 JSON，可留空", Text = action.ContinuousGestureJson, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 64 };
        var drawnPointPatterns = new List<List<(double X, double Y)>>();
        var drawPanel = NewInlineGestureDrawingPanel(drawnPointPatterns, out var showRecordedGesture, out var clearGestureButton);
        var trainingStatus = new TextBlock { Text = "触控板或触控录制会使用后台识别服务捕捉真实多指轨迹。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trainByTouchpad = NewPillButton("用触控板或触控录制", false);
        trainByTouchpad.Click += async (_, _) =>
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            SetGestureText(gesture, gestureName);
            await StartGestureTrainingForNameAsync(gestureName, trainingStatus, showRecordedGesture);
        };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        panel.Children.Add(NewBuiltInGesturePicker(gesture));
        panel.Children.Add(new TextBlock { Text = "手势图案", Opacity = 0.68, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(drawPanel);
        panel.Children.Add(NewTwoColumnRow(clearGestureButton, trainByTouchpad));
        panel.Children.Add(trainingStatus);
        panel.Children.Add(NewTwoColumnRow(enabled, activateWindow));
        panel.Children.Add(mouseHotkey);
        panel.Children.Add(ignoredDevices);
        panel.Children.Add(hotkeyRecorder);
        // panel.Children.Add(continuousGestureJson);
        if (!await ConfirmDialogAsync($"编辑动作 {DisplayName(action.Name)}", panel, "保存"))
            return;

        var validDrawnPointPatterns = drawnPointPatterns
            .Where(pattern => pattern.Count >= 2)
            .Cast<IReadOnlyList<(double X, double Y)>>()
            .ToList();
        if (validDrawnPointPatterns.Count > 0)
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            var existingGesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, gestureName, StringComparison.OrdinalIgnoreCase));
            if (existingGesture is null)
                _legacyData.AddGestureFromPointPatterns(gestureName, validDrawnPointPatterns);
            else
                _legacyData.UpdateGesturePointPatterns(existingGesture, validDrawnPointPatterns);
            SetGestureText(gesture, gestureName);
        }

        _legacyData.UpdateAction(action, name.Text, ResolveGestureName(gesture, name.Text), condition.Text, enabled.IsChecked ?? true, activateWindow.IsChecked ?? true, MouseActionValue(mouseHotkey.SelectedIndex), ParseInt(ignoredDevices.Text, action.IgnoredDevices), hotkeyJson.Text, continuousGestureJson.Text);
        ReloadData();
    }

    private FrameworkElement NewBuiltInGesturePicker(TextBox gesture)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text)) };
        combo.Items.Add("选择内置触发方式");
        combo.Items.Add("触控板上边缘点击");
        combo.Items.Add("触控板下边缘点击");
        combo.Items.Add("触控板左边缘点击");
        combo.Items.Add("触控板右边缘点击");
        combo.Items.Add("触控板上边缘左滑");
        combo.Items.Add("触控板上边缘右滑");
        combo.Items.Add("触控板下边缘左滑");
        combo.Items.Add("触控板下边缘右滑");
        combo.Items.Add("触控板左边缘上滑");
        combo.Items.Add("触控板左边缘下滑");
        combo.Items.Add("触控板右边缘上滑");
        combo.Items.Add("触控板右边缘下滑");
        combo.Items.Add("触控屏上边缘点击");
        combo.Items.Add("触控屏下边缘点击");
        combo.Items.Add("触控屏左边缘点击");
        combo.Items.Add("触控屏右边缘点击");
        combo.Items.Add("触控屏上边缘左滑");
        combo.Items.Add("触控屏上边缘右滑");
        combo.Items.Add("触控屏下边缘左滑");
        combo.Items.Add("触控屏下边缘右滑");
        combo.Items.Add("触控屏左边缘上滑");
        combo.Items.Add("触控屏左边缘下滑");
        combo.Items.Add("触控屏右边缘上滑");
        combo.Items.Add("触控屏右边缘下滑");
        combo.Items.Clear();
        for (var index = 0; index <= 24; index++)
            combo.Items.Add(BuiltInGestureDisplayNameFromIndex(index));
        combo.SelectedIndex = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text));

        combo.SelectionChanged += (_, _) =>
        {
            var gestureName = BuiltInGestureNameFromIndex(combo.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(gestureName))
                SetGestureText(gesture, gestureName);
        };
        return combo;
    }

    private string ResolveGestureName(TextBox gesture, string fallback)
    {
        var text = (gesture.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        if (gesture.Tag is string tag && BuiltInGestureIndex(tag) > 0 &&
            (string.Equals(text, tag, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(text, BuiltInGestureDisplayName(tag), StringComparison.OrdinalIgnoreCase)))
            return tag;

        for (var index = 1; index <= 24; index++)
        {
            var gestureName = BuiltInGestureNameFromIndex(index);
            if (string.Equals(text, BuiltInGestureDisplayName(gestureName), StringComparison.OrdinalIgnoreCase))
                return gestureName;
        }

        return text;
    }

    private void SetGestureText(TextBox gesture, string gestureName)
    {
        gesture.Text = BuiltInGestureDisplayName(gestureName);
        gesture.Tag = BuiltInGestureIndex(gestureName) > 0 ? gestureName : null;
    }

    private string BuiltInGestureDisplayName(string gestureName)
    {
        var index = BuiltInGestureIndex(gestureName);
        return index > 0 ? BuiltInGestureDisplayNameFromIndex(index) : gestureName;
    }

    private string BuiltInGestureDisplayNameFromIndex(int index, string fallback = "")
        => index switch
        {
            0 => L("选择内置触发方式", "Choose built-in trigger", "選擇內建觸發方式", "組み込みトリガーを選択", "기본 제공 트리거 선택"),
            1 => L("触控板上边缘点击", "Touchpad top edge tap", "觸控板上邊緣點擊", "タッチパッド上端タップ", "터치패드 위쪽 가장자리 탭"),
            2 => L("触控板下边缘点击", "Touchpad bottom edge tap", "觸控板下邊緣點擊", "タッチパッド下端タップ", "터치패드 아래쪽 가장자리 탭"),
            3 => L("触控板左边缘点击", "Touchpad left edge tap", "觸控板左邊緣點擊", "タッチパッド左端タップ", "터치패드 왼쪽 가장자리 탭"),
            4 => L("触控板右边缘点击", "Touchpad right edge tap", "觸控板右邊緣點擊", "タッチパッド右端タップ", "터치패드 오른쪽 가장자리 탭"),
            5 => L("触控板上边缘左滑", "Touchpad top edge swipe left", "觸控板上邊緣左滑", "タッチパッド上端を左へスワイプ", "터치패드 위쪽 가장자리 왼쪽 스와이프"),
            6 => L("触控板上边缘右滑", "Touchpad top edge swipe right", "觸控板上邊緣右滑", "タッチパッド上端を右へスワイプ", "터치패드 위쪽 가장자리 오른쪽 스와이프"),
            7 => L("触控板下边缘左滑", "Touchpad bottom edge swipe left", "觸控板下邊緣左滑", "タッチパッド下端を左へスワイプ", "터치패드 아래쪽 가장자리 왼쪽 스와이프"),
            8 => L("触控板下边缘右滑", "Touchpad bottom edge swipe right", "觸控板下邊緣右滑", "タッチパッド下端を右へスワイプ", "터치패드 아래쪽 가장자리 오른쪽 스와이프"),
            9 => L("触控板左边缘上滑", "Touchpad left edge swipe up", "觸控板左邊緣上滑", "タッチパッド左端を上へスワイプ", "터치패드 왼쪽 가장자리 위로 스와이프"),
            10 => L("触控板左边缘下滑", "Touchpad left edge swipe down", "觸控板左邊緣下滑", "タッチパッド左端を下へスワイプ", "터치패드 왼쪽 가장자리 아래로 스와이프"),
            11 => L("触控板右边缘上滑", "Touchpad right edge swipe up", "觸控板右邊緣上滑", "タッチパッド右端を上へスワイプ", "터치패드 오른쪽 가장자리 위로 스와이프"),
            12 => L("触控板右边缘下滑", "Touchpad right edge swipe down", "觸控板右邊緣下滑", "タッチパッド右端を下へスワイプ", "터치패드 오른쪽 가장자리 아래로 스와이프"),
            13 => L("触摸屏上边缘点击", "Touchscreen top edge tap", "觸控螢幕上邊緣點擊", "タッチスクリーン上端タップ", "터치스크린 위쪽 가장자리 탭"),
            14 => L("触摸屏下边缘点击", "Touchscreen bottom edge tap", "觸控螢幕下邊緣點擊", "タッチスクリーン下端タップ", "터치스크린 아래쪽 가장자리 탭"),
            15 => L("触摸屏左边缘点击", "Touchscreen left edge tap", "觸控螢幕左邊緣點擊", "タッチスクリーン左端タップ", "터치스크린 왼쪽 가장자리 탭"),
            16 => L("触摸屏右边缘点击", "Touchscreen right edge tap", "觸控螢幕右邊緣點擊", "タッチスクリーン右端タップ", "터치스크린 오른쪽 가장자리 탭"),
            17 => L("触摸屏上边缘左滑", "Touchscreen top edge swipe left", "觸控螢幕上邊緣左滑", "タッチスクリーン上端を左へスワイプ", "터치스크린 위쪽 가장자리 왼쪽 스와이프"),
            18 => L("触摸屏上边缘右滑", "Touchscreen top edge swipe right", "觸控螢幕上邊緣右滑", "タッチスクリーン上端を右へスワイプ", "터치스크린 위쪽 가장자리 오른쪽 스와이프"),
            19 => L("触摸屏下边缘左滑", "Touchscreen bottom edge swipe left", "觸控螢幕下邊緣左滑", "タッチスクリーン下端を左へスワイプ", "터치스크린 아래쪽 가장자리 왼쪽 스와이프"),
            20 => L("触摸屏下边缘右滑", "Touchscreen bottom edge swipe right", "觸控螢幕下邊緣右滑", "タッチスクリーン下端を右へスワイプ", "터치스크린 아래쪽 가장자리 오른쪽 스와이프"),
            21 => L("触摸屏左边缘上滑", "Touchscreen left edge swipe up", "觸控螢幕左邊緣上滑", "タッチスクリーン左端を上へスワイプ", "터치스크린 왼쪽 가장자리 위로 스와이프"),
            22 => L("触摸屏左边缘下滑", "Touchscreen left edge swipe down", "觸控螢幕左邊緣下滑", "タッチスクリーン左端を下へスワイプ", "터치스크린 왼쪽 가장자리 아래로 스와이프"),
            23 => L("触摸屏右边缘上滑", "Touchscreen right edge swipe up", "觸控螢幕右邊緣上滑", "タッチスクリーン右端を上へスワイプ", "터치스크린 오른쪽 가장자리 위로 스와이프"),
            24 => L("触摸屏右边缘下滑", "Touchscreen right edge swipe down", "觸控螢幕右邊緣下滑", "タッチスクリーン右端を下へスワイプ", "터치스크린 오른쪽 가장자리 아래로 스와이프"),
            _ => fallback
        };

    private static int BuiltInGestureIndex(string gestureName)
        => gestureName switch
        {
            TouchPadEdgeTopGesture => 1,
            TouchPadEdgeBottomGesture => 2,
            TouchPadEdgeLeftGesture => 3,
            TouchPadEdgeRightGesture => 4,
            TouchPadEdgeTopLeftGesture => 5,
            TouchPadEdgeTopRightGesture => 6,
            TouchPadEdgeBottomLeftGesture => 7,
            TouchPadEdgeBottomRightGesture => 8,
            TouchPadEdgeLeftUpGesture => 9,
            TouchPadEdgeLeftDownGesture => 10,
            TouchPadEdgeRightUpGesture => 11,
            TouchPadEdgeRightDownGesture => 12,
            TouchScreenEdgeTopGesture => 13,
            TouchScreenEdgeBottomGesture => 14,
            TouchScreenEdgeLeftGesture => 15,
            TouchScreenEdgeRightGesture => 16,
            TouchScreenEdgeTopLeftGesture => 17,
            TouchScreenEdgeTopRightGesture => 18,
            TouchScreenEdgeBottomLeftGesture => 19,
            TouchScreenEdgeBottomRightGesture => 20,
            TouchScreenEdgeLeftUpGesture => 21,
            TouchScreenEdgeLeftDownGesture => 22,
            TouchScreenEdgeRightUpGesture => 23,
            TouchScreenEdgeRightDownGesture => 24,
            _ => 0
        };

    private static string BuiltInGestureNameFromIndex(int index)
        => index switch
        {
            1 => TouchPadEdgeTopGesture,
            2 => TouchPadEdgeBottomGesture,
            3 => TouchPadEdgeLeftGesture,
            4 => TouchPadEdgeRightGesture,
            5 => TouchPadEdgeTopLeftGesture,
            6 => TouchPadEdgeTopRightGesture,
            7 => TouchPadEdgeBottomLeftGesture,
            8 => TouchPadEdgeBottomRightGesture,
            9 => TouchPadEdgeLeftUpGesture,
            10 => TouchPadEdgeLeftDownGesture,
            11 => TouchPadEdgeRightUpGesture,
            12 => TouchPadEdgeRightDownGesture,
            13 => TouchScreenEdgeTopGesture,
            14 => TouchScreenEdgeBottomGesture,
            15 => TouchScreenEdgeLeftGesture,
            16 => TouchScreenEdgeRightGesture,
            17 => TouchScreenEdgeTopLeftGesture,
            18 => TouchScreenEdgeTopRightGesture,
            19 => TouchScreenEdgeBottomLeftGesture,
            20 => TouchScreenEdgeBottomRightGesture,
            21 => TouchScreenEdgeLeftUpGesture,
            22 => TouchScreenEdgeLeftDownGesture,
            23 => TouchScreenEdgeRightUpGesture,
            24 => TouchScreenEdgeRightDownGesture,
            _ => string.Empty
        };

    private async Task DeleteActionAsync(LegacyApplication app, LegacyAction action)
    {
        if (!await ConfirmDialogAsync("删除确认", $"确定删除动作 {action.Name}？", "删除"))
            return;
        _legacyData.DeleteAction(app, action);
        ReloadData();
    }

    private async Task AddCommandAsync(LegacyAction action)
    {
        var name = new TextBox { PlaceholderText = "命令名称", Text = "发送快捷键" };
        var plugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 0 };
        AddPluginItems(plugin);
        var pluginClass = new TextBox { PlaceholderText = "自定义插件类名", Text = PluginClassFromIndex(0), Margin = new Thickness(0, 8, 0, 0) };
        var settings = new TextBox { PlaceholderText = "命令设置 JSON，可留空", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var hotkey = NewHotKeyRecorder(settings, "");
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var typedSettings = NewTypedCommandSettingsEditor(pluginClass, settings);
        void UpdateEditor()
        {
            var pluginClassValue = PluginClassFromIndex(plugin.SelectedIndex);
            pluginClass.Text = pluginClassValue;
            if (!pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(settings.Text))
                settings.Text = PluginSettingsTemplate(pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, pluginClass, hotkey, settings, appPicker);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClassValue, settings.Text);
        }
        plugin.SelectionChanged += (_, _) =>
        {
            var pluginClassValue = PluginClassFromIndex(plugin.SelectedIndex);
            pluginClass.Text = pluginClassValue;
            settings.Text = pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) ? "" : PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClassValue, pluginClass, hotkey, settings, appPicker);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClassValue, settings.Text);
        };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        panel.Children.Add(settings);
        UpdateEditor();
        if (!await ConfirmDialogAsync($"给 {action.Name} 添加命令", panel, "添加"))
            return;

        _legacyData.AddCommand(action, name.Text, pluginClass.Text, settings.Text);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadActionDataOnly();
    }

    private async Task SetCommandAsync(LegacyAction action)
    {
        var command = action.Commands.FirstOrDefault();
        if (command is null)
            await AddCommandAsync(action);
        else
            await EditCommandAsync(command);
    }

    private async Task EditCommandAsync(LegacyCommand command)
    {
        var name = new TextBox { PlaceholderText = "命令名称", Text = command.Name };
        var plugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = PluginIndex(command.PluginClass) };
        AddPluginItems(plugin);
        var pluginClass = new TextBox { PlaceholderText = "自定义插件类名", Text = command.PluginClass, Margin = new Thickness(0, 8, 0, 0) };
        var settings = new TextBox { PlaceholderText = "命令设置 JSON，可留空", Text = command.Settings, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var hotkey = NewHotKeyRecorder(settings, command.Settings);
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var typedSettings = NewTypedCommandSettingsEditor(pluginClass, settings);
        plugin.SelectionChanged += (_, _) =>
        {
            var knownClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(knownClass))
                pluginClass.Text = knownClass;
            settings.Text = PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings, appPicker);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClass.Text, settings.Text);
        };
        var enabled = new CheckBox { Content = "启用", IsChecked = command.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        panel.Children.Add(settings);
        panel.Children.Add(enabled);
        UpdateCommandEditorVisibility(command.PluginClass, pluginClass, hotkey, settings, appPicker);
        UpdateTypedCommandSettingsEditor(typedSettings, command.PluginClass, settings.Text);
        if (!await ConfirmDialogAsync($"编辑命令 {command.Name}", panel, "保存"))
            return;

        _legacyData.UpdateCommand(command, name.Text, pluginClass.Text, settings.Text, enabled.IsChecked ?? true);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadActionDataOnly();
    }

    private async Task DeleteCommandAsync(LegacyAction action, LegacyCommand command)
    {
        if (!await ConfirmDialogAsync("删除确认", $"确定删除命令 {command.Name}？", "删除"))
            return;
        _legacyData.DeleteCommand(action, command);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadActionDataOnly();
    }

    private TextBox NewHotKeyRecorder(TextBox settings, string existingSettings, bool usesArrayKeyCode = true, Action<string>? onRecorded = null)
    {
        var recorder = new TextBox
        {
            PlaceholderText = L("单击这里，然后直接按下快捷键", "Click here, then press the shortcut", "按一下這裡，然後直接按下快速鍵", "ここをクリックしてショートカットを押してください", "여기를 클릭한 뒤 단축키를 누르세요"),
            Text = HotKeyDisplayText(existingSettings),
            Margin = new Thickness(0, 8, 0, 0),
            IsReadOnly = true
        };
        recorder.GotFocus += (_, _) => StartHotKeyRecording(recorder, settings, usesArrayKeyCode, onRecorded);
        recorder.LostFocus += (_, _) => StopHotKeyRecording();
        return recorder;
    }

    private FrameworkElement NewHotKeyRecorderWithClear(TextBox settings, string existingSettings, bool usesArrayKeyCode = true, Action<string>? onRecorded = null)
    {
        var recorder = NewHotKeyRecorder(settings, existingSettings, usesArrayKeyCode, onRecorded);
        recorder.Margin = new Thickness(0);
        recorder.HorizontalAlignment = HorizontalAlignment.Stretch;

        var clear = NewPillButton("清除", false);
        clear.Click += (_, _) =>
        {
            if (ReferenceEquals(_activeHotKeyRecorder, recorder))
                StopHotKeyRecording();
            settings.Text = "";
            recorder.Text = "";
            onRecorded?.Invoke("");
        };

        var panel = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnSpacing = 8
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(recorder);
        Grid.SetColumn(clear, 1);
        panel.Children.Add(clear);
        return panel;
    }

    private sealed class AppCommandChoice
    {
        private AppCommandChoice(string name, string target, AppCommandKind kind)
        {
            Name = name;
            Target = target;
            Kind = kind;
        }

        public string Name { get; }

        public string Target { get; }

        public AppCommandKind Kind { get; }

        public static AppCommandChoice Desktop(string name, string path) => new(name, path, AppCommandKind.Desktop);

        public static AppCommandChoice Uwp(string name, string appUserModelId) => new(name, appUserModelId, AppCommandKind.Uwp);

        public bool SameTarget(AppCommandChoice other)
            => Kind == other.Kind && string.Equals(Target, other.Target, StringComparison.OrdinalIgnoreCase);

        public override string ToString()
            => Kind == AppCommandKind.Desktop ? $"{Name}  ·  {Path.GetFileName(Target)}" : $"{Name}  ·  UWP";
    }

    private enum AppCommandKind
    {
        Desktop,
        Uwp
    }

    private FrameworkElement NewCommandAppPicker(ComboBox plugin, TextBox pluginClass, TextBox settings)
    {
        var combo = new ComboBox
        {
            PlaceholderText = "选择已安装应用",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var choices = GetInstalledApplicationChoices().ToList();
        if (TryCreateChoiceFromSettings(pluginClass.Text, settings.Text, out var current)
            && current is not null
            && choices.All(item => !item.SameTarget(current)))
        {
            choices.Insert(0, current);
        }

        foreach (var choice in choices)
            combo.Items.Add(choice);

        if (current is not null)
        {
            combo.SelectedItem = combo.Items.OfType<AppCommandChoice>().FirstOrDefault(item => item.SameTarget(current));
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not AppCommandChoice choice)
                return;

            ApplyAppCommandChoice(choice, plugin, pluginClass, settings);
        };

        var browse = NewPillButton("浏览 EXE", false);
        browse.Click += async (_, _) =>
        {
            var path = await PickOpenFileAsync(new[] { ".exe" });
            if (string.IsNullOrWhiteSpace(path))
                return;

            var choice = AppCommandChoice.Desktop(Path.GetFileNameWithoutExtension(path), path);
            if (combo.Items.OfType<AppCommandChoice>().All(item => !item.SameTarget(choice)))
                combo.Items.Insert(0, choice);
            combo.SelectedItem = combo.Items.OfType<AppCommandChoice>().First(item => item.SameTarget(choice));
            ApplyAppCommandChoice(choice, plugin, pluginClass, settings);
        };

        var grid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(combo);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private static void ApplyAppCommandChoice(AppCommandChoice choice, ComboBox plugin, TextBox pluginClass, TextBox settings)
    {
        if (choice.Kind == AppCommandKind.Uwp)
        {
            plugin.SelectedIndex = 4;
            pluginClass.Text = PluginClassFromIndex(4);
            settings.Text = LaunchAppSettingsJson(choice.Target, choice.Name);
            return;
        }

        plugin.SelectedIndex = 2;
        pluginClass.Text = PluginClassFromIndex(2);
        settings.Text = RunCommandSettingsJson(choice.Target);
    }

    private static bool TryCreateChoiceFromSettings(string pluginClass, string settings, out AppCommandChoice? choice)
    {
        choice = null;
        try
        {
            if (JsonNode.Parse(settings) is not JsonObject root)
                return false;

            if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            {
                var key = root.StringValue("Key", "");
                var value = root.StringValue("Value", "");
                if (string.IsNullOrWhiteSpace(key))
                    return false;
                choice = AppCommandChoice.Uwp(string.IsNullOrWhiteSpace(value) ? key : value, key);
                return true;
            }

            if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            {
                var command = root.StringValue("Command", "");
                var path = ExtractExecutablePath(command);
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                choice = AppCommandChoice.Desktop(Path.GetFileNameWithoutExtension(path), path);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private sealed class TypedCommandSettingsEditor
    {
        public required StackPanel Root { get; init; }

        public required StackPanel VolumePanel { get; init; }

        public required ComboBox VolumeMethod { get; init; }

        public required TextBox VolumePercent { get; init; }

        public required StackPanel BrightnessPanel { get; init; }

        public required ComboBox BrightnessMethod { get; init; }

        public required TextBox BrightnessPercent { get; init; }

        public required StackPanel OpenFilePanel { get; init; }

        public required TextBox OpenFilePath { get; init; }

        public required TextBox OpenFileVariables { get; init; }

        public required StackPanel RunCommandPanel { get; init; }

        public required TextBox RunCommandText { get; init; }

        public required ComboBox RunCommandShell { get; init; }

        public required CheckBox RunCommandAdministrator { get; init; }

        public required CheckBox RunCommandShowWindow { get; init; }

        public bool Updating { get; set; }
    }

    private FrameworkElement NewTypedCommandSettingsEditor(TextBox pluginClass, TextBox settings)
    {
        var root = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var volumeMethod = NewInlineComboBox(["增大音量", "减小音量", "静音"], 0);
        var volumePercent = new TextBox
        {
            Header = "变化量",
            PlaceholderText = "百分比",
            Text = "10"
        };
        var volumePanel = NewCommandSettingsPanel(volumeMethod, volumePercent);

        var brightnessMethod = NewInlineComboBox(["增大亮度", "减小亮度"], 0);
        var brightnessPercent = new TextBox
        {
            Header = "变化量",
            PlaceholderText = "百分比",
            Text = "10"
        };
        var brightnessPanel = NewCommandSettingsPanel(brightnessMethod, brightnessPercent);

        var openFilePath = new TextBox
        {
            PlaceholderText = "选择要打开的文件",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var browseOpenFile = NewPillButton("浏览", false);
        browseOpenFile.Click += async (_, _) =>
        {
            var path = await PickOpenFileAsync(["*"]);
            if (string.IsNullOrWhiteSpace(path))
                return;

            openFilePath.Text = path;
            SyncTypedCommandSettings(root, pluginClass.Text, settings);
        };

        var openFileGrid = new Grid { ColumnSpacing = 8 };
        openFileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        openFileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        openFileGrid.Children.Add(openFilePath);
        Grid.SetColumn(browseOpenFile, 1);
        openFileGrid.Children.Add(browseOpenFile);

        var openFileVariables = new TextBox
        {
            PlaceholderText = "启动参数，可留空"
        };
        var openFilePanel = new StackPanel { Spacing = 8 };
        openFilePanel.Children.Add(openFileGrid);
        openFilePanel.Children.Add(openFileVariables);

        var runCommandText = new TextBox
        {
            PlaceholderText = "输入要执行的命令；多行命令会按顺序执行",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 140
        };
        var runCommandShell = NewInlineComboBox(["CMD", "PowerShell"], 0);
        var runCommandAdministrator = new CheckBox { Content = "管理员权限" };
        var runCommandShowWindow = new CheckBox { Content = "显示窗口" };
        var runCommandOptions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };
        runCommandOptions.Children.Add(runCommandShell);
        runCommandOptions.Children.Add(runCommandAdministrator);
        runCommandOptions.Children.Add(runCommandShowWindow);
        var runCommandPanel = new StackPanel { Spacing = 8 };
        runCommandPanel.Children.Add(runCommandText);
        runCommandPanel.Children.Add(runCommandOptions);

        root.Children.Add(runCommandPanel);
        root.Children.Add(volumePanel);
        root.Children.Add(brightnessPanel);
        root.Children.Add(openFilePanel);

        var editor = new TypedCommandSettingsEditor
        {
            Root = root,
            VolumePanel = volumePanel,
            VolumeMethod = volumeMethod,
            VolumePercent = volumePercent,
            BrightnessPanel = brightnessPanel,
            BrightnessMethod = brightnessMethod,
            BrightnessPercent = brightnessPercent,
            OpenFilePanel = openFilePanel,
            OpenFilePath = openFilePath,
            OpenFileVariables = openFileVariables,
            RunCommandPanel = runCommandPanel,
            RunCommandText = runCommandText,
            RunCommandShell = runCommandShell,
            RunCommandAdministrator = runCommandAdministrator,
            RunCommandShowWindow = runCommandShowWindow
        };
        _typedCommandSettingsEditors[root] = editor;

        volumeMethod.SelectionChanged += (_, _) =>
        {
            UpdateVolumePercentVisibility(editor);
            SyncTypedCommandSettings(root, pluginClass.Text, settings);
        };
        volumePercent.TextChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        brightnessMethod.SelectionChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        brightnessPercent.TextChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        openFilePath.TextChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        openFileVariables.TextChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandText.TextChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandShell.SelectionChanged += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandAdministrator.Checked += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandAdministrator.Unchecked += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandShowWindow.Checked += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);
        runCommandShowWindow.Unchecked += (_, _) => SyncTypedCommandSettings(root, pluginClass.Text, settings);

        return root;
    }

    private static StackPanel NewCommandSettingsPanel(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static ComboBox NewInlineComboBox(string[] items, int selectedIndex)
    {
        var combo = new ComboBox
        {
            SelectedIndex = selectedIndex,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 150
        };
        foreach (var item in items)
            combo.Items.Add(item);
        return combo;
    }

    private void UpdateTypedCommandSettingsEditor(FrameworkElement root, string pluginClass, string settings)
    {
        if (!_typedCommandSettingsEditors.TryGetValue(root, out var editor))
            return;

        var typed = TypedCommandSettingsKind(pluginClass);
        root.Visibility = typed == CommandSettingsKind.None ? Visibility.Collapsed : Visibility.Visible;
        editor.RunCommandPanel.Visibility = typed == CommandSettingsKind.RunCommand ? Visibility.Visible : Visibility.Collapsed;
        editor.VolumePanel.Visibility = typed == CommandSettingsKind.Volume ? Visibility.Visible : Visibility.Collapsed;
        editor.BrightnessPanel.Visibility = typed == CommandSettingsKind.Brightness ? Visibility.Visible : Visibility.Collapsed;
        editor.OpenFilePanel.Visibility = typed == CommandSettingsKind.OpenFile ? Visibility.Visible : Visibility.Collapsed;

        editor.Updating = true;
        try
        {
            if (typed == CommandSettingsKind.Volume)
            {
                editor.VolumeMethod.SelectedIndex = Math.Clamp(JsonIntValue(settings, "Method", 0), 0, 2);
                editor.VolumePercent.Text = JsonIntValue(settings, "Percent", 10).ToString(CultureInfo.InvariantCulture);
                UpdateVolumePercentVisibility(editor);
            }
            else if (typed == CommandSettingsKind.RunCommand)
            {
                editor.RunCommandText.Text = JsonStringValue(settings, "Command", "");
                editor.RunCommandShell.SelectedIndex = string.Equals(JsonStringValue(settings, "Shell", "CMD"), "PowerShell", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                editor.RunCommandAdministrator.IsChecked = JsonBoolValue(settings, "RunAsAdministrator", false);
                editor.RunCommandShowWindow.IsChecked = JsonBoolValue(settings, "ShowCmd", false);
            }
            else if (typed == CommandSettingsKind.Brightness)
            {
                editor.BrightnessMethod.SelectedIndex = Math.Clamp(JsonIntValue(settings, "Method", 0), 0, 1);
                editor.BrightnessPercent.Text = JsonIntValue(settings, "Percent", 10).ToString(CultureInfo.InvariantCulture);
            }
            else if (typed == CommandSettingsKind.OpenFile)
            {
                editor.OpenFilePath.Text = JsonStringValue(settings, "Path", "");
                editor.OpenFileVariables.Text = JsonStringValue(settings, "Variables", "");
            }
        }
        finally
        {
            editor.Updating = false;
        }
    }

    private void SyncTypedCommandSettings(FrameworkElement root, string pluginClass, TextBox settings)
    {
        if (!_typedCommandSettingsEditors.TryGetValue(root, out var editor) || editor.Updating)
            return;

        settings.Text = TypedCommandSettingsKind(pluginClass) switch
        {
            CommandSettingsKind.RunCommand => RunCommandSettingsJson(editor.RunCommandText.Text, editor.RunCommandShell.SelectedIndex == 1 ? "PowerShell" : "CMD", editor.RunCommandAdministrator.IsChecked == true, editor.RunCommandShowWindow.IsChecked == true),
            CommandSettingsKind.Volume => VolumeSettingsJson(editor.VolumeMethod.SelectedIndex, ParsePercent(editor.VolumePercent.Text)),
            CommandSettingsKind.Brightness => BrightnessSettingsJson(editor.BrightnessMethod.SelectedIndex, ParsePercent(editor.BrightnessPercent.Text)),
            CommandSettingsKind.OpenFile => OpenFileSettingsJson(editor.OpenFilePath.Text, editor.OpenFileVariables.Text),
            _ => settings.Text
        };
    }

    private static void UpdateVolumePercentVisibility(TypedCommandSettingsEditor editor)
        => editor.VolumePercent.Visibility = editor.VolumeMethod.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;

    private enum CommandSettingsKind
    {
        None,
        RunCommand,
        Volume,
        Brightness,
        OpenFile
    }

    private static CommandSettingsKind TypedCommandSettingsKind(string pluginClass)
    {
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return CommandSettingsKind.RunCommand;
        if (pluginClass.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            return CommandSettingsKind.Volume;
        if (pluginClass.Contains("ScreenBrightness", StringComparison.OrdinalIgnoreCase))
            return CommandSettingsKind.Brightness;
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return CommandSettingsKind.OpenFile;
        return CommandSettingsKind.None;
    }

    private static bool IsTypedSettingsPlugin(string pluginClass)
        => TypedCommandSettingsKind(pluginClass) != CommandSettingsKind.None;

    private static string RunCommandSettingsJson(string command, string shell, bool runAsAdministrator, bool showWindow)
        => new JsonObject
        {
            ["Command"] = command ?? "",
            ["ShowCmd"] = showWindow,
            ["Shell"] = string.Equals(shell, "PowerShell", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : "CMD",
            ["RunAsAdministrator"] = runAsAdministrator
        }.ToJsonString();

    private static string VolumeSettingsJson(int method, int percent)
        => new JsonObject
        {
            ["Method"] = Math.Clamp(method, 0, 2),
            ["Percent"] = Math.Clamp(percent, 1, 100)
        }.ToJsonString();

    private static string BrightnessSettingsJson(int method, int percent)
        => new JsonObject
        {
            ["Method"] = Math.Clamp(method, 0, 1),
            ["Percent"] = Math.Clamp(percent, 1, 100)
        }.ToJsonString();

    private static string OpenFileSettingsJson(string path, string variables)
        => new JsonObject
        {
            ["Path"] = path ?? "",
            ["Variables"] = variables ?? ""
        }.ToJsonString();

    private static int ParsePercent(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent, 1, 100)
            : 10;

    private static int JsonIntValue(string settings, string key, int fallback)
    {
        try
        {
            if (JsonNode.Parse(settings) is JsonObject root && root[key] is JsonValue value && value.TryGetValue<int>(out var result))
                return result;
        }
        catch
        {
        }

        return fallback;
    }

    private static string JsonStringValue(string settings, string key, string fallback)
    {
        try
        {
            if (JsonNode.Parse(settings) is JsonObject root)
                return root.StringValue(key, fallback);
        }
        catch
        {
        }

        return fallback;
    }

    private static bool JsonBoolValue(string settings, string key, bool fallback)
    {
        try
        {
            if (JsonNode.Parse(settings) is JsonObject root && root[key] is JsonValue value && value.TryGetValue<bool>(out var result))
                return result;
        }
        catch
        {
        }

        return fallback;
    }

    private static IReadOnlyList<AppCommandChoice> GetInstalledApplicationChoices()
    {
        var choices = new Dictionary<string, AppCommandChoice>(StringComparer.OrdinalIgnoreCase);
        AddDesktopChoice(choices, "记事本", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"));
        AddDesktopChoice(choices, "资源管理器", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            AddInstalledApplicationsFromRegistry(choices, RegistryHive.LocalMachine, view);
            AddInstalledApplicationsFromRegistry(choices, RegistryHive.CurrentUser, view);
        }

        return choices.Values
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(260)
            .ToList();
    }

    private static void AddInstalledApplicationsFromRegistry(Dictionary<string, AppCommandChoice> choices, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null)
                return;

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subKeyName);
                if (key is null)
                    continue;

                var displayNameValue = key.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayNameValue))
                    continue;
                var displayName = displayNameValue.Trim();

                var systemComponent = key.GetValue("SystemComponent")?.ToString();
                if (systemComponent == "1")
                    continue;

                var path = ExtractExecutablePath(key.GetValue("DisplayIcon") as string)
                    ?? ExtractExecutablePath(key.GetValue("InstallLocation") as string)
                    ?? FindApplicationExe(key.GetValue("InstallLocation") as string, displayName);
                AddDesktopChoice(choices, displayName, path);
            }
        }
        catch
        {
        }
    }

    private static void AddDesktopChoice(Dictionary<string, AppCommandChoice> choices, string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("update", StringComparison.OrdinalIgnoreCase))
            return;

        choices[path] = AppCommandChoice.Desktop(name.Trim(), path);
    }

    private static string? FindApplicationExe(string? installLocation, string displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return null;

            var directory = Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"'));
            if (File.Exists(directory) && directory.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return directory;
            if (!Directory.Exists(directory))
                return null;

            var normalizedName = NormalizeAppName(displayName);
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileNameWithoutExtension(path).Contains("unins", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => NormalizeAppName(Path.GetFileNameWithoutExtension(path)).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)
                    || normalizedName.Contains(NormalizeAppName(Path.GetFileNameWithoutExtension(path)), StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path.Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAppName(string value)
        => new(value.Where(char.IsLetterOrDigit).ToArray());

    private static string? ExtractExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        string candidate;
        if (expanded.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = expanded.IndexOf('"', 1);
            candidate = end > 1 ? expanded[1..end] : expanded.Trim('"');
        }
        else
        {
            var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
                return null;
            candidate = expanded[..(exeIndex + 4)];
        }

        candidate = candidate.Trim().Trim('"');
        return File.Exists(candidate) ? candidate : null;
    }

    private static string RunCommandSettingsJson(string path)
        => new JsonObject
        {
            ["Command"] = QuoteCommandPath(path),
            ["ShowCmd"] = false
        }.ToJsonString();

    private static string LaunchAppSettingsJson(string key, string value)
        => new JsonObject
        {
            ["Key"] = key,
            ["Value"] = value
        }.ToJsonString();

    private static string QuoteCommandPath(string path)
        => path.Contains(' ') ? $"\"{path}\"" : path;

    private static void UpdateCommandEditorVisibility(string pluginClass, TextBox pluginClassBox, FrameworkElement hotkeyBox, TextBox settingsBox, FrameworkElement? appPicker = null)
    {
        var isHotKey = pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase);
        var isCustom = string.IsNullOrWhiteSpace(pluginClass);
        var isSettingsFree = IsSettingsFreePlugin(pluginClass);
        var hasTypedSettings = IsTypedSettingsPlugin(pluginClass);
        var isAppLauncher = pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase);
        pluginClassBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        hotkeyBox.Visibility = isHotKey ? Visibility.Visible : Visibility.Collapsed;
        settingsBox.Visibility = isHotKey || isSettingsFree || isAppLauncher || hasTypedSettings ? Visibility.Collapsed : Visibility.Visible;
        if (appPicker is not null)
            appPicker.Visibility = isAppLauncher ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsSettingsFreePlugin(string pluginClass)
    {
        if (string.IsNullOrWhiteSpace(pluginClass))
            return false;

        return pluginClass.Contains("DefaultBrowser", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("PreviousApplication", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("NextApplication", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("MaximizeRestore", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("Minimize", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("ToggleWindowTopmost", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("ToggleDisableGestures", StringComparison.OrdinalIgnoreCase);
    }

    private static string HotKeySettingsJson(bool windows, bool control, bool shift, bool alt, int keyCode)
        => $"{{\"Windows\":{JsonBool(windows)},\"Control\":{JsonBool(control)},\"Shift\":{JsonBool(shift)},\"Alt\":{JsonBool(alt)},\"KeyCode\":[{keyCode}],\"SendByKeybdEvent\":false}}";

    private static string ActionHotKeySettingsJson(bool windows, bool control, bool shift, bool alt, int keyCode)
        => $"{{\"KeyCode\":{keyCode},\"ModifierKeys\":{HotKeyModifierFlags(windows, control, shift, alt)}}}";

    private static string JsonBool(bool value) => value ? "true" : "false";

    private static string HotKeyDisplayText(string settings)
    {
        try
        {
            if (JsonNode.Parse(settings) is not JsonObject root)
                return "";

            var parts = new List<string>();
            if (root["Windows"]?.GetValue<bool>() == true)
                parts.Add("Win");
            if (root["Control"]?.GetValue<bool>() == true)
                parts.Add("Ctrl");
            if (root["Shift"]?.GetValue<bool>() == true)
                parts.Add("Shift");
            if (root["Alt"]?.GetValue<bool>() == true)
                parts.Add("Alt");
            if (root["ModifierKeys"] is JsonValue modifierValue && modifierValue.TryGetValue<int>(out var modifiers))
            {
                if ((modifiers & 8) != 0 && !parts.Contains("Win"))
                    parts.Add("Win");
                if ((modifiers & 2) != 0 && !parts.Contains("Ctrl"))
                    parts.Add("Ctrl");
                if ((modifiers & 4) != 0 && !parts.Contains("Shift"))
                    parts.Add("Shift");
                if ((modifiers & 1) != 0 && !parts.Contains("Alt"))
                    parts.Add("Alt");
            }
            if (root["KeyCode"] is JsonArray keys)
            {
                foreach (var key in keys)
                {
                    if (key is null)
                        continue;
                    parts.Add(KeyDisplayName(key.GetValue<int>()));
                }
            }
            else if (root["KeyCode"] is JsonValue keyValue && keyValue.TryGetValue<int>(out var keyCode) && keyCode != 0)
                parts.Add(KeyDisplayName(keyCode));
            return parts.Count == 0 ? "" : string.Join(" + ", parts);
        }
        catch
        {
            return "";
        }
    }

    private static string KeyDisplayName(int keyCode)
    {
        if (keyCode >= 0x30 && keyCode <= 0x39)
            return ((char)keyCode).ToString();
        if (keyCode >= 0x41 && keyCode <= 0x5A)
            return ((char)keyCode).ToString();

        var key = (VirtualKey)keyCode;
        return key switch
        {
            VirtualKey.Escape => "Esc",
            VirtualKey.Control => "Ctrl",
            VirtualKey.Menu => "Alt",
            VirtualKey.Shift => "Shift",
            VirtualKey.CapitalLock => "Caps Lock",
            VirtualKey.Space => "Space",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Delete => "Del",
            VirtualKey.Insert => "Ins",
            _ => key.ToString()
        };
    }

    private static bool IsModifierKey(VirtualKey key)
        => key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static bool IsVirtualKeyDown(int virtualKey)
        => (GetKeyState(virtualKey) & 0x8000) != 0;

    private void StartHotKeyRecording(TextBox recorder, TextBox settings, bool usesArrayKeyCode, Action<string>? onRecorded = null)
    {
        _activeHotKeyRecorder = recorder;
        _activeHotKeySettings = settings;
        _activeHotKeyRecorded = onRecorded;
        _activeHotKeyUsesArrayKeyCode = usesArrayKeyCode;
        _hotKeyRecordingPressedKeys.Clear();
        _stopHotKeyRecordingWhenReleased = false;
        recorder.Text = "请按下快捷键...";
        if (_keyboardHook != IntPtr.Zero)
            return;

        _keyboardHookProc = LowLevelKeyboardCallback;
        _keyboardHook = SetWindowsHookEx(13, _keyboardHookProc, GetModuleHandle(null), 0);
    }

    private void StopHotKeyRecording()
    {
        var recorder = _activeHotKeyRecorder;
        var settings = _activeHotKeySettings;
        _activeHotKeyRecorder = null;
        _activeHotKeySettings = null;
        _activeHotKeyRecorded = null;
        _activeHotKeyUsesArrayKeyCode = true;
        _hotKeyRecordingPressedKeys.Clear();
        _stopHotKeyRecordingWhenReleased = false;
        if (_keyboardHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _keyboardHookProc = null;

        if (recorder is not null && settings is not null && string.Equals(recorder.Text, "请按下快捷键...", StringComparison.Ordinal))
            recorder.Text = HotKeyDisplayText(settings.Text);
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        const int wmKeyDown = 0x0100;
        const int wmSysKeyDown = 0x0104;
        const int wmKeyUp = 0x0101;
        const int wmSysKeyUp = 0x0105;
        if (nCode >= 0 && _activeHotKeyRecorder is not null && _activeHotKeySettings is not null)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var virtualKey = info.VkCode;
            var isDown = wParam == wmKeyDown || wParam == wmSysKeyDown;
            var isUp = wParam == wmKeyUp || wParam == wmSysKeyUp;

            if (isDown)
            {
                _hotKeyRecordingPressedKeys.Add(virtualKey);

                if (IsModifierVirtualKey(virtualKey))
                {
                    var preview = HotKeyModifierDisplayText(
                        HasPressedModifier(0x5B, 0x5C),
                        HasPressedModifier(0x11, 0xA2, 0xA3),
                        HasPressedModifier(0x10, 0xA0, 0xA1),
                        HasPressedModifier(0x12, 0xA4, 0xA5));
                    _ = DispatcherQueue.TryEnqueue(() => _activeHotKeyRecorder.Text = string.IsNullOrWhiteSpace(preview) ? "请按下快捷键..." : $"{preview} + ...");
                    return new IntPtr(1);
                }

                var windows = HasPressedModifier(0x5B, 0x5C);
                var control = HasPressedModifier(0x11, 0xA2, 0xA3);
                var shift = HasPressedModifier(0x10, 0xA0, 0xA1);
                var alt = HasPressedModifier(0x12, 0xA4, 0xA5);
                var settings = _activeHotKeySettings;
                var recorder = _activeHotKeyRecorder;
                var onRecorded = _activeHotKeyRecorded;
                var usesArrayKeyCode = _activeHotKeyUsesArrayKeyCode;
                _stopHotKeyRecordingWhenReleased = true;
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    var nextSettings = usesArrayKeyCode
                        ? HotKeySettingsJson(windows, control, shift, alt, virtualKey)
                        : ActionHotKeySettingsJson(windows, control, shift, alt, virtualKey);
                    settings.Text = nextSettings;
                    recorder.Text = HotKeyDisplayText(nextSettings);
                    onRecorded?.Invoke(nextSettings);
                });
                return new IntPtr(1);
            }

            if (isUp)
            {
                _hotKeyRecordingPressedKeys.Remove(virtualKey);
                if (_stopHotKeyRecordingWhenReleased && _hotKeyRecordingPressedKeys.Count == 0)
                    _ = DispatcherQueue.TryEnqueue(StopHotKeyRecording);
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsModifierVirtualKey(int virtualKey)
        => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private bool HasPressedModifier(params int[] virtualKeys)
        => virtualKeys.Any(key => _hotKeyRecordingPressedKeys.Contains(key) || IsVirtualKeyDown(key));

    private static string HotKeyModifierDisplayText(bool windows, bool control, bool shift, bool alt)
    {
        var parts = new List<string>();
        if (windows)
            parts.Add("Win");
        if (control)
            parts.Add("Ctrl");
        if (shift)
            parts.Add("Shift");
        if (alt)
            parts.Add("Alt");
        return string.Join(" + ", parts);
    }

    private static int HotKeyModifierFlags(bool windows, bool control, bool shift, bool alt)
    {
        var flags = 0;
        if (alt)
            flags |= 1;
        if (control)
            flags |= 2;
        if (shift)
            flags |= 4;
        if (windows)
            flags |= 8;
        return flags;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLlHookStruct
    {
        public NativePoint Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    private async Task ImportActionsAsync()
    {
        var file = await PickOpenFileAsync([".gsa", ".ges"]);
        if (file is null)
            return;
        if (Path.GetExtension(file).Equals(".ges", StringComparison.OrdinalIgnoreCase))
            _legacyData.RestoreArchive(file);
        else
            _legacyData.ImportActions(file);
        ReloadData();
        await ShowInfoDialog("导入完成", file);
    }

    private async Task ExportActionsAsync()
    {
        var file = await PickSaveFileAsync("Actions.gsa", ".gsa");
        if (file is null)
            return;
        await ShowInfoDialog("导出完成", _legacyData.ExportActions(file));
    }

    private async Task ImportGesturesAsync()
    {
        var file = await PickOpenFileAsync([".gest"]);
        if (file is null)
            return;
        _legacyData.ImportGestures(file);
        ReloadData();
        await ShowInfoDialog("导入完成", file);
    }

    private async Task ExportGesturesAsync()
    {
        var file = await PickSaveFileAsync("Gestures.gest", ".gest");
        if (file is null)
            return;
        await ShowInfoDialog("导出完成", _legacyData.ExportGestures(file));
    }

    private async Task AddGestureAsync()
    {
        var name = new TextBox { PlaceholderText = "手势名称", Text = "NewGesture" };
        var fingerCount = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 2 };
        foreach (var item in new[] { "1 指", "2 指", "3 指", "4 指", "5 指" })
            fingerCount.Items.Add(item);
        var direction = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 0 };
        foreach (var item in new[] { "向右", "向左", "向上", "向下", "左上", "右上", "左下", "右下" })
            direction.Items.Add(item);

        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(fingerCount);
        panel.Children.Add(direction);
        panel.Children.Add(new TextBlock { Text = "这里会生成旧版配置可识别的基础轨迹模板，采样训练编辑器会继续迁移。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        if (!await ConfirmDialogAsync("新建手势", panel, "添加"))
            return;

        _legacyData.AddGesture(name.Text, fingerCount.SelectedIndex + 1, direction.SelectedItem?.ToString() ?? "向右");
        ReloadData();
    }

    private async Task RenameGestureAsync(LegacyGesture gesture)
    {
        var name = new TextBox { PlaceholderText = "手势名称", Text = gesture.Name };
        if (!await ConfirmDialogAsync("重命名手势", name, "保存"))
            return;
        _legacyData.RenameGesture(gesture, name.Text);
        ReloadData();
    }

    private async Task DeleteGestureAsync(LegacyGesture gesture)
    {
        if (!await ConfirmDialogAsync("删除确认", $"确定删除手势 {gesture.Name}？引用它的动作会保留，但后台将无法匹配这个手势。", "删除"))
            return;
        _legacyData.DeleteGesture(gesture);
        ReloadData();
    }

    private async Task StartDaemonGestureTrainingAsync()
    {
        var name = new TextBox { PlaceholderText = "手势名称", Text = "NewGesture" };
        var panel = NewCardPanel(8);
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "点击开始后，请用触控板/触摸屏绘制真实多指手势。后台捕获完成后会自动保存。", TextWrapping = TextWrapping.Wrap, Opacity = 0.68 });
        if (!await ConfirmDialogAsync("后台训练手势", panel, "开始"))
            return;

        _pendingTrainingGestureName = name.Text;
        _trainingPipeServer.Start();
        if (!await NotifyDaemonAsync(DaemonCommand.StartTeaching))
        {
            _trainingPipeServer.Stop();
            await ShowInfoDialog("后台未运行", "没有连接到后台托盘，已切换为手绘训练。");
            await DrawGestureAsync(null);
        }
    }

    private string? _pendingTrainingGestureName;
    private Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>? _pendingTrainingPreview;

    private async Task SaveTrainedGestureAsync(IReadOnlyList<IReadOnlyList<(double X, double Y)>> pointPatterns)
    {
        await NotifyDaemonAsync(DaemonCommand.StopTraining);
        _trainingPipeServer.Stop();

        var name = string.IsNullOrWhiteSpace(_pendingTrainingGestureName) ? "NewGesture" : _pendingTrainingGestureName;
        var existingGesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingGesture is null)
            _legacyData.AddGestureFromPointPatterns(name, pointPatterns);
        else
            _legacyData.UpdateGesturePointPatterns(existingGesture, pointPatterns);

        _legacyData = LegacyDataStore.Load();
        _ = NotifyDaemonAsync(DaemonCommand.LoadGestures);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);

        if (_pendingTrainingStatus is not null)
        {
            _pendingTrainingStatus.Text = $"已录制并保存手势 {name}。";
            _pendingTrainingPreview?.Invoke(pointPatterns);
            _pendingTrainingGestureName = null;
            _pendingTrainingStatus = null;
            _pendingTrainingPreview = null;
            ReloadData();
            return;
        }

        _pendingTrainingGestureName = null;
        _pendingTrainingPreview = null;
        ReloadData();
        await ShowInfoDialog("训练完成", $"已保存手势 {name}。");
    }

    private async Task StartGestureTrainingForNameAsync(string gestureName, TextBlock status, Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>? preview = null)
    {
        _pendingTrainingGestureName = string.IsNullOrWhiteSpace(gestureName) ? "NewGesture" : gestureName;
        _pendingTrainingStatus = status;
        _pendingTrainingPreview = preview;
        _trainingPipeServer.Start();
        status.Text = "请现在用触控板绘制手势，完成后会自动保存到当前手势图案。";
        if (await NotifyDaemonAsync(DaemonCommand.StartTeaching))
            return;

        _trainingPipeServer.Stop();
        _pendingTrainingGestureName = null;
        _pendingTrainingStatus = null;
        _pendingTrainingPreview = null;
        status.Text = "后台识别服务未连接，无法捕捉触控板原始轨迹。可以先确认托盘服务已启动。";
    }

    private async Task DrawGestureAsync(LegacyGesture? gesture)
    {
        var name = new TextBox { PlaceholderText = "手势名称", Text = gesture?.Name ?? "NewGesture" };
        var fingerCount = new ComboBox { Margin = new Thickness(0, 8, 0, 8), SelectedIndex = Math.Clamp((gesture?.FingerCount ?? 3) - 1, 0, 4) };
        foreach (var item in new[] { "1 指", "2 指", "3 指", "4 指", "5 指" })
            fingerCount.Items.Add(item);

        var sample = new System.Collections.Generic.List<(double X, double Y)>();
        var canvas = new Canvas
        {
            Width = 620,
            Height = 300,
            Background = SubtleBrush()
        };
        var hint = new TextBlock
        {
            Text = "在这里按住并绘制手势轨迹",
            Opacity = 0.62,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        canvas.Children.Add(hint);

        Polyline? line = null;
        uint? activePointerId = null;
        canvas.RightTapped += (_, args) => args.Handled = true;
        canvas.PointerCanceled += (_, _) =>
        {
            line = null;
            activePointerId = null;
        };
        canvas.PointerCaptureLost += (_, _) =>
        {
            line = null;
            activePointerId = null;
        };
        canvas.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas);

            sample.Clear();
            canvas.Children.Clear();
            line = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)), StrokeThickness = 4 };
            canvas.Children.Add(line);
            canvas.CapturePointer(args.Pointer);
            activePointerId = point.PointerId;
            var position = point.Position;
            sample.Add((position.X, position.Y));
            line.Points.Add(position);
            args.Handled = true;
        };
        canvas.PointerMoved += (_, args) =>
        {
            if (line is null)
                return;
            var point = args.GetCurrentPoint(canvas);
            if (activePointerId is not null && point.PointerId != activePointerId.Value)
                return;
            var position = point.Position;
            var last = sample.LastOrDefault();
            if (sample.Count > 0 && Math.Abs(last.X - position.X) + Math.Abs(last.Y - position.Y) < 4)
                return;
            sample.Add((position.X, position.Y));
            line.Points.Add(position);
            args.Handled = true;
        };
        canvas.PointerReleased += (_, args) =>
        {
            canvas.ReleasePointerCapture(args.Pointer);
            line = null;
            activePointerId = null;
            args.Handled = true;
        };

        var clear = NewPillButton("清除轨迹", false);
        clear.Click += (_, _) =>
        {
            sample.Clear();
            canvas.Children.Clear();
            canvas.Children.Add(hint);
        };

        var panel = NewCardPanel(8);
        panel.Children.Add(name);
        panel.Children.Add(fingerCount);
        panel.Children.Add(new Border
        {
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = canvas
        });
        panel.Children.Add(clear);
        if (!await ConfirmDialogAsync(gesture is null ? "绘制手势" : $"重训 {gesture.Name}", panel, "保存"))
            return;

        if (sample.Count < 2)
        {
            await ShowInfoDialog("轨迹太短", "请至少绘制一段明显轨迹。");
            return;
        }

        if (gesture is null)
            _legacyData.AddGestureFromPoints(name.Text, fingerCount.SelectedIndex + 1, sample);
        else
        {
            if (!string.Equals(name.Text, gesture.Name, StringComparison.OrdinalIgnoreCase))
                _legacyData.RenameGesture(gesture, name.Text);
            _legacyData.UpdateGesturePoints(gesture, fingerCount.SelectedIndex + 1, sample);
        }
        ReloadData();
    }

    private FrameworkElement NewInlineGestureDrawingPanel(List<List<(double X, double Y)>> sample, out Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> showRecordedGesture, out Button clearButton)
    {
        var canvas = new Canvas
        {
            Width = 460,
            Height = 180,
            Background = IsDark
                ? new SolidColorBrush(Color.FromArgb(42, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(255, 248, 250, 252))
        };
        var hint = new TextBlock
        {
            Text = "在这里用触控板、鼠标或触摸屏按住并绘制图案",
            Opacity = 0.62,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Width = 360
        };
        canvas.Children.Add(hint);

        void ShowHint()
        {
            canvas.Children.Clear();
            canvas.Children.Add(hint);
        }

        showRecordedGesture = strokes =>
        {
            sample.Clear();
            foreach (var stroke in strokes)
                sample.Add(stroke.ToList());
            canvas.Children.Clear();
            DrawGestureLines(canvas, strokes, canvas.Width, canvas.Height);
        };

        var activeLines = new Dictionary<uint, Polyline>();
        var activeStrokes = new Dictionary<uint, List<(double X, double Y)>>();
        canvas.RightTapped += (_, args) => args.Handled = true;
        canvas.PointerCanceled += (_, _) =>
        {
            activeLines.Clear();
            activeStrokes.Clear();
        };
        canvas.PointerCaptureLost += (_, _) =>
        {
            // WinUI may move pointer capture while a second touch contact is added.
            // Keep the in-progress strokes so simultaneous touch drawing is not saved as one stroke.
        };
        canvas.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas);

            if (activeLines.Count == 0)
            {
                sample.Clear();
                canvas.Children.Clear();
            }

            var line = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(line);
            var position = point.Position;
            var stroke = new List<(double X, double Y)> { (position.X, position.Y) };
            sample.Add(stroke);
            activeLines[point.PointerId] = line;
            activeStrokes[point.PointerId] = stroke;
            line.Points.Add(position);
            args.Handled = true;
        };
        canvas.PointerMoved += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas);
            if (!activeLines.TryGetValue(point.PointerId, out var line) ||
                !activeStrokes.TryGetValue(point.PointerId, out var stroke))
                return;
            var position = point.Position;
            var last = stroke.LastOrDefault();
            if (stroke.Count > 0 && Math.Abs(last.X - position.X) + Math.Abs(last.Y - position.Y) < 4)
                return;
            stroke.Add((position.X, position.Y));
            line.Points.Add(position);
            args.Handled = true;
        };
        canvas.PointerReleased += (_, args) =>
        {
            var point = args.GetCurrentPoint(canvas);
            activeLines.Remove(point.PointerId);
            activeStrokes.Remove(point.PointerId);
            args.Handled = true;
        };

        clearButton = NewPillButton("清除图案", false);
        clearButton.Click += (_, _) =>
        {
            sample.Clear();
            activeLines.Clear();
            activeStrokes.Clear();
            ShowHint();
        };

        var panel = NewCardPanel(8);
        panel.Children.Add(new Border
        {
            BorderBrush = IsDark
                ? new SolidColorBrush(Color.FromArgb(96, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(255, 148, 158, 170)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Child = canvas
        });
        return panel;
    }

    private async Task RestoreArchiveAsync()
    {
        var file = await PickOpenFileAsync([".ges"]);
        if (file is null)
            return;
        _legacyData.RestoreArchive(file);
        ReloadData();
        await ShowInfoDialog("恢复完成", "配置已恢复，后台服务会在重载后使用新配置。");
    }

    private async Task TestKandoMenuAsync()
    {
        await FlushPendingOptionUpdatesAsync();
        _legacyData = LegacyDataStore.Load();

        if (!StartKando(_legacyData.Options, BuildKandoShowMenuArguments(_legacyData.Options)))
        {
            await ShowInfoDialog("Kando 未启动", "没有找到 Kando 可执行文件，或启动失败。请检查 Kando 程序路径。");
            return;
        }

        await ShowInfoDialog("已发送唤起命令", "如果 Kando 已正确配置，圆环菜单会出现在当前鼠标位置。");
    }

    private async Task EnableKandoQuickActionsAsync()
    {
        await UpdateOptionAndWaitAsync("KandoEnabled", "True");
        _legacyData = LegacyDataStore.Load();

        if (StartKando(_legacyData.Options, string.Empty))
            return;

        await UpdateOptionAndWaitAsync("KandoEnabled", "False");
        _legacyData = LegacyDataStore.Load();
        ShowPage("quickActions");
        await ShowInfoDialog("Kando 未启动", "没有找到 Kando 可执行文件，或启动失败。请检查 Kando 程序路径。");
    }

    private async Task DisableKandoQuickActionsAsync()
    {
        await UpdateOptionAndWaitAsync("KandoEnabled", "False");
        _legacyData = LegacyDataStore.Load();
        StopKandoProcesses(_legacyData.Options);
    }

    private async Task EnsureKandoStartedIfEnabledAsync()
    {
        try
        {
            await Task.Delay(600);
            _legacyData = LegacyDataStore.Load();
            if (!_legacyData.Options.KandoEnabled || IsKandoRunning(_legacyData.Options))
                return;

            StartKando(_legacyData.Options, string.Empty);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private async Task OpenKandoSettingsAsync()
    {
        await FlushPendingOptionUpdatesAsync();
        _legacyData = LegacyDataStore.Load();

        if (!StartKando(_legacyData.Options, "--settings"))
            await ShowInfoDialog("Kando 未启动", "没有找到 Kando 可执行文件，或启动失败。请检查 Kando 程序路径。");
    }

    private static string BuildKandoShowMenuArguments(LegacyOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.KandoMenuName))
            return "--menu " + QuoteArgument(options.KandoMenuName);
        return string.Empty;
    }

    private static bool StartKando(LegacyOptions options, string arguments)
        => StartKandoProcess(options, arguments) is not null;

    private static Process? StartKandoProcess(LegacyOptions options, string arguments)
    {
        try
        {
            var executablePath = FindKandoExecutablePath(options.KandoExecutablePath);
            if (executablePath is null)
                return null;

            return Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            });
        }
        catch
        {
            return null;
        }
    }

    private static void StopKandoProcesses(LegacyOptions options)
    {
        var executablePath = FindKandoExecutablePath(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);

        foreach (var process in Process.GetProcessesByName("kando").Concat(Process.GetProcessesByName("Kando")).GroupBy(process => process.Id).Select(group => group.First()))
        {
            try
            {
                if (expectedPath is not null)
                {
                    var processPath = process.MainModule?.FileName;
                    if (!string.Equals(Path.GetFullPath(processPath ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task ExitAllGestureSignProcessesAsync()
    {
        if (_isExitingApplication)
            return;

        _isExitingApplication = true;
        _daemonWatchdogTimer.Stop();
        _kandoMenuRefreshTimer.Stop();
        _windowModeRefreshTimer.Stop();
        StopKandoProcesses(_legacyData.Options);

        try
        {
            await SendDaemonCommandAsync(DaemonCommand.Exit);
        }
        catch
        {
        }

        await Task.Delay(700);
        StopInstalledGestureSignProcesses();
        Close();
    }

    private static void StopInstalledGestureSignProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcessId)
                    continue;

                var processPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath))
                    continue;

                var fullPath = Path.GetFullPath(processPath);
                var isInstalledGestureSignProcess =
                    fullPath.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    (Path.GetFileNameWithoutExtension(fullPath).StartsWith("GestureSign", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileNameWithoutExtension(fullPath).Equals("RestartAgent", StringComparison.OrdinalIgnoreCase));

                if (!isInstalledGestureSignProcess)
                    continue;

                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsKandoRunning(LegacyOptions options)
    {
        var executablePath = FindKandoExecutablePath(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);

        foreach (var process in Process.GetProcessesByName("kando").Concat(Process.GetProcessesByName("Kando")).GroupBy(process => process.Id).Select(group => group.First()))
        {
            try
            {
                if (expectedPath is null)
                    return true;

                var processPath = process.MainModule?.FileName;
                if (string.Equals(Path.GetFullPath(processPath ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static string? FindKandoExecutablePath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Kando", "kando.exe"),
            Path.Combine(baseDirectory, "Kando", "Kando.exe"),
            Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "kando.exe"),
            Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "Kando.exe"),
            Path.Combine(baseDirectory, "kando.exe"),
            Path.Combine(baseDirectory, "Kando.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "kando.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "Kando.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static List<KandoMenuInfo> ReadKandoMenus()
    {
        var path = GetKandoMenusPath();
        if (!File.Exists(path))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("menus", out var menusElement) || menusElement.ValueKind != JsonValueKind.Array)
                return [];

            var menus = new List<KandoMenuInfo>();
            foreach (var menuElement in menusElement.EnumerateArray())
            {
                var shortcut = JsonString(menuElement, "shortcut");
                var name = "";
                if (menuElement.TryGetProperty("root", out var rootElement))
                    name = JsonString(rootElement, "name");

                if (string.IsNullOrWhiteSpace(name))
                    name = "未命名菜单";

                menus.Add(new KandoMenuInfo(name, shortcut));
            }

            return menus;
        }
        catch
        {
            return [];
        }
    }

    private void KandoMenuRefreshTimer_Tick(object? sender, object e)
    {
        if (Navigation.SelectedItem is not NavigationViewItem { Tag: "quickActions" })
        {
            _kandoMenuRefreshTimer.Stop();
            _refreshKandoMenuList = null;
            return;
        }

        var writeTime = GetKandoMenusWriteTimeUtc();
        if (writeTime == _lastKandoMenusWriteTimeUtc)
            return;

        _lastKandoMenusWriteTimeUtc = writeTime;
        _refreshKandoMenuList?.Invoke();
    }

    private static string GetKandoMenusPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kando", "menus.json");

    private static DateTime GetKandoMenusWriteTimeUtc()
    {
        try
        {
            var path = GetKandoMenusPath();
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string JsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int KandoMenuIndex(IReadOnlyList<KandoMenuInfo> menus, LegacyOptions options)
        => KandoMenuIndex(menus, options.KandoMenuName);

    private static int KandoMenuIndex(IReadOnlyList<KandoMenuInfo> menus, string selectedMenuName)
    {
        if (!string.IsNullOrWhiteSpace(selectedMenuName))
        {
            var nameIndex = menus.ToList().FindIndex(menu => string.Equals(menu.Name, selectedMenuName, StringComparison.OrdinalIgnoreCase));
            if (nameIndex >= 0)
                return nameIndex;
        }

        return 0;
    }

    private static string DisplayFallback(string value)
        => string.IsNullOrWhiteSpace(value) ? "未设置" : value;

    private static bool TryCreateHotKeySettingsFromKandoShortcut(string shortcut, out string settings)
    {
        settings = "";
        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        var windows = false;
        var control = false;
        var shift = false;
        var alt = false;
        int? keyCode = null;

        foreach (var rawPart in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
                continue;

            var normalized = part.Replace("Left", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Right", "", StringComparison.OrdinalIgnoreCase);

            if (normalized.Equals("Control", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                control = true;
                continue;
            }

            if (normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                continue;
            }

            if (normalized.Equals("Alt", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Menu", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                continue;
            }

            if (normalized.Equals("Meta", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Super", StringComparison.OrdinalIgnoreCase))
            {
                windows = true;
                continue;
            }

            if (!TryKandoShortcutKeyCode(normalized, out var parsedKeyCode))
                return false;

            keyCode = parsedKeyCode;
        }

        if (keyCode is null)
            return false;

        settings = HotKeySettingsJson(windows, control, shift, alt, keyCode.Value);
        return true;
    }

    private static bool TryKandoShortcutKeyCode(string key, out int keyCode)
    {
        keyCode = 0;
        if (key.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsLetter(key[3]))
        {
            keyCode = char.ToUpperInvariant(key[3]);
            return true;
        }

        if (key.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && key.Length == 6 && char.IsDigit(key[5]))
        {
            keyCode = key[5];
            return true;
        }

        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            keyCode = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            keyCode = 111 + functionKey;
            return true;
        }

        keyCode = key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "escape" or "esc" => 0x1B,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "arrowleft" or "left" => 0x25,
            "arrowup" or "up" => 0x26,
            "arrowright" or "right" => 0x27,
            "arrowdown" or "down" => 0x28,
            _ => 0
        };

        return keyCode != 0;
    }

    private readonly record struct KandoMenuInfo(string Name, string Shortcut)
    {
        public string DisplayText
        {
            get
            {
                var shortcut = string.IsNullOrWhiteSpace(Shortcut) ? "未绑定快捷键" : Shortcut;
                return $"{Name}    {shortcut}";
            }
        }
    }

    private async Task DownloadSharedSettingsAsync()
    {
        var urls = new[]
        {
            "https://github.com/TransposonY/GestureSignSettings/archive/master.zip",
            "https://transposony.coding.net/p/GestureSignSettings/d/GestureSignSettings/git/archive/master"
        };
        var tempRoot = Path.Combine(_legacyData.LocalPath, "TempSharedSettings");
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
        Directory.CreateDirectory(tempRoot);

        using var client = new System.Net.Http.HttpClient();
        byte[]? data = null;
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                data = await client.GetByteArrayAsync(url);
                if (data.Length > 0)
                    break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (data is null)
        {
            await ShowInfoDialog("下载失败", lastError?.Message ?? "没有可用的共享列表源。");
            return;
        }

        var zipPath = Path.Combine(tempRoot, "settings.zip");
        File.WriteAllBytes(zipPath, data);
        ZipFile.ExtractToDirectory(zipPath, tempRoot);
        var actionFiles = Directory.GetFiles(tempRoot, "*.gsa", SearchOption.AllDirectories);
        var gestureFiles = Directory.GetFiles(tempRoot, "*.gest", SearchOption.AllDirectories);
        foreach (var file in actionFiles)
            _legacyData.ImportActions(file);
        foreach (var file in gestureFiles)
            _legacyData.ImportGestures(file);
        Directory.Delete(tempRoot, true);
        ReloadData();
        await ShowInfoDialog("导入完成", $"已导入 {actionFiles.Length} 个动作文件、{gestureFiles.Length} 个手势文件。");
    }

    private async Task ShowLogAsync()
    {
        var logPath = Path.Combine(_legacyData.LocalPath, "GestureSign.log");
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = GetWindowScale();
        var workWidth = Math.Max(900, workArea.Width / scale);
        var workHeight = Math.Max(720, workArea.Height / scale);
        var dialogWidth = Math.Clamp(workWidth * 0.68, 900, 1400);
        var dialogHeight = Math.Clamp(workHeight * 0.9, 720, 980);
        var logWidth = Math.Max(780, dialogWidth - 72);
        var logHeight = Math.Max(560, dialogHeight - 190);

        var logTextBlock = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(14, 10, 14, 10),
            TextWrapping = TextWrapping.Wrap
        };

        var scrollViewer = new ScrollViewer
        {
            Content = logTextBlock,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled
        };

        var verticalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 26,
            Minimum = 0,
            SmallChange = 48,
            LargeChange = 300,
            Visibility = Visibility.Visible,
            Margin = new Thickness(0, 4, 6, 4)
        };

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(verticalScrollBar, 1);
        contentGrid.Children.Add(scrollViewer);
        contentGrid.Children.Add(verticalScrollBar);

        var logHost = new Border
        {
            Width = logWidth,
            Height = logHeight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = contentGrid
        };

        void ApplyLogTheme()
        {
            var dark = IsDark;
            var panelBackground = dark
                ? new SolidColorBrush(Color.FromArgb(255, 32, 32, 32))
                : new SolidColorBrush(Color.FromArgb(255, 248, 250, 253));
            var foreground = dark
                ? new SolidColorBrush(Color.FromArgb(255, 248, 248, 248))
                : new SolidColorBrush(Color.FromArgb(255, 24, 24, 24));
            var borderBrush = dark
                ? new SolidColorBrush(Color.FromArgb(255, 78, 78, 78))
                : new SolidColorBrush(Color.FromArgb(255, 218, 224, 232));
            var scrollBarBackground = dark
                ? new SolidColorBrush(Color.FromArgb(255, 64, 64, 64))
                : new SolidColorBrush(Color.FromArgb(255, 220, 226, 234));
            var scrollBarForeground = dark
                ? new SolidColorBrush(Color.FromArgb(255, 222, 222, 222))
                : new SolidColorBrush(Color.FromArgb(255, 75, 85, 99));

            logHost.Background = panelBackground;
            logHost.BorderBrush = borderBrush;
            scrollViewer.Background = panelBackground;
            logTextBlock.Foreground = foreground;
            verticalScrollBar.Background = scrollBarBackground;
            verticalScrollBar.Foreground = scrollBarForeground;
        }

        void SetLogText(string text, bool keepScrollPosition = false)
        {
            var oldOffset = scrollViewer.VerticalOffset;
            var wasNearBottom = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 24;
            logTextBlock.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            ApplyLogTheme();
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLogScrollBar();
                if (keepScrollPosition && !wasNearBottom)
                    scrollViewer.ChangeView(null, Math.Min(oldOffset, scrollViewer.ScrollableHeight), null, true);
                else
                    scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, true);
                UpdateLogScrollBar();
            });
        }

        void UpdateLogScrollBar()
        {
            verticalScrollBar.Maximum = Math.Max(0, scrollViewer.ScrollableHeight);
            verticalScrollBar.ViewportSize = Math.Max(1, scrollViewer.ViewportHeight);
            if (Math.Abs(verticalScrollBar.Value - scrollViewer.VerticalOffset) > 0.5)
                verticalScrollBar.Value = Math.Min(verticalScrollBar.Maximum, scrollViewer.VerticalOffset);
        }

        scrollViewer.ViewChanged += (_, _) => UpdateLogScrollBar();
        scrollViewer.SizeChanged += (_, _) => UpdateLogScrollBar();
        verticalScrollBar.ValueChanged += (_, args) =>
        {
            if (Math.Abs(scrollViewer.VerticalOffset - args.NewValue) > 0.5)
                scrollViewer.ChangeView(null, args.NewValue, null, true);
        };

        var currentText = await BuildLogDisplayTextAsync(logPath);
        SetLogText(currentText);

        TypedEventHandler<FrameworkElement, object>? themeChanged = null;
        themeChanged = (_, _) => ApplyLogTheme();
        Root.ActualThemeChanged += themeChanged;
        var isRefreshing = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (isRefreshing)
                return;
            isRefreshing = true;
            try
            {
                var refreshed = await BuildLogDisplayTextAsync(logPath);
                if (!string.Equals(currentText, refreshed, StringComparison.Ordinal))
                {
                    currentText = refreshed;
                    SetLogText(refreshed, true);
                }
            }
            finally
            {
                isRefreshing = false;
            }
        };

        var titleBlock = new TextBlock
        {
            Text = "GestureSign 日志",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(36, 28, 36, 18)
        };

        var closeButton = new Button
        {
            Content = "关闭",
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var footer = new Border
        {
            Padding = new Thickness(64, 18, 64, 24),
            Child = closeButton
        };

        var dialogPanel = new Grid
        {
            Width = dialogWidth,
            MaxHeight = dialogHeight
        };
        dialogPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialogPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dialogPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(titleBlock, 0);
        Grid.SetRow(logHost, 1);
        Grid.SetRow(footer, 2);
        logHost.Margin = new Thickness(36, 0, 36, 0);
        dialogPanel.Children.Add(titleBlock);
        dialogPanel.Children.Add(logHost);
        dialogPanel.Children.Add(footer);

        var dialogSurface = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = dialogPanel,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var overlay = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };
        Grid.SetRowSpan(overlay, 2);
        overlay.Children.Add(dialogSurface);

        void ApplyOverlayTheme()
        {
            var dark = IsDark;
            overlay.Background = dark
                ? new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(120, 29, 42, 53));
            dialogSurface.Background = dark
                ? new SolidColorBrush(Color.FromArgb(255, 39, 39, 39))
                : new SolidColorBrush(Colors.White);
            footer.Background = dark
                ? new SolidColorBrush(Color.FromArgb(255, 34, 34, 34))
                : new SolidColorBrush(Color.FromArgb(255, 247, 247, 247));
            titleBlock.Foreground = dark
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Black);
            ApplyLogTheme();
        }

        var closed = new TaskCompletionSource();
        void CloseLogOverlay()
        {
            timer.Stop();
            if (themeChanged is not null)
                Root.ActualThemeChanged -= themeChanged;
            Root.Children.Remove(overlay);
            closed.TrySetResult();
        }

        closeButton.Click += (_, _) => CloseLogOverlay();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                args.Handled = true;
                CloseLogOverlay();
            }
        };

        ApplyOverlayTheme();
        Root.Children.Add(overlay);
        overlay.Focus(FocusState.Programmatic);
        timer.Start();
        await closed.Task;
    }

    private static async Task<string> BuildLogDisplayTextAsync(string logPath)
    {
        var text = File.Exists(logPath) ? await ReadTextFileSharedWithRetryAsync(logPath) : "暂无日志文件。";
        return TailLogText(text);
    }

    private static string TailLogText(string text)
    {
        const int maxChars = 240 * 1024;
        var fullText = text;
        var result = new StringBuilder();
        var gestureSummary = RecentGestureLogSummary(fullText);
        result.AppendLine(string.IsNullOrWhiteSpace(gestureSummary)
            ? "还没有记录到新的手势捕捉、识别或执行。"
            : gestureSummary);
        text = result.ToString();

        if (text.Length <= maxChars)
            return text;

        var start = text.Length - maxChars;
        var firstLineBreak = text.IndexOf('\n', start);
        if (firstLineBreak >= 0 && firstLineBreak + 1 < text.Length)
            start = firstLineBreak + 1;

        return "仅显示最近日志。完整日志仍保存在本机文件中。\r\n\r\n" + text.Substring(start);
    }

    private static string CompactRawLogTail(string text, int lineCount)
    {
        var lines = text.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(lineCount)
            .Select(FormatRawLogLine)
            .ToArray();

        return lines.Length == 0 ? "暂无原始日志。" : string.Join("\r\n", lines);
    }

    private static string FormatRawLogLine(string line)
    {
        var time = ExtractLogTime(line);
        var message = ExtractLogMessage(line);

        message = message
            .Replace("GestureSign.CorePlugins.", "", StringComparison.Ordinal)
            .Replace("GestureSign.Common.Exceptions.", "", StringComparison.Ordinal)
            .Replace("GestureSign.Common v8.1.0.0", "Common", StringComparison.Ordinal);

        message = ShortenPathField(message, "Executable");
        message = ShortenPathField(message, "Config");
        message = ShortenPathField(message, "Log");

        return $"{time} {message}";
    }

    private static string ShortenPathField(string text, string key)
    {
        var prefix = key + "=";
        var start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return text;

        var valueStart = start + prefix.Length;
        var end = text.IndexOf(", ", valueStart, StringComparison.Ordinal);
        if (end < 0)
            end = text.Length;

        var value = text.Substring(valueStart, end - valueStart);
        var shortValue = value;
        try
        {
            if (Path.IsPathFullyQualified(value))
                shortValue = Path.GetFileName(value);
        }
        catch
        {
            shortValue = value;
        }

        return text.Substring(0, valueStart) + shortValue + text.Substring(end);
    }

    private static string RecentGestureLogSummary(string text)
    {
        var keywords = new[]
        {
            "Mouse gesture",
            "Gesture capture",
            "Gesture recognized",
            "Gesture not recognized",
            "Gesture action",
            "Gesture command"
        };

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var recent = lines
            .Where(line => keywords.Any(keyword => line.Contains(keyword, StringComparison.Ordinal)))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(FormatGestureLogLine)
            .TakeLast(80)
            .ToArray();

        return recent.Length == 0 ? string.Empty : string.Join("\r\n", recent);
    }

    private static string FormatGestureLogLine(string line)
    {
        var time = ExtractLogTime(line);
        var message = ExtractLogMessage(line);

        if (message.StartsWith("Mouse gesture button down.", StringComparison.Ordinal))
            return $"{time} [按键] 鼠标{TranslateMouseButton(ExtractField(message, "Button"))}按下，坐标 {ExtractFieldToEnd(message, "Point")}";

        if (message.StartsWith("Gesture capture started.", StringComparison.Ordinal))
            return $"{time} [开始] {TranslateDevice(ExtractField(message, "Device"))}，{ExtractField(message, "Contacts")} 个触点，模式 {TranslateMode(ExtractField(message, "Mode"))}";

        if (message.StartsWith("Gesture capture ended.", StringComparison.Ordinal))
            return $"{time} [结束] {TranslateDevice(ExtractField(message, "Device"))}，{ExtractField(message, "Strokes")} 条轨迹，{ExtractField(message, "Points")} 个点";

        if (message.StartsWith("Gesture capture canceled", StringComparison.Ordinal))
            return $"{time} [取消] 捕捉被取消";

        if (message.StartsWith("Gesture recognized.", StringComparison.Ordinal))
            return $"{time} [识别] 手势 {ExtractField(message, "Name")}，触点 {ExtractField(message, "Contacts")}";

        if (message.StartsWith("Gesture not recognized.", StringComparison.Ordinal))
            return $"{time} [未识别] 没有匹配到手势";

        if (message.StartsWith("Gesture action lookup completed.", StringComparison.Ordinal))
            return $"{time} [查找] 手势 {ExtractField(message, "Gesture")}，匹配 {ExtractField(message, "Actions")} 个动作，设备 {TranslateDevice(ExtractField(message, "Device"))}";

        if (message.StartsWith("Gesture action completed without executing any command.", StringComparison.Ordinal))
            return $"{time} [未执行] 没有可用命令";

        if (message.StartsWith("Gesture command executing.", StringComparison.Ordinal))
        {
            var plugin = ExtractField(message, "Plugin").Split('.').LastOrDefault() ?? "";
            return $"{time} [执行] 动作 {ExtractField(message, "Action")}，命令 {ExtractField(message, "Command")}，插件 {plugin}";
        }

        return $"{time} {message}";
    }

    private static string ExtractLogTime(string line)
    {
        var end = line.IndexOf(']');
        if (end <= 1)
            return "[--:--:--]";

        var value = line.Substring(1, end - 1);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? $"[{parsed:MM-dd HH:mm:ss}]"
            : $"[{value}]";
    }

    private static string ExtractLogMessage(string line)
    {
        var firstEnd = line.IndexOf("] ", StringComparison.Ordinal);
        if (firstEnd < 0 || firstEnd + 2 >= line.Length)
            return line;

        var rest = line.Substring(firstEnd + 2);
        if (!rest.StartsWith("[", StringComparison.Ordinal))
            return rest;

        var secondEnd = rest.IndexOf("] ", StringComparison.Ordinal);
        return secondEnd >= 0 && secondEnd + 2 < rest.Length ? rest.Substring(secondEnd + 2) : rest;
    }

    private static string ExtractField(string message, string key)
    {
        var marker = key + "=";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return "-";

        start += marker.Length;
        var end = message.IndexOf(',', start);
        if (end < 0)
            end = message.Length;
        return message.Substring(start, end - start).Trim();
    }

    private static string ExtractFieldToEnd(string message, string key)
    {
        var marker = key + "=";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        return start < 0 ? "-" : message.Substring(start + marker.Length).Trim();
    }

    private static string TranslateDevice(string value)
        => value switch
        {
            "Mouse" => "鼠标",
            "TouchPad" => "触控板",
            "TouchScreen" => "触摸屏",
            "Pen" => "手写笔",
            _ => value
        };

    private static string TranslateMouseButton(string value)
        => value switch
        {
            "Right" => "右键",
            "Middle" => "中键",
            "Left" => "左键",
            "XButton1" => "侧键1",
            "XButton2" => "侧键2",
            _ => value
        };

    private static string TranslateMode(string value)
        => value switch
        {
            "Normal" => "正常",
            "Training" => "训练",
            "UserDisabled" => "暂停",
            _ => value
        };

    private static async Task<string> ReadTextFileSharedWithRetryAsync(string path)
    {
        IOException? lastIoException = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await ReadTextFileSharedAsync(path);
            }
            catch (IOException ex)
            {
                lastIoException = ex;
                await Task.Delay(120);
            }
        }

        return $"日志文件暂时被其他进程独占，稍后再试即可。\r\n\r\n路径: {path}\r\n错误: {lastIoException?.Message}";
    }

    private static async Task<string> ReadTextFileSharedAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    private async Task SendFeedbackAsync()
    {
        var logPath = Path.Combine(_legacyData.LocalPath, "GestureSign.log");
        var body = File.Exists(logPath) ? Uri.EscapeDataString($"请描述问题:%0D%0A%0D%0A日志路径: {logPath}") : Uri.EscapeDataString("请描述问题:");
        Process.Start(new ProcessStartInfo($"mailto:553078206@qq.com?subject=GestureSign%20Feedback&body={body}") { UseShellExecute = true });
        await ShowInfoDialog("反馈", "已打开默认邮件客户端；日志路径也已写入邮件正文。");
    }

    private async Task<string?> PickOpenFileAsync(string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var extension in extensions)
            picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickSaveFileAsync(string suggestedName, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("GestureSign 配置", [extension]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<bool> ConfirmDialogAsync(string title, object content, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = NewDialogScrollContent(content),
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private ScrollViewer NewDialogScrollContent(object content)
    {
        var maxHeight = Root.ActualHeight > 0
            ? Math.Max(320, Math.Min(760, Root.ActualHeight - 180))
            : 640;

        return new ScrollViewer
        {
            Content = content,
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled
        };
    }

    private async System.Threading.Tasks.Task ShowInfoDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定"
        };
        await dialog.ShowAsync();
    }

    private FrameworkElement NewListRow(string title, string subtitle, FrameworkElement? trailing, bool isSelected = false, Action? onClick = null)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(2);
        text.Children.Add(new TextBlock { Text = title, Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.62, FontSize = 12 });
        grid.Children.Add(text);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 1);
            grid.Children.Add(trailing);
        }

        var border = new Border
        {
            Background = isSelected ? SelectionBrush() : SubtleBrush(),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
        if (onClick is not null)
        {
            border.Tapped += (_, _) => onClick();
            border.PointerEntered += (_, _) => border.Opacity = 0.9;
            border.PointerExited += (_, _) => border.Opacity = 1;
        }
        return border;
    }

    private FrameworkElement NewKandoMenuRow(string title, string subtitle, FrameworkElement trailing, bool isSelected, Action onClick)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(2);
        text.Children.Add(new TextBlock { Text = title, Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.62, FontSize = 12 });
        grid.Children.Add(text);

        Grid.SetColumn(trailing, 1);
        grid.Children.Add(trailing);

        var border = new Border
        {
            Background = isSelected ? SelectionBrush() : SubtleBrush(),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
        border.PointerReleased += (_, args) =>
        {
            var point = args.GetCurrentPoint(border);
            if (point.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse || point.Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
                return;

            args.Handled = true;
            onClick();
        };
        border.Tapped += (_, args) =>
        {
            if (args.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                return;

            onClick();
        };
        border.PointerEntered += (_, _) => border.Opacity = 0.9;
        border.PointerExited += (_, _) => border.Opacity = 1;
        return border;
    }

    private FrameworkElement NewApplicationRow(string title, string subtitle, FrameworkElement trailing, bool isSelected = false, Action? onClick = null)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = BodyStrongTextBlockStyle,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 2
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.62,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 2
        });
        panel.Children.Add(trailing);

        var border = new Border
        {
            Background = isSelected ? SelectionBrush() : SubtleBrush(),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = panel
        };
        if (onClick is not null)
        {
            border.Tapped += (_, _) => onClick();
            border.PointerEntered += (_, _) => border.Opacity = 0.9;
            border.PointerExited += (_, _) => border.Opacity = 1;
        }
        return border;
    }

    private FrameworkElement NewActionRow(LegacyApplication application, LegacyAction action)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var gestureBox = NewGesturePreview(action.GestureName, 150, 76);
        grid.Children.Add(gestureBox);

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = DisplayName(action.Name), Style = BodyStrongTextBlockStyle });
        text.Children.Add(new TextBlock { Text = ActionSummary(application, action), Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        foreach (var command in action.Commands.Take(1))
        {
            var commandRow = NewListRow(DisplayName(command.Name), $"{PluginName(command.PluginClass)} · {(command.IsEnabled ? L("启用", "Enabled", "啟用", "有効", "사용") : L("停用", "Disabled", "停用", "無効", "사용 안 함"))}", null);
            text.Children.Add(commandRow);
        }
        if (action.Commands.Count == 0)
            text.Children.Add(NewListRow(L("未设置命令", "No Command", "未設定命令", "コマンド未設定", "명령 없음"), L("这个手势暂时不会执行任何操作", "This gesture will not run anything yet.", "這個手勢暫時不會執行任何操作。", "このジェスチャはまだ何も実行しません。", "이 제스처는 아직 아무 작업도 실행하지 않습니다."), null));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = NewInlineButtonsWithContext(
            (L("编辑", "Edit", "編輯", "編集", "편집"), async _ => await EditActionAsync(action)),
            (action.IsEnabled ? L("停用", "Disable", "停用", "無効化", "사용 안 함") : L("启用", "Enable", "啟用", "有効化", "사용"), async button => await ToggleEnabledAsync(action.Source, button)),
            (L("设置命令", "Set Command", "設定命令", "コマンド設定", "명령 설정"), async _ => await SetCommandAsync(action)),
            (L("删除", "Delete", "刪除", "削除", "삭제"), async _ => await DeleteActionAsync(application, action)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return NewCard(grid, new Thickness(12));
    }

    private FrameworkElement NewGesturePreview(string gestureName, double width, double height)
    {
        var gesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, gestureName, StringComparison.OrdinalIgnoreCase));
        var edgePreview = NewEdgeGesturePreview(gestureName, width, height);
        UIElement child = edgePreview is not null
            ? edgePreview
            : gesture is not null && gesture.PointPatterns.Count > 0
            ? NewGestureCanvas(gesture, width, height)
            : new TextBlock
            {
                Text = DisplayName(gestureName),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            };

        return new Border
        {
            Background = SubtleBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Width = width,
            Height = height,
            Child = child
        };
    }

    private FrameworkElement? NewEdgeGesturePreview(string gestureName, double width, double height)
    {
        if (!TryParseEdgeGesture(gestureName, out var isTouchScreen, out var isTouchPad, out var edge, out var direction))
            return null;

        var canvas = new Canvas { Width = width, Height = height };
        var accent = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
        var secondary = new SolidColorBrush(Color.FromArgb(255, 0, 153, 188));
        var muted = IsDark
            ? new SolidColorBrush(Color.FromArgb(132, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(255, 118, 128, 140));
        var deviceFill = IsDark
            ? new SolidColorBrush(Color.FromArgb(34, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(255, 249, 252, 255));

        if (isTouchPad)
        {
            AddTouchPadEdgePreview(canvas, width, height, edge, direction, accent, secondary, muted, deviceFill);
            return canvas;
        }

        var deviceWidth = isTouchScreen ? 72d : 96d;
        var deviceHeight = isTouchScreen ? 58d : 46d;
        var left = (width - deviceWidth) / 2;
        var top = (height - deviceHeight) / 2;
        var right = left + deviceWidth;
        var bottom = top + deviceHeight;

        var body = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = deviceWidth,
            Height = deviceHeight,
            RadiusX = isTouchScreen ? 7 : 10,
            RadiusY = isTouchScreen ? 7 : 10,
            Stroke = muted,
            StrokeThickness = 1.6,
            Fill = deviceFill
        };
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        canvas.Children.Add(body);

        if (isTouchScreen)
            AddPreviewLine(canvas, left + deviceWidth * 0.38, bottom - 5, right - deviceWidth * 0.38, bottom - 5, muted, 1.4);
        else
            AddPreviewLine(canvas, left + 10, top + deviceHeight * 0.68, right - 10, top + deviceHeight * 0.68, muted, 1.2);

        AddEdgeHighlight(canvas, edge, left, top, right, bottom, accent);
        if (string.IsNullOrWhiteSpace(direction))
            AddTapDot(canvas, edge, left, top, right, bottom, accent);
        else
            AddEdgeArrow(canvas, edge, direction, left, top, right, bottom, accent, secondary);

        return canvas;
    }

    private static void AddTouchPadEdgePreview(Canvas canvas, double width, double height, string edge, string direction, Brush accent, Brush secondary, Brush muted, Brush deviceFill)
    {
        var deviceWidth = Math.Min(98d, width - 34d);
        var deviceHeight = Math.Min(58d, height - 12d);
        var left = (width - deviceWidth) / 2;
        var top = (height - deviceHeight) / 2;
        var right = left + deviceWidth;
        var bottom = top + deviceHeight;

        var body = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = deviceWidth,
            Height = deviceHeight,
            RadiusX = 9,
            RadiusY = 9,
            Stroke = muted,
            StrokeThickness = 1.7,
            Fill = deviceFill
        };
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        canvas.Children.Add(body);

        AddPreviewLine(canvas, left + deviceWidth * 0.39, bottom - 5.5, right - deviceWidth * 0.39, bottom - 5.5, muted, 1.5);
        AddEdgeHighlight(canvas, edge, left, top, right, bottom, accent);

        if (string.IsNullOrWhiteSpace(direction))
            AddTapDot(canvas, edge, left, top, right, bottom, accent);
        else
            AddEdgeArrow(canvas, edge, direction, left, top, right, bottom, accent, secondary);
    }

    private static bool TryParseEdgeGesture(string gestureName, out bool isTouchScreen, out bool isTouchPad, out string edge, out string direction)
    {
        isTouchScreen = false;
        isTouchPad = false;
        edge = "";
        direction = "";
        if (string.IsNullOrWhiteSpace(gestureName))
            return false;

        var parts = gestureName.Split('.');
        if (parts.Length < 2)
            return false;

        isTouchScreen = string.Equals(parts[0], "TouchScreenEdge", StringComparison.OrdinalIgnoreCase);
        isTouchPad = string.Equals(parts[0], "TouchPadEdge", StringComparison.OrdinalIgnoreCase);
        if (!isTouchScreen && !isTouchPad)
            return false;

        edge = parts[1];
        direction = parts.Length >= 3 ? parts[2] : "";
        return edge.Equals("Top", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("Bottom", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("Left", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("Right", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddEdgeHighlight(Canvas canvas, string edge, double left, double top, double right, double bottom, Brush brush)
    {
        const double inset = 3;
        if (edge.Equals("Top", StringComparison.OrdinalIgnoreCase))
            AddPreviewLine(canvas, left + 8, top + inset, right - 8, top + inset, brush, 4);
        else if (edge.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
            AddPreviewLine(canvas, left + 8, bottom - inset, right - 8, bottom - inset, brush, 4);
        else if (edge.Equals("Left", StringComparison.OrdinalIgnoreCase))
            AddPreviewLine(canvas, left + inset, top + 8, left + inset, bottom - 8, brush, 4);
        else if (edge.Equals("Right", StringComparison.OrdinalIgnoreCase))
            AddPreviewLine(canvas, right - inset, top + 8, right - inset, bottom - 8, brush, 4);
    }

    private static void AddTapDot(Canvas canvas, string edge, double left, double top, double right, double bottom, Brush brush)
    {
        var x = (left + right) / 2;
        var y = (top + bottom) / 2;
        if (edge.Equals("Top", StringComparison.OrdinalIgnoreCase))
            y = top + 12;
        else if (edge.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
            y = bottom - 12;
        else if (edge.Equals("Left", StringComparison.OrdinalIgnoreCase))
            x = left + 12;
        else if (edge.Equals("Right", StringComparison.OrdinalIgnoreCase))
            x = right - 12;

        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = brush
        };
        Canvas.SetLeft(dot, x - 4.5);
        Canvas.SetTop(dot, y - 4.5);
        canvas.Children.Add(dot);
    }

    private static void AddEdgeArrow(Canvas canvas, string edge, string direction, double left, double top, double right, double bottom, Brush brush, Brush secondaryBrush)
    {
        var midX = (left + right) / 2;
        var midY = (top + bottom) / 2;
        Point start;
        Point end;
        if (direction.Equals("Left", StringComparison.OrdinalIgnoreCase) || direction.Equals("Right", StringComparison.OrdinalIgnoreCase))
        {
            var y = edge.Equals("Bottom", StringComparison.OrdinalIgnoreCase) ? bottom - 15 : top + 15;
            start = direction.Equals("Left", StringComparison.OrdinalIgnoreCase) ? new Point(right - 22, y) : new Point(left + 22, y);
            end = direction.Equals("Left", StringComparison.OrdinalIgnoreCase) ? new Point(left + 22, y) : new Point(right - 22, y);
        }
        else
        {
            var x = edge.Equals("Right", StringComparison.OrdinalIgnoreCase) ? right - 15 : left + 15;
            start = direction.Equals("Up", StringComparison.OrdinalIgnoreCase) ? new Point(x, bottom - 16) : new Point(x, top + 16);
            end = direction.Equals("Up", StringComparison.OrdinalIgnoreCase) ? new Point(x, top + 16) : new Point(x, bottom - 16);
        }

        AddArrow(canvas, start, end, brush, 3.8);
        var origin = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = secondaryBrush
        };
        Canvas.SetLeft(origin, start.X - 3.5);
        Canvas.SetTop(origin, start.Y - 3.5);
        canvas.Children.Add(origin);
    }

    private static void AddArrow(Canvas canvas, Point start, Point end, Brush brush, double thickness)
    {
        AddPreviewLine(canvas, start.X, start.Y, end.X, end.Y, brush, thickness);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double arrowLength = 9;
        const double arrowAngle = Math.PI / 7;
        var leftHead = new Point(end.X - arrowLength * Math.Cos(angle - arrowAngle), end.Y - arrowLength * Math.Sin(angle - arrowAngle));
        var rightHead = new Point(end.X - arrowLength * Math.Cos(angle + arrowAngle), end.Y - arrowLength * Math.Sin(angle + arrowAngle));
        AddPreviewLine(canvas, end.X, end.Y, leftHead.X, leftHead.Y, brush, thickness);
        AddPreviewLine(canvas, end.X, end.Y, rightHead.X, rightHead.Y, brush, thickness);
    }

    private static void AddPreviewLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
            StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round
        });
    }

    private FrameworkElement NewGestureCanvas(LegacyGesture gesture, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height };
        DrawGestureLines(canvas, gesture.PointPatterns, width, height, fillTwoDimensionalBounds: true);
        return canvas;
    }

    private static void DrawGestureLines(Canvas canvas, IReadOnlyList<IReadOnlyList<(double X, double Y)>> pointPatterns, double width, double height, bool fillTwoDimensionalBounds = false)
    {
        var allPoints = pointPatterns.SelectMany(line => line).ToList();
        if (allPoints.Count == 0)
            return;

        var minX = allPoints.Min(point => point.X);
        var maxX = allPoints.Max(point => point.X);
        var minY = allPoints.Min(point => point.Y);
        var maxY = allPoints.Max(point => point.Y);
        var rangeX = maxX - minX;
        var rangeY = maxY - minY;
        var padding = 14d;
        var contentWidth = Math.Max(1, width - padding * 2);
        var contentHeight = Math.Max(1, height - padding * 2);
        var hasWidth = rangeX > 0.001;
        var hasHeight = rangeY > 0.001;
        var isTwoDimensional = hasWidth && hasHeight && Math.Min(rangeX / rangeY, rangeY / rangeX) >= 0.22;

        double scaleX;
        double scaleY;
        double offsetX;
        double offsetY;
        if (!hasWidth && !hasHeight)
        {
            scaleX = 1;
            scaleY = 1;
            offsetX = width / 2;
            offsetY = height / 2;
        }
        else if (fillTwoDimensionalBounds && isTwoDimensional)
        {
            scaleX = contentWidth / rangeX;
            scaleY = contentHeight / rangeY;
            offsetX = padding;
            offsetY = padding;
        }
        else
        {
            var safeRangeX = hasWidth ? rangeX : 1;
            var safeRangeY = hasHeight ? rangeY : 1;
            var scale = Math.Min(contentWidth / safeRangeX, contentHeight / safeRangeY);
            var drawingWidth = hasWidth ? rangeX * scale : 0;
            var drawingHeight = hasHeight ? rangeY * scale : 0;
            scaleX = scale;
            scaleY = scale;
            offsetX = (width - drawingWidth) / 2;
            offsetY = (height - drawingHeight) / 2;
        }

        var colors = new[]
        {
            Color.FromArgb(255, 0, 120, 212),
            Color.FromArgb(255, 0, 153, 188),
            Color.FromArgb(255, 112, 99, 221),
            Color.FromArgb(255, 16, 124, 16),
            Color.FromArgb(255, 232, 17, 35)
        };

        for (var index = 0; index < pointPatterns.Count; index++)
        {
            var line = pointPatterns[index];
            if (line.Count == 1)
            {
                var point = NormalizePreviewPoint(line[0], minX, minY, scaleX, scaleY, offsetX, offsetY);
                var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(colors[index % colors.Length])
                };
                Canvas.SetLeft(dot, point.X - 4);
                Canvas.SetTop(dot, point.Y - 4);
                canvas.Children.Add(dot);
                continue;
            }

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(colors[index % colors.Length]),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var point in line)
                polyline.Points.Add(NormalizePreviewPoint(point, minX, minY, scaleX, scaleY, offsetX, offsetY));
            canvas.Children.Add(polyline);
        }
    }

    private static Windows.Foundation.Point NormalizePreviewPoint((double X, double Y) point, double minX, double minY, double scaleX, double scaleY, double offsetX, double offsetY)
        => new(offsetX + (point.X - minX) * scaleX, offsetY + (point.Y - minY) * scaleY);

    private FrameworkElement NewDialogMapCard()
    {
        var content = NewCardPanel(10);
        content.Children.Add(new TextBlock { Text = "编辑入口", Style = BodyStrongTextBlockStyle });
        content.Children.Add(new TextBlock { Text = "旧版对话框已整理为 WinUI 重构目标：程序匹配、动作设置、命令选择、触发条件、导入导出。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(NewSmallCommandBar(["程序设置", "动作设置", "命令设置", "触发条件", "导入/导出"]));
        return NewCard(content);
    }

    private FrameworkElement NewInfoCard(string title, string subtitle, string detail)
    {
        var content = NewCardPanel();
        content.Children.Add(new TextBlock { Text = title, Style = BodyStrongTextBlockStyle });
        content.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.72 });
        content.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        return NewCard(content);
    }

    private FrameworkElement NewTableHeader(string[] cells) => NewTableRow(cells, true);

    private FrameworkElement NewTableRow(string[] cells, bool header = false, FrameworkElement? trailing = null)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        foreach (var _ in cells)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (trailing is not null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < cells.Length; i++)
        {
            var text = new TextBlock { Text = cells[i], Opacity = header ? 0.72 : 1, FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, cells.Length);
            grid.Children.Add(trailing);
        }

        if (header)
            return new Border { Padding = new Thickness(12, 10, 12, 10), Child = grid };

        return new Border
        {
            Background = SubtleBrush(),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
    }

    private FrameworkElement NewGestureGroup(string title, LegacyGesture[] gestures)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock { Text = $"{title}  {CountText(gestures.Length, L("个", "items", "個", "個", "개"))}", Style = BodyStrongTextBlockStyle });

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var gesture in gestures)
        {
            var content = NewCardPanel(6);
            content.Children.Add(NewGestureCanvas(gesture, 148, 74));
            content.Children.Add(new TextBlock
            {
                Text = DisplayName(gesture.Name),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Style = BodyStrongTextBlockStyle
            });
            content.Children.Add(new TextBlock
            {
                Text = CountText(gesture.FingerCount, L("指", "finger(s)", "指", "本指", "손가락")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.68
            });
            content.Children.Add(NewInlineButtons((L("重训", "Retrain", "重訓", "再学習", "다시 훈련"), async () => await DrawGestureAsync(gesture)), (L("改名", "Rename", "重新命名", "名前変更", "이름 변경"), async () => await RenameGestureAsync(gesture)), (L("删除", "Delete", "刪除", "削除", "삭제"), async () => await DeleteGestureAsync(gesture))));

            wrap.Children.Add(new Border
            {
                Width = 188,
                MinHeight = 178,
                Margin = new Thickness(0, 0, 8, 8),
                Background = SubtleBrush(),
                BorderBrush = BorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = content
            });
        }
        if (gestures.Length == 0)
            wrap.Children.Add(new TextBlock { Text = L("暂无手势", "No gestures", "暫無手勢", "ジェスチャなし", "제스처 없음"), Opacity = 0.68 });

        panel.Children.Add(new ScrollViewer
        {
            Content = wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled
        });
        return NewCard(panel);
    }

    private FrameworkElement NewSettingsGroup(string title, FrameworkElement[] rows)
    {
        var panel = NewCardPanel(0);
        panel.Children.Add(new TextBlock { Text = title, Style = BodyStrongTextBlockStyle, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var row in rows)
            panel.Children.Add(row);
        return NewCard(panel);
    }

    private void UpdateOptionAndReload(string key, string value)
    {
        _pendingOptionUpdates[key] = value;
        _optionSaveTimer.Stop();
        _optionSaveTimer.Start();
    }

    private void UpdateOptionAndReloadNow(string key, string value)
    {
        _pendingOptionUpdates[key] = value;
        _optionSaveTimer.Stop();
        _ = FlushPendingOptionUpdatesAsync();
    }

    private async Task UpdateOptionAndWaitAsync(string key, string value)
    {
        _pendingOptionUpdates[key] = value;
        _optionSaveTimer.Stop();
        await FlushPendingOptionUpdatesAsync();
    }

    private async void OptionSaveTimer_Tick(object? sender, object e)
    {
        _optionSaveTimer.Stop();
        if (_isSavingOptions)
            return;

        await FlushPendingOptionUpdatesAsync();
    }

    private async Task FlushPendingOptionUpdatesAsync()
    {
        if (_isSavingOptions)
        {
            _optionSaveTimer.Stop();
            _optionSaveTimer.Start();
            return;
        }

        if (_pendingOptionUpdates.Count == 0)
            return;

        var updates = _pendingOptionUpdates.ToArray();
        _pendingOptionUpdates.Clear();
        _isSavingOptions = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var (key, value) in updates)
                    _legacyData.UpdateOption(key, value);
            });
            _legacyData = LegacyDataStore.Load();
            await NotifyDaemonAsync(DaemonCommand.LoadConfiguration);
        }
        catch (Exception ex)
        {
            LogException(ex);
            await ShowInfoDialog("选项保存失败", ex.Message);
        }
        finally
        {
            _isSavingOptions = false;
            if (_pendingOptionUpdates.Count > 0)
                _optionSaveTimer.Start();
        }
    }

    private FrameworkElement NewToggleRow(string title, bool isOn, string? configKey = null, string? onValue = null, string? offValue = null)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(configKey))
        {
            toggle.Toggled += (_, _) =>
            {
                UpdateOptionAndReload(configKey, toggle.IsOn ? onValue ?? "True" : offValue ?? "False");
                if (string.Equals(configKey, "DrawingButton", StringComparison.OrdinalIgnoreCase))
                    UpdateOptionAndReload("MouseGesturesDisabledByUser", toggle.IsOn ? "False" : "True");
            };
        }
        return NewSettingRow(title, null, toggle);
    }

    private FrameworkElement NewStartupToggleRow()
    {
        var toggle = new ToggleSwitch
        {
            IsOn = IsStartupEnabled(),
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += async (_, _) =>
        {
            try
            {
                SetStartupEnabled(toggle.IsOn);
            }
            catch (Exception ex)
            {
                toggle.IsOn = IsStartupEnabled();
                await ShowInfoDialog(L("启动项设置失败", "Startup setting failed", "啟動項設定失敗", "スタートアップ設定に失敗しました", "시작 항목 설정 실패"), ex.Message);
            }
        };
        return NewSettingRow(L("Windows 启动时运行", "Run at Windows startup", "Windows 啟動時執行", "Windows 起動時に実行", "Windows 시작 시 실행"), L("登录后启动后台托盘和手势识别服务。", "Start the tray and gesture recognition service after sign-in.", "登入後啟動背景系統匣與手勢辨識服務。", "サインイン後にトレイとジェスチャ認識サービスを起動します。", "로그인 후 트레이와 제스처 인식 서비스를 시작합니다."), toggle);
    }

    private FrameworkElement NewAdminStartupToggleRow(bool isOn)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += async (_, _) =>
        {
            try
            {
                if (toggle.IsOn)
                    await SetAdminStartupEnabledAsync(true);
                else
                    await SetAdminStartupEnabledAsync(false);
                UpdateOptionAndReload("RunAsAdmin", toggle.IsOn ? "True" : "False");
            }
            catch (Exception ex)
            {
                toggle.IsOn = !toggle.IsOn;
                await ShowInfoDialog(L("管理员启动设置失败", "Administrator startup setting failed", "系統管理員啟動設定失敗", "管理者起動設定に失敗しました", "관리자 시작 설정 실패"), ex.Message);
            }
        };
        return NewSettingRow(L("以管理员身份启动", "Start as administrator", "以系統管理員身分啟動", "管理者として起動", "관리자 권한으로 시작"), L("创建或删除 StartGestureSign 计划任务，需要 UAC 确认。", "Creates or removes the StartGestureSign scheduled task. UAC confirmation is required.", "建立或刪除 StartGestureSign 排程工作，需要 UAC 確認。", "StartGestureSign タスクを作成または削除します。UAC 確認が必要です。", "StartGestureSign 예약 작업을 만들거나 삭제합니다. UAC 확인이 필요합니다."), toggle);
    }

    private FrameworkElement NewOneDriveSyncRow()
    {
        var oneDrivePath = _legacyData.OneDriveSyncPath;
        var canSync = !string.IsNullOrWhiteSpace(oneDrivePath);
        var toggle = new ToggleSwitch
        {
            IsOn = _legacyData.OneDriveSyncEnabled,
            IsEnabled = canSync,
            OnContent = L("开", "On", "開", "オン", "켬"),
            OffContent = L("关", "Off", "關", "オフ", "끔"),
            VerticalAlignment = VerticalAlignment.Center
        };

        toggle.Toggled += async (_, _) =>
        {
            if (!toggle.IsEnabled)
                return;

            var requestedValue = toggle.IsOn;
            try
            {
                await FlushPendingOptionUpdatesAsync();
                await Task.Run(() => _legacyData.SetOneDriveSyncEnabled(requestedValue));
                _legacyData = LegacyDataStore.Load();
                await NotifyDaemonAsync(DaemonCommand.LoadConfiguration);
                ShowSelectedPage();
            }
            catch (Exception ex)
            {
                toggle.IsOn = !requestedValue;
                LogException(ex);
                await ShowInfoDialog(
                    L("OneDrive 同步设置失败", "OneDrive sync setting failed", "OneDrive 同步設定失敗", "OneDrive 同期設定に失敗しました", "OneDrive 동기화 설정 실패"),
                    ex.Message);
            }
        };

        var subtitle = canSync
            ? string.Format(CultureInfo.CurrentCulture,
                L("配置将保存到 {0}，OneDrive 会负责跨设备同步。", "Configuration will be saved to {0}; OneDrive handles cross-device sync.", "設定會儲存到 {0}，由 OneDrive 跨裝置同步。", "設定は {0} に保存され、OneDrive がデバイス間で同期します。", "구성은 {0}에 저장되고 OneDrive가 기기 간 동기화합니다."),
                oneDrivePath)
            : L("未检测到 OneDrive 文件夹。请先登录并启用 OneDrive。", "No OneDrive folder was detected. Sign in to OneDrive first.", "未偵測到 OneDrive 資料夾。請先登入並啟用 OneDrive。", "OneDrive フォルダーが見つかりません。先に OneDrive にサインインしてください。", "OneDrive 폴더를 찾을 수 없습니다. 먼저 OneDrive에 로그인하세요.");

        return NewSettingRow(L("同步配置到 OneDrive", "Sync configuration to OneDrive", "同步設定到 OneDrive", "設定を OneDrive に同期", "구성을 OneDrive에 동기화"), subtitle, toggle);
    }

    private FrameworkElement NewOpenSettingsHotKeyRow(string existingSettings)
    {
        var settings = new TextBox { Text = existingSettings, Visibility = Visibility.Collapsed };
        var recorder = NewHotKeyRecorder(settings, existingSettings, onRecorded: value => UpdateOptionAndReloadNow("OpenSettingsHotKey", value));
        recorder.MinWidth = 260;
        recorder.MaxWidth = 360;
        recorder.HorizontalAlignment = HorizontalAlignment.Right;
        settings.TextChanged += (_, _) =>
        {
            UpdateOptionAndReloadNow("OpenSettingsHotKey", settings.Text);
        };

        var clear = NewPillButton(L("清除", "Clear", "清除", "クリア", "지우기"), false);
        clear.Click += (_, _) =>
        {
            settings.Text = "";
            recorder.Text = "";
            UpdateOptionAndReloadNow("OpenSettingsHotKey", "");
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(recorder);
        panel.Children.Add(clear);
        return NewSettingRow(L("快捷键打开设置", "Open settings hotkey", "快速鍵開啟設定", "設定を開くショートカット", "설정 열기 단축키"), L("设置后可直接唤起 GestureSign 设置窗口。", "Use this hotkey to open the GestureSign settings window directly.", "設定後可直接叫出 GestureSign 設定視窗。", "このショートカットで GestureSign 設定ウィンドウを直接開けます。", "이 단축키로 GestureSign 설정 창을 바로 열 수 있습니다."), panel);
    }

    private FrameworkElement NewVisualFeedbackColorRow(string colorValue)
    {
        var originalValue = colorValue?.Trim() ?? string.Empty;
        var committedValue = originalValue;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Right };
        var editPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var color = new TextBox { Width = 190, Text = originalValue, PlaceholderText = L("颜色名、#RRGGBB 或 theme:*", "Color name, #RRGGBB, or theme:*", "色彩名稱、#RRGGBB 或 theme:*", "色名、#RRGGBB、または theme:*", "색상 이름, #RRGGBB 또는 theme:*") };
        var preview = new Border
        {
            Width = 46,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            Background = BrushForVisualFeedbackValue(originalValue)
        };
        var undo = NewPillButton(L("撤销修改", "Undo", "復原修改", "元に戻す", "되돌리기"), false);
        undo.Visibility = Visibility.Collapsed;

        void ApplyPreview(string value, bool updateDaemon)
        {
            var trimmedValue = value.Trim();
            preview.Background = BrushForVisualFeedbackValue(trimmedValue);
            undo.Visibility = Visibility.Collapsed;

            if (updateDaemon && IsVisualFeedbackPreviewValueValid(trimmedValue))
            {
                UpdateOptionAndReloadNow("VisualFeedbackColor", trimmedValue);
                committedValue = trimmedValue;
            }
        }

        var save = NewPillButton(L("保存颜色", "Save color", "儲存色彩", "色を保存", "색상 저장"), false);
        save.Click += async (_, _) =>
        {
            var value = color.Text.Trim();
            if (!IsVisualFeedbackPreviewValueValid(value))
            {
                await ShowInfoDialog(L("颜色无效", "Invalid color", "色彩無效", "無効な色", "잘못된 색상"), L("请输入颜色名、#RRGGBB、#AARRGGBB，或选择下面的预设颜色。", "Enter a color name, #RRGGBB, #AARRGGBB, or choose one of the presets below.", "請輸入色彩名稱、#RRGGBB、#AARRGGBB，或選擇下方預設色彩。", "色名、#RRGGBB、#AARRGGBB を入力するか、下のプリセットを選んでください。", "색상 이름, #RRGGBB, #AARRGGBB를 입력하거나 아래 사전 설정을 선택하세요."));
                return;
            }

            UpdateOptionAndReloadNow("VisualFeedbackColor", value);
            committedValue = value;
            color.Text = value;
            ApplyPreview(value, false);
        };
        var system = NewPillButton(L("使用系统色", "Use system color", "使用系統色彩", "システム色を使用", "시스템 색상 사용"), false);
        system.Click += (_, _) =>
        {
            color.Text = "";
            ApplyPreview(color.Text, true);
        };
        undo.Click += (_, _) =>
        {
            color.Text = committedValue;
            ApplyPreview(committedValue, true);
        };
        editPanel.Children.Add(color);
        editPanel.Children.Add(preview);
        save.Visibility = Visibility.Collapsed;
        editPanel.Children.Add(save);
        editPanel.Children.Add(system);
        undo.Visibility = Visibility.Collapsed;
        editPanel.Children.Add(undo);
        panel.Children.Add(editPanel);

        color.TextChanged += (_, _) =>
        {
            ApplyPreview(color.Text, true);
        };

        var swatches = new StackPanel { Spacing = 6 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var index = 0;
        foreach (var preset in VisualFeedbackColorPresets())
        {
            if (index > 0 && index % 5 == 0)
            {
                swatches.Children.Add(row);
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            }
            row.Children.Add(NewColorPresetButton(preset.Label, preset.Value, preset.Colors, color));
            index++;
        }
        swatches.Children.Add(row);
        panel.Children.Add(swatches);

        return NewSettingRow(L("轨迹颜色", "Trail color", "軌跡色彩", "軌跡の色", "궤적 색상"), L("点击颜色后立即生效。", "Color changes take effect immediately.", "點擊色彩後立即生效。", "色を選ぶとすぐに反映されます。", "색상을 선택하면 즉시 적용됩니다."), panel);
    }

    private static IReadOnlyList<(string Label, string Value, Color[] Colors)> VisualFeedbackColorPresets()
        =>
        [
            ("默认蓝", "DeepSkyBlue", [Color.FromArgb(255, 0, 191, 255)]),
            ("Windows", "theme:windows",
            [
                Color.FromArgb(255, 0, 120, 212),
                Color.FromArgb(255, 0, 188, 242),
                Color.FromArgb(255, 123, 97, 255),
                Color.FromArgb(255, 16, 124, 16)
            ]),
            ("系统蓝", "#0078D4", [Color.FromArgb(255, 0, 120, 212)]),
            ("青色", "Cyan", [Color.FromArgb(255, 0, 255, 255)]),
            ("绿色", "LimeGreen", [Color.FromArgb(255, 50, 205, 50)]),
            ("薄荷", "MediumSeaGreen", [Color.FromArgb(255, 60, 179, 113)]),
            ("黄色", "Gold", [Color.FromArgb(255, 255, 215, 0)]),
            ("橙色", "Orange", [Color.FromArgb(255, 255, 165, 0)]),
            ("红色", "Red", [Color.FromArgb(255, 255, 0, 0)]),
            ("粉色", "DeepPink", [Color.FromArgb(255, 255, 20, 147)]),
            ("紫色", "MediumPurple", [Color.FromArgb(255, 147, 112, 219)]),
            ("白色", "White", [Color.FromArgb(255, 255, 255, 255)]),
            ("黑色", "Black", [Color.FromArgb(255, 0, 0, 0)]),
            ("LGBTQ+", "theme:pride",
            [
                Color.FromArgb(255, 228, 3, 3),
                Color.FromArgb(255, 255, 140, 0),
                Color.FromArgb(255, 255, 237, 0),
                Color.FromArgb(255, 0, 128, 38),
                Color.FromArgb(255, 0, 77, 255),
                Color.FromArgb(255, 117, 7, 135)
            ]),
            ("团结色", "theme:unity",
            [
                Color.FromArgb(255, 0, 0, 0),
                Color.FromArgb(255, 206, 17, 38),
                Color.FromArgb(255, 0, 107, 63),
                Color.FromArgb(255, 252, 209, 22)
            ])
        ];

    private Brush BrushForVisualFeedbackValue(string value)
    {
        var presetBrush = PresetBrushForValue(value);
        if (presetBrush != null)
            return presetBrush;

        if (TryParseWinUIColor(value, out var parsedColor))
            return new SolidColorBrush(parsedColor);

        return new SolidColorBrush(Color.FromArgb(255, 0, 191, 255));
    }

    private bool IsVisualFeedbackPreviewValueValid(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.StartsWith("theme:", StringComparison.OrdinalIgnoreCase)
            || PresetBrushForValue(value) != null
            || TryParseWinUIColor(value, out _);
    }

    private Brush? PresetBrushForValue(string value)
    {
        var preset = VisualFeedbackColorPresets().FirstOrDefault(item => item.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (preset.Colors == null || preset.Colors.Length == 0)
            return null;
        return preset.Colors.Length <= 1 ? new SolidColorBrush(preset.Colors[0]) : NewLinearGradientBrush(preset.Colors);
    }

    private static bool TryParseWinUIColor(string value, out Color color)
    {
        color = default;
        value = value.Trim();
        if (value.Length == 7 && value[0] == '#'
            && byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        if (value.Length == 9 && value[0] == '#'
            && byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            && byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(value.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        var property = typeof(Colors).GetProperties()
            .FirstOrDefault(item => item.Name.Equals(value, StringComparison.OrdinalIgnoreCase) && item.PropertyType == typeof(Color));
        if (property?.GetValue(null) is Color namedColor)
        {
            color = namedColor;
            return true;
        }

        return false;
    }

    private Button NewColorPresetButton(string label, string value, Color[] colors, TextBox target)
    {
        var stack = new StackPanel { Spacing = 3, Width = 64 };
        stack.Children.Add(new Border
        {
            Height = 16,
            CornerRadius = new CornerRadius(6),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            Background = colors.Length <= 1 ? new SolidColorBrush(colors[0]) : NewLinearGradientBrush(colors)
        });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var button = new Button
        {
            Content = stack,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 4, 5, 4),
            MinWidth = 74
        };
        button.Click += (_, _) =>
        {
            target.Text = value;
        };
        return button;
    }

    private static LinearGradientBrush NewLinearGradientBrush(IReadOnlyList<Color> colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        for (var i = 0; i < colors.Count; i++)
        {
            var offset = colors.Count == 1 ? 0d : i / (double)(colors.Count - 1);
            brush.GradientStops.Add(new GradientStop { Color = colors[i], Offset = offset });
        }
        return brush;
    }

    private FrameworkElement NewPenButtonRow(int penGestureButton)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var right = new CheckBox { Content = L("右键", "Right button", "右鍵", "右ボタン", "오른쪽 버튼"), IsChecked = (penGestureButton & 4) != 0 };
        var eraser = new CheckBox { Content = L("橡皮擦", "Eraser", "橡皮擦", "消しゴム", "지우개"), IsChecked = (penGestureButton & 16) != 0 };
        var tip = new CheckBox { Content = L("笔尖", "Tip", "筆尖", "ペン先", "펜촉"), IsChecked = (penGestureButton & 1) != 0 };
        var hover = new CheckBox { Content = L("悬停", "Hover", "懸停", "ホバー", "호버"), IsChecked = (penGestureButton & 2) != 0 };
        CheckBox[] boxes = [right, eraser, tip, hover];
        foreach (var box in boxes)
        {
            box.Click += (_, _) =>
            {
                var value = (right.IsChecked == true ? 4 : 0)
                    | (eraser.IsChecked == true ? 16 : 0)
                    | (tip.IsChecked == true ? 1 : 0)
                    | (hover.IsChecked == true ? 2 : 0);
                UpdateOptionAndReload("PenGestureButton", value.ToString(CultureInfo.InvariantCulture));
            };
            panel.Children.Add(box);
        }
        return NewSettingRow(L("触控笔按钮", "Pen buttons", "觸控筆按鈕", "ペンボタン", "펜 버튼"), null, panel);
    }

    private FrameworkElement NewSliderRow(string title, double value, double minimum, double maximum, double step, string configKey, Func<double, string> toConfigValue, Func<double, string> toDisplayText)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var valueLabel = new TextBlock { Text = toDisplayText(value), Width = 72, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.68 };
        var slider = new Slider
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = step,
            Width = 220,
            MinWidth = 160
        };
        slider.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue) || double.IsInfinity(args.NewValue))
                return;
            valueLabel.Text = toDisplayText(args.NewValue);
            UpdateOptionAndReload(configKey, toConfigValue(args.NewValue));
        };
        panel.Children.Add(slider);
        panel.Children.Add(valueLabel);
        return NewSettingRow(title, null, panel);
    }

    private FrameworkElement NewComboRow(string title, string[] items, string[] values, string configKey, int selectedIndex)
    {
        var combo = new ComboBox { Width = 220 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Length)
            {
                var value = values[combo.SelectedIndex];
                if (string.Equals(configKey, "CultureName", StringComparison.OrdinalIgnoreCase))
                {
                    _uiCultureName = value;
                    ApplyUiCulture(value);
                    RefreshNavigationText();
                    UpdateOptionAndReloadNow(configKey, value);
                    ShowSelectedPage();
                    return;
                }

                UpdateOptionAndReload(configKey, value);
            }
        };
        return NewSettingRow(title, null, combo);
    }

    private FrameworkElement NewButtonRow(string title, string[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in buttons)
            panel.Children.Add(NewPillButton(button));
        return NewSettingRow(title, null, panel);
    }

    private FrameworkElement NewSettingRow(string title, string? subtitle, FrameworkElement control)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(2);
        text.Children.Add(new TextBlock { Text = title, Style = ResourceStyle("BodyTextBlockStyle") });
        if (!string.IsNullOrWhiteSpace(subtitle))
            text.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.62, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border
        {
            Padding = new Thickness(0, 12, 0, 12),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private string MatchSummary(LegacyApplication app)
    {
        if (app.Type == "全局")
            return L("全局动作", "Global Actions", "全域動作", "グローバルアクション", "전역 동작");

        var match = string.IsNullOrWhiteSpace(app.MatchString) ? L("匹配项", "Match", "比對項", "一致項目", "매칭 항목") : app.MatchString;
        return $"{MatchUsingText(app.MatchUsing)} · {match}";
    }

    private string ApplicationDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant() switch
        {
            "(全局动作)" or "全局动作" => L("(全局动作)", "(Global Actions)", "(全域動作)", "(グローバルアクション)", "(전역 동작)"),
            "windows 资源管理器" or "windows explorer" => L("Windows 资源管理器", "Windows Explorer", "Windows 檔案總管", "Windows エクスプローラー", "Windows 탐색기"),
            "浏览器" => L("浏览器", "Browser", "瀏覽器", "ブラウザー", "브라우저"),
            "uwp 应用" or "uwp app" or "uwp application" => L("UWP 应用", "UWP App", "UWP 應用程式", "UWP アプリ", "UWP 앱"),
            _ => value
        };
    }

    private string DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return L("未命名", "Unnamed", "未命名", "名前なし", "이름 없음");

        var trimmed = value.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "save" or "保存" => L("保存", "Save", "儲存", "保存", "저장"),
            "copy" or "复制" => L("复制", "Copy", "複製", "コピー", "복사"),
            "cut" or "剪切" => L("剪切", "Cut", "剪下", "切り取り", "잘라내기"),
            "paste" or "粘贴" => L("粘贴", "Paste", "貼上", "貼り付け", "붙여넣기"),
            "undo" or "撤销" => L("撤销", "Undo", "復原", "元に戻す", "실행 취소"),
            "redo" or "重做" => L("重做", "Redo", "重做", "やり直し", "다시 실행"),
            "delete" or "删除" => L("删除", "Delete", "刪除", "削除", "삭제"),
            "select all" or "全选" => L("全选", "Select All", "全選", "すべて選択", "모두 선택"),
            "open web browser" or "open browser" or "open default browser" or "打开网页浏览器" or "打开浏览器" or "打开默认浏览器" => L("打开网页浏览器", "Open Web Browser", "開啟網頁瀏覽器", "Web ブラウザーを開く", "웹 브라우저 열기"),
            "close" or "关闭" => L("关闭", "Close", "關閉", "閉じる", "닫기"),
            "关闭窗口" => L("关闭窗口", "Close Window", "關閉視窗", "ウィンドウを閉じる", "창 닫기"),
            "close tab" or "关闭标签页" => L("关闭标签页", "Close Tab", "關閉分頁", "タブを閉じる", "탭 닫기"),
            "new tab" or "新建标签页" => L("新建标签页", "New Tab", "新增分頁", "新しいタブ", "새 탭"),
            "reopen closed tab" or "重新打开关闭的标签页" => L("重新打开关闭的标签页", "Reopen Closed Tab", "重新開啟關閉的分頁", "閉じたタブを再度開く", "닫은 탭 다시 열기"),
            "previous tab" or "上一个标签页" => L("上一个标签页", "Previous Tab", "上一個分頁", "前のタブ", "이전 탭"),
            "next tab" or "下一个标签页" => L("下一个标签页", "Next Tab", "下一個分頁", "次のタブ", "다음 탭"),
            "back" or "后退" => L("后退", "Back", "返回", "戻る", "뒤로"),
            "forward" or "前进" => L("前进", "Forward", "前進", "進む", "앞으로"),
            "refresh" or "刷新" => L("刷新", "Refresh", "重新整理", "更新", "새로 고침"),
            "minimize" or "最小化" => L("最小化", "Minimize", "最小化", "最小化", "최소화"),
            "maximize" or "最大化" => L("最大化", "Maximize", "最大化", "最大化", "최대화"),
            "restore" or "还原" => L("还原", "Restore", "還原", "復元", "복원"),
            "maximize/restore" or "最大化/还原" => L("最大化/还原", "Maximize/Restore", "最大化/還原", "最大化/復元", "최대화/복원"),
            "show desktop" or "显示桌面" => L("显示桌面", "Show Desktop", "顯示桌面", "デスクトップを表示", "바탕 화면 표시"),
            "switch window" or "切换窗口" => L("切换窗口", "Switch Window", "切換視窗", "ウィンドウ切替", "창 전환"),
            "previous application" or "上一个窗口" => L("上一个窗口", "Previous Window", "上一個視窗", "前のウィンドウ", "이전 창"),
            "next application" or "下一个窗口" => L("下一个窗口", "Next Window", "下一個視窗", "次のウィンドウ", "다음 창"),
            "increase volume" or "volume up" or "增大音量" => L("增大音量", "Volume Up", "增大音量", "音量を上げる", "볼륨 높이기"),
            "decrease volume" or "volume down" or "减小音量" => L("减小音量", "Volume Down", "降低音量", "音量を下げる", "볼륨 낮추기"),
            "mute" or "静音" => L("静音", "Mute", "靜音", "ミュート", "음소거"),
            "play/pause" or "play pause" or "播放/暂停" => L("播放/暂停", "Play/Pause", "播放/暫停", "再生/一時停止", "재생/일시 정지"),
            "媒体播放/暂停" => L("媒体播放/暂停", "Media Play/Pause", "媒體播放/暫停", "メディア再生/一時停止", "미디어 재생/일시 정지"),
            "previous track" or "上一曲" => L("上一曲", "Previous Track", "上一首", "前のトラック", "이전 트랙"),
            "next track" or "下一曲" => L("下一曲", "Next Track", "下一首", "次のトラック", "다음 트랙"),
            "run command" or "运行命令" => L("运行命令", "Run Command", "執行命令", "コマンド実行", "명령 실행"),
            "open file" or "打开文件" => L("打开文件", "Open File", "開啟檔案", "ファイルを開く", "파일 열기"),
            "launch app" or "启动应用" => L("启动应用", "Launch App", "啟動應用程式", "アプリ起動", "앱 실행"),
            "delay" or "延迟等待" => L("延迟等待", "Delay", "延遲等待", "遅延", "지연"),
            "mouse action" or "mouse actions" or "鼠标动作" => L("鼠标动作", "Mouse Action", "滑鼠動作", "マウス操作", "마우스 동작"),
            "screen brightness" or "屏幕亮度" => L("屏幕亮度", "Screen Brightness", "螢幕亮度", "画面の明るさ", "화면 밝기"),
            "brightness up" or "提高亮度" => L("提高亮度", "Brightness Up", "提高亮度", "明るくする", "밝기 높이기"),
            "brightness down" or "降低亮度" => L("降低亮度", "Brightness Down", "降低亮度", "暗くする", "밝기 낮추기"),
            "activate window" or "激活窗口" => L("激活窗口", "Activate Window", "啟用視窗", "ウィンドウをアクティブ化", "창 활성화"),
            "touch keyboard" or "触摸键盘" => L("触摸键盘", "Touch Keyboard", "觸控鍵盤", "タッチキーボード", "터치 키보드"),
            "toggle window topmost" or "窗口置顶" => L("窗口置顶", "Toggle Topmost", "視窗置頂", "最前面表示切替", "항상 위 전환"),
            "temporarily disable" or "临时禁用手势" => L("临时禁用手势", "Temporarily Disable Gestures", "暫時停用手勢", "ジェスチャを一時無効化", "제스처 임시 비활성화"),
            "toggle disable gestures" or "切换禁用手势" => L("切换禁用手势", "Toggle Gesture Disable", "切換停用手勢", "ジェスチャ無効化切替", "제스처 비활성화 전환"),
            "发送快捷键" => L("发送快捷键", "Send Hotkey", "傳送快速鍵", "ショートカット送信", "단축키 보내기"),
            "显示/隐藏触摸键盘" => L("显示/隐藏触摸键盘", "Show/Hide Touch Keyboard", "顯示/隱藏觸控鍵盤", "タッチキーボード表示/非表示", "터치 키보드 표시/숨기기"),
            "s形手势" => L("S形手势", "S-shaped Gesture", "S 形手勢", "S 字ジェスチャ", "S자 제스처"),
            "双指左滑" => L("双指左滑", "Two-finger Swipe Left", "雙指左滑", "2 本指左スワイプ", "두 손가락 왼쪽 스와이프"),
            "双指上下滑" => L("双指上下滑", "Two-finger Vertical Swipe", "雙指上下滑", "2 本指上下スワイプ", "두 손가락 위아래 스와이프"),
            "双指平行左滑" => L("双指平行左滑", "Two-finger Parallel Swipe Left", "雙指平行左滑", "2 本指平行左スワイプ", "두 손가락 평행 왼쪽 스와이프"),
            "双指手" => L("双指手", "Two-finger Gesture", "雙指手勢", "2 本指ジェスチャ", "두 손가락 제스처"),
            "三指左滑" => L("三指左滑", "Three-finger Swipe Left", "三指左滑", "3 本指左スワイプ", "세 손가락 왼쪽 스와이프"),
            "三指l形" => L("三指L形", "Three-finger L Shape", "三指 L 形", "3 本指 L 字", "세 손가락 L자"),
            "四指l形" => L("四指L形", "Four-finger L Shape", "四指 L 形", "4 本指 L 字", "네 손가락 L자"),
            "四指点按" => L("四指点按", "Four-finger Tap", "四指點按", "4 本指タップ", "네 손가락 탭"),
            "四指下滑" => L("四指下滑", "Four-finger Swipe Down", "四指下滑", "4 本指下スワイプ", "네 손가락 아래 스와이프"),
            "四指右滑" => L("四指右滑", "Four-finger Swipe Right", "四指右滑", "4 本指右スワイプ", "네 손가락 오른쪽 스와이프"),
            "四指左滑" => L("四指左滑", "Four-finger Swipe Left", "四指左滑", "4 本指左スワイプ", "네 손가락 왼쪽 스와이프"),
            "touchpadedge.top" => L("触控板上边缘点击", "Touchpad Top Edge Tap", "觸控板上邊緣點擊", "タッチパッド上端タップ", "터치패드 위쪽 가장자리 탭"),
            "touchpadedge.bottom" => L("触控板下边缘点击", "Touchpad Bottom Edge Tap", "觸控板下邊緣點擊", "タッチパッド下端タップ", "터치패드 아래쪽 가장자리 탭"),
            "touchpadedge.left" => L("触控板左边缘点击", "Touchpad Left Edge Tap", "觸控板左邊緣點擊", "タッチパッド左端タップ", "터치패드 왼쪽 가장자리 탭"),
            "touchpadedge.right" => L("触控板右边缘点击", "Touchpad Right Edge Tap", "觸控板右邊緣點擊", "タッチパッド右端タップ", "터치패드 오른쪽 가장자리 탭"),
            "touchscreenedge.top" => L("触控屏上边缘点击", "Touchscreen Top Edge Tap", "觸控螢幕上邊緣點擊", "タッチスクリーン上端タップ", "터치스크린 위쪽 가장자리 탭"),
            "touchscreenedge.bottom" => L("触控屏下边缘点击", "Touchscreen Bottom Edge Tap", "觸控螢幕下邊緣點擊", "タッチスクリーン下端タップ", "터치스크린 아래쪽 가장자리 탭"),
            "touchscreenedge.left" => L("触控屏左边缘点击", "Touchscreen Left Edge Tap", "觸控螢幕左邊緣點擊", "タッチスクリーン左端タップ", "터치스크린 왼쪽 가장자리 탭"),
            "touchscreenedge.right" => L("触控屏右边缘点击", "Touchscreen Right Edge Tap", "觸控螢幕右邊緣點擊", "タッチスクリーン右端タップ", "터치스크린 오른쪽 가장자리 탭"),
            "left" or "向左" => L("向左", "Left", "向左", "左", "왼쪽"),
            "right" or "向右" => L("向右", "Right", "向右", "右", "오른쪽"),
            "up" or "向上" => L("向上", "Up", "向上", "上", "위"),
            "down" or "向下" => L("向下", "Down", "向下", "下", "아래"),
            _ => trimmed
        };
    }

    private string ActionSummary(LegacyApplication app, LegacyAction action)
    {
        var commands = action.Commands.Count == 0
            ? L("无命令", "No command", "無命令", "コマンドなし", "명령 없음")
            : string.Join("、", action.Commands.Take(2).Select(command => string.IsNullOrWhiteSpace(command.Name) ? PluginName(command.PluginClass) : DisplayName(command.Name)));

        if (action.Commands.Count > 2)
            commands += $" {L("等", "and", "等", "ほか", "외")} {CountText(action.Commands.Count, L("个命令", "commands", "個命令", "個のコマンド", "개 명령"))}";

        var scope = app.Type == "全局" ? L("全局动作", "Global Actions", "全域動作", "グローバルアクション", "전역 동작") : ApplicationDisplayName(app.Name);
        return $"{scope} · {commands}";
    }

    private string CommandPreviewText(string pluginClass, string settings)
    {
        var pluginName = PluginName(pluginClass);
        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
        {
            var hotKey = HotKeyDisplayText(settings);
            return string.IsNullOrWhiteSpace(hotKey)
                ? $"{pluginName} · {L("尚未录制快捷键", "No shortcut recorded", "尚未錄製快速鍵", "ショートカット未登録", "단축키가 아직 없습니다")}"
                : $"{pluginName} · {hotKey}";
        }

        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
        {
            var command = JsonStringValue(settings, "Command", "");
            return string.IsNullOrWhiteSpace(command)
                ? $"{pluginName} · {L("尚未填写命令", "No command entered", "尚未填寫命令", "コマンド未入力", "명령이 아직 없습니다")}"
                : $"{pluginName} · {command}";
        }

        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
        {
            var path = JsonStringValue(settings, "Path", "");
            return string.IsNullOrWhiteSpace(path)
                ? $"{pluginName} · {L("尚未选择文件", "No file selected", "尚未選擇檔案", "ファイル未選択", "파일이 아직 없습니다")}"
                : $"{pluginName} · {path}";
        }

        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
        {
            var value = JsonStringValue(settings, "Value", "");
            var key = JsonStringValue(settings, "Key", "");
            var target = string.IsNullOrWhiteSpace(value) ? key : value;
            return string.IsNullOrWhiteSpace(target)
                ? $"{pluginName} · {L("尚未选择应用", "No app selected", "尚未選擇應用程式", "アプリ未選択", "앱이 아직 없습니다")}"
                : $"{pluginName} · {target}";
        }

        return string.IsNullOrWhiteSpace(settings)
            ? pluginName
            : $"{pluginName} · {settings}";
    }

    private static bool ShouldCreateCommand(string pluginClass, string settings)
    {
        if (string.IsNullOrWhiteSpace(pluginClass))
            return false;

        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(HotKeyDisplayText(settings));
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(JsonStringValue(settings, "Command", ""));
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(JsonStringValue(settings, "Path", ""));
        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(JsonStringValue(settings, "Key", ""));

        return IsSettingsFreePlugin(pluginClass) || !string.IsNullOrWhiteSpace(settings);
    }

    private string PluginName(string pluginClass)
    {
        if (string.IsNullOrWhiteSpace(pluginClass))
            return L("插件命令", "Plugin Command", "外掛命令", "プラグインコマンド", "플러그인 명령");

        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
            return L("快捷键", "Hotkey", "快速鍵", "ショートカット", "단축키");
        if (pluginClass.Contains("DefaultBrowser", StringComparison.OrdinalIgnoreCase))
            return L("打开默认浏览器", "Open Default Browser", "開啟預設瀏覽器", "既定のブラウザーを開く", "기본 브라우저 열기");
        if (pluginClass.Contains("PreviousApplication", StringComparison.OrdinalIgnoreCase))
            return L("上一窗口", "Previous Window", "上一個視窗", "前のウィンドウ", "이전 창");
        if (pluginClass.Contains("NextApplication", StringComparison.OrdinalIgnoreCase))
            return L("下一窗口", "Next Window", "下一個視窗", "次のウィンドウ", "다음 창");
        if (pluginClass.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            return L("音量", "Volume", "音量", "音量", "볼륨");
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return L("运行命令", "Run Command", "執行命令", "コマンド実行", "명령 실행");
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return L("打开文件", "Open File", "開啟檔案", "ファイルを開く", "파일 열기");
        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            return L("启动应用", "Launch App", "啟動應用程式", "アプリ起動", "앱 실행");
        if (pluginClass.Contains("Delay", StringComparison.OrdinalIgnoreCase))
            return L("延迟等待", "Delay", "延遲等待", "遅延", "지연");
        if (pluginClass.Contains("MouseActions", StringComparison.OrdinalIgnoreCase))
            return L("鼠标动作", "Mouse Action", "滑鼠動作", "マウス操作", "마우스 동작");
        if (pluginClass.Contains("ScreenBrightness", StringComparison.OrdinalIgnoreCase))
            return L("屏幕亮度", "Screen Brightness", "螢幕亮度", "画面の明るさ", "화면 밝기");
        if (pluginClass.Contains("ActivateWindow", StringComparison.OrdinalIgnoreCase))
            return L("激活窗口", "Activate Window", "啟用視窗", "ウィンドウをアクティブ化", "창 활성화");
        if (pluginClass.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase))
            return L("触摸键盘", "Touch Keyboard", "觸控鍵盤", "タッチキーボード", "터치 키보드");
        if (pluginClass.Contains("MaximizeRestore", StringComparison.OrdinalIgnoreCase))
            return L("最大化/还原", "Maximize/Restore", "最大化/還原", "最大化/復元", "최대화/복원");
        if (pluginClass.Contains("Minimize", StringComparison.OrdinalIgnoreCase))
            return L("最小化", "Minimize", "最小化", "最小化", "최소화");
        if (pluginClass.Contains("ToggleWindowTopmost", StringComparison.OrdinalIgnoreCase))
            return L("窗口置顶", "Toggle Topmost", "視窗置頂", "最前面表示切替", "항상 위 전환");
        if (pluginClass.Contains("TemporarilyDisable", StringComparison.OrdinalIgnoreCase))
            return L("临时禁用手势", "Temporarily Disable Gestures", "暫時停用手勢", "ジェスチャを一時無効化", "제스처 임시 비활성화");
        if (pluginClass.Contains("ToggleDisableGestures", StringComparison.OrdinalIgnoreCase))
            return L("切换禁用手势", "Toggle Gesture Disable", "切換停用手勢", "ジェスチャ無効化切替", "제스처 비활성화 전환");

        var lastDot = pluginClass.LastIndexOf('.');
        return lastDot >= 0 && lastDot + 1 < pluginClass.Length ? pluginClass[(lastDot + 1)..] : pluginClass;
    }

    private void AddPluginItems(ComboBox plugin)
    {
        plugin.Items.Add(L("快捷键", "Hotkey", "快速鍵", "ショートカット", "단축키"));
        plugin.Items.Add(L("音量", "Volume", "音量", "音量", "볼륨"));
        plugin.Items.Add(L("运行命令", "Run Command", "執行命令", "コマンド実行", "명령 실행"));
        plugin.Items.Add(L("打开文件", "Open File", "開啟檔案", "ファイルを開く", "파일 열기"));
        plugin.Items.Add(L("启动应用", "Launch App", "啟動應用程式", "アプリ起動", "앱 실행"));
        plugin.Items.Add(L("延迟等待", "Delay", "延遲等待", "遅延", "지연"));
        plugin.Items.Add(L("鼠标动作", "Mouse Action", "滑鼠動作", "マウス操作", "마우스 동작"));
        plugin.Items.Add(L("调整亮度", "Adjust Brightness", "調整亮度", "明るさ調整", "밝기 조정"));
        plugin.Items.Add(L("激活窗口", "Activate Window", "啟用視窗", "ウィンドウをアクティブ化", "창 활성화"));
        plugin.Items.Add(L("触摸键盘", "Touch Keyboard", "觸控鍵盤", "タッチキーボード", "터치 키보드"));
        plugin.Items.Add(L("最大化/还原", "Maximize/Restore", "最大化/還原", "最大化/復元", "최대화/복원"));
        plugin.Items.Add(L("最小化", "Minimize", "最小化", "最小化", "최소화"));
        plugin.Items.Add(L("窗口置顶", "Toggle Topmost", "視窗置頂", "最前面表示切替", "항상 위 전환"));
        plugin.Items.Add(L("临时禁用", "Temporarily Disable", "暫時停用", "一時無効化", "임시 비활성화"));
        plugin.Items.Add(L("切换禁用", "Toggle Disable", "切換停用", "無効化切替", "비활성화 전환"));
        plugin.Items.Add(L("自定义插件", "Custom Plugin", "自訂外掛", "カスタムプラグイン", "사용자 지정 플러그인"));
    }

    private static string PluginClassFromIndex(int index)
        => index switch
        {
            1 => "GestureSign.CorePlugins.Volume.VolumePlugin",
            2 => "GestureSign.CorePlugins.RunCommand.RunCommandPlugin",
            3 => "GestureSign.CorePlugins.OpenFile.OpenFilePlugin",
            4 => "GestureSign.CorePlugins.LaunchApp.LaunchApp",
            5 => "GestureSign.CorePlugins.Delay.Delay",
            6 => "GestureSign.CorePlugins.MouseActions.MouseActionsPlugin",
            7 => "GestureSign.CorePlugins.ScreenBrightness.ScreenBrightnessPlugin",
            8 => "GestureSign.CorePlugins.ActivateWindow.ActivateWindowPlugin",
            9 => "GestureSign.CorePlugins.TouchKeyboard.TouchKeyboard",
            10 => "GestureSign.CorePlugins.MaximizeRestore",
            11 => "GestureSign.CorePlugins.Minimize",
            12 => "GestureSign.CorePlugins.ToggleWindowTopmost",
            13 => "GestureSign.CorePlugins.TemporarilyDisable",
            14 => "GestureSign.CorePlugins.ToggleDisableGestures",
            15 => "",
            _ => "GestureSign.CorePlugins.HotKey.HotKeyPlugin"
        };

    private static int PluginIndex(string pluginClass)
    {
        if (pluginClass.Contains("DefaultBrowser", StringComparison.OrdinalIgnoreCase))
            return 15;
        if (pluginClass.Contains("PreviousApplication", StringComparison.OrdinalIgnoreCase))
            return 15;
        if (pluginClass.Contains("NextApplication", StringComparison.OrdinalIgnoreCase))
            return 15;
        if (pluginClass.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (pluginClass.Contains("Delay", StringComparison.OrdinalIgnoreCase))
            return 5;
        if (pluginClass.Contains("MouseActions", StringComparison.OrdinalIgnoreCase))
            return 6;
        if (pluginClass.Contains("ScreenBrightness", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (pluginClass.Contains("ActivateWindow", StringComparison.OrdinalIgnoreCase))
            return 8;
        if (pluginClass.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase))
            return 9;
        if (pluginClass.Contains("MaximizeRestore", StringComparison.OrdinalIgnoreCase))
            return 10;
        if (pluginClass.Contains("Minimize", StringComparison.OrdinalIgnoreCase))
            return 11;
        if (pluginClass.Contains("ToggleWindowTopmost", StringComparison.OrdinalIgnoreCase))
            return 12;
        if (pluginClass.Contains("TemporarilyDisable", StringComparison.OrdinalIgnoreCase))
            return 13;
        if (pluginClass.Contains("ToggleDisableGestures", StringComparison.OrdinalIgnoreCase))
            return 14;
        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
            return 0;
        return 15;
    }

    private static string PluginSettingsTemplate(string pluginClass)
    {
        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
            return "{\"Windows\":false,\"Control\":true,\"Shift\":false,\"Alt\":false,\"KeyCode\":[67],\"SendByKeybdEvent\":false}";
        if (pluginClass.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            return "{\"Method\":0,\"Percent\":10}";
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return "{\"Command\":\"notepad\",\"ShowCmd\":false,\"Shell\":\"CMD\",\"RunAsAdministrator\":false}";
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return "{\"Path\":\"C:\\\\Windows\\\\System32\\\\notepad.exe\",\"Variables\":\"\"}";
        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            return "{\"Key\":\"\",\"Value\":\"\"}";
        if (pluginClass.Contains("Delay", StringComparison.OrdinalIgnoreCase))
            return "{\"WaitType\":0,\"Timeout\":500}";
        if (pluginClass.Contains("MouseActions", StringComparison.OrdinalIgnoreCase))
            return "{\"MouseAction\":257,\"ActionLocation\":2,\"MovePoint\":{\"X\":0,\"Y\":0},\"ScrollAmount\":3}";
        if (pluginClass.Contains("ScreenBrightness", StringComparison.OrdinalIgnoreCase))
            return "{\"Method\":0,\"Percent\":10}";
        if (pluginClass.Contains("ActivateWindow", StringComparison.OrdinalIgnoreCase))
            return "{\"ClassName\":\"\",\"Caption\":\"\",\"IsRegEx\":false,\"Timeout\":1000}";
        if (pluginClass.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase))
            return "";
        return "";
    }

    private static int DrawingButtonIndex(int drawingButton)
        => drawingButton switch
        {
            4194304 => 1,
            8388608 => 2,
            16777216 => 3,
            _ => 0
        };

    private static int NormalizeDrawingButton(int drawingButton, int fallback = 0)
        => drawingButton is 2097152 or 4194304 or 8388608 or 16777216 ? drawingButton : fallback;

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    private static int MouseActionIndex(int mouseAction)
        => mouseAction switch
        {
            1 => 1,
            2 => 2,
            1048576 => 3,
            2097152 => 4,
            4194304 => 5,
            8388608 => 6,
            16777216 => 7,
            _ => 0
        };

    private static int MouseActionValue(int index)
        => index switch
        {
            1 => 1,
            2 => 2,
            3 => 1048576,
            4 => 2097152,
            5 => 4194304,
            6 => 8388608,
            7 => 16777216,
            _ => 0
        };

    private static int CultureIndex(string cultureName)
    {
        if (cultureName.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return 5;
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    private static async Task<bool> NotifyDaemonAsync(DaemonCommand command)
    {
        if (await SendDaemonCommandAsync(command))
            return true;

        if (!StartDaemonIfAvailable())
            return false;

        await Task.Delay(600);
        return await SendDaemonCommandAsync(command);
    }

    private static async Task EnsureDaemonRunningAsync()
    {
        if (_isExitingApplication)
            return;

        if (await NotifyDaemonAsync(DaemonCommand.LoadConfiguration))
        {
            await NotifyDaemonAsync(DaemonCommand.LoadApplications);
            await NotifyDaemonAsync(DaemonCommand.LoadGestures);
        }
    }

    private static async Task<bool> SendDaemonCommandAsync(DaemonCommand command)
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().User?.ToString();
            if (string.IsNullOrWhiteSpace(user))
                return false;

            await using var pipe = new NamedPipeClientStream(".", $"GestureSignDaemon-{user}", PipeDirection.Out, PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(cancellation.Token);
            pipe.WriteByte((byte)command);
            await pipe.FlushAsync(cancellation.Token);
            return true;
        }
        catch
        {
            // The daemon may not be running while the settings UI is open.
            return false;
        }
    }

    private static bool StartDaemonIfAvailable()
    {
        try
        {
            if (Process.GetProcessesByName("GestureSign").Any())
                return true;

            var daemonPath = FindDaemonPath();
            if (daemonPath is null)
            {
                LogWinUiDaemonMessage("Daemon start skipped. Reason=NotFound");
                return false;
            }

            var now = DateTime.UtcNow;
            if (now - _lastDaemonStartAttemptUtc < TimeSpan.FromSeconds(3))
                return true;

            _lastDaemonStartAttemptUtc = now;
            LogWinUiDaemonMessage($"Daemon start requested. Path={daemonPath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = daemonPath,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            LogWinUiDaemonMessage($"Daemon start failed. {ex}");
            return false;
        }
    }

    private static void LogWinUiDaemonMessage(string message)
    {
        try
        {
            var logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign V2");
            Directory.CreateDirectory(logRoot);
            File.AppendAllText(Path.Combine(logRoot, "GestureSign.WinUI.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n\r\n");
        }
        catch
        {
        }
    }

    private static bool IsStartupEnabled()
        => File.Exists(StartupShortcutPath());

    private static void SetStartupEnabled(bool enabled)
    {
        var shortcut = StartupShortcutPath();
        if (!enabled)
        {
            if (File.Exists(shortcut))
                File.Delete(shortcut);
            return;
        }

        var daemonPath = FindDaemonPath();
        if (!File.Exists(daemonPath))
            throw new FileNotFoundException("未找到后台程序 GestureSign.exe。", daemonPath);

        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(shortcut);
        link.TargetPath = daemonPath;
        link.WorkingDirectory = Path.GetDirectoryName(daemonPath);
        link.IconLocation = daemonPath;
        link.Description = "GestureSign background service";
        link.Save();
    }

    private static async Task SetAdminStartupEnabledAsync(bool enabled)
    {
        var daemonPath = FindDaemonPath();
        if (enabled && !File.Exists(daemonPath))
            throw new FileNotFoundException("未找到后台程序 GestureSign.exe。", daemonPath);

        var args = enabled
            ? $" /create /tn StartGestureSign /f /sc onlogon /rl highest /tr \"\\\"{daemonPath}\\\"\""
            : " /delete /tn StartGestureSign /f";

        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        });
        if (process is null)
            throw new InvalidOperationException("无法启动 schtasks.exe。");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"schtasks 退出代码: {process.ExitCode}");
    }

    private static string StartupShortcutPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "GestureSign.lnk");

    private static string FindDaemonPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "GestureSign.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "bin", "Release", "GestureSign.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "..", "bin", "Release", "GestureSign.exe"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private async Task<PickedWindowInfo?> PickWindowByClickAsync()
    {
        if (_mouseHook != IntPtr.Zero)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowPickCompletion = new TaskCompletionSource<PickedWindowInfo?>();
        _mouseHookProc = LowLevelMouseCallback;
        _mouseHook = SetWindowsHookEx(14, _mouseHookProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
        {
            _windowPickCompletion = null;
            _mouseHookProc = null;
            return null;
        }

        ShowWindow(hwnd, 6);
        var completed = await Task.WhenAny(_windowPickCompletion.Task, Task.Delay(TimeSpan.FromSeconds(12)));
        var result = completed == _windowPickCompletion.Task ? await _windowPickCompletion.Task : null;
        StopWindowPicking();
        ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
        return result;
    }

    private void StopWindowPicking()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        ClearPickOutline();
        _mouseHookProc = null;
        _windowPickCompletion = null;
    }

    private IntPtr LowLevelMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        const int wmMouseMove = 0x0200;
        const int wmLButtonDown = 0x0201;
        if (nCode >= 0 && _windowPickCompletion is not null)
        {
            var mouse = Marshal.PtrToStructure<MouseLlHookStruct>(lParam);
            if (wParam == wmMouseMove)
            {
                DrawPickOutline(mouse.Point);
            }
            else if (wParam == wmLButtonDown)
            {
                var info = TryPickWindowAtPoint(mouse.Point);
                _windowPickCompletion.TrySetResult(info);
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static PickedWindowInfo? TryPickWindowUnderCursor()
    {
        if (!GetCursorPos(out var point))
            return null;
        return TryPickWindowAtPoint(point);
    }

    private void DrawPickOutline(NativePoint point)
    {
        var hwnd = TopLevelWindowFromPoint(point);
        if (hwnd == IntPtr.Zero || !TryGetVisibleWindowRect(hwnd, out var rect))
        {
            ClearPickOutline();
            return;
        }

        if (_pickOutlineRect is RECT previous && previous.Equals(rect))
            return;

        ClearPickOutline();
        DrawPickOverlayRect(rect);
        _pickOutlineHwnd = IntPtr.Zero;
        _pickOutlineRect = rect;
    }

    private void ClearPickOutline()
    {
        if (_pickOutlineWindow != IntPtr.Zero)
        {
            DestroyWindow(_pickOutlineWindow);
            _pickOutlineWindow = IntPtr.Zero;
        }

        _pickOutlineHwnd = IntPtr.Zero;
        _pickOutlineRect = null;
    }

    private static void ApplyPickOutline(IntPtr hwnd)
    {
        const int dwmwaBorderColor = 34;
        var color = GetWindowsAccentColorRef();
        DwmSetWindowAttribute(hwnd, dwmwaBorderColor, ref color, sizeof(uint));
    }

    private static void ResetPickOutline(IntPtr hwnd)
    {
        const int dwmwaBorderColor = 34;
        const uint dwmwaColorDefault = 0xFFFFFFFF;
        var color = dwmwaColorDefault;
        DwmSetWindowAttribute(hwnd, dwmwaBorderColor, ref color, sizeof(uint));
    }

    private void DrawPickOverlayRect(RECT rect)
    {
        var thickness = PickOutlineThickness;
        var accent = GetWindowsAccentColorRef();
        var halfThickness = (int)Math.Ceiling(thickness / 2d);
        var outline = rect.Inflate(halfThickness);
        var width = Math.Max(1, outline.Right - outline.Left);
        var height = Math.Max(1, outline.Bottom - outline.Top);
        var hwnd = EnsurePickOutlineWindow();
        if (hwnd == IntPtr.Zero)
            return;

        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new System.Drawing.Pen(ToDrawingColor(accent), thickness)
            {
                Alignment = System.Drawing.Drawing2D.PenAlignment.Center,
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };

            var inset = thickness / 2f;
            var drawRect = new System.Drawing.RectangleF(
                inset,
                inset,
                Math.Max(1, width - thickness),
                Math.Max(1, height - thickness));
            using var path = RoundedRectanglePath(drawRect, PickOutlineCornerRadius);
            graphics.DrawPath(pen, path);
        }

        UpdatePickLayeredWindow(hwnd, bitmap, outline.Left, outline.Top);
    }

    private static void UpdatePickLayeredWindow(IntPtr hwnd, System.Drawing.Bitmap bitmap, int x, int y)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return;

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            return;
        }

        var hBitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, hBitmap);
        var destination = new NativePoint(x, y);
        var size = new NativeSize(bitmap.Width, bitmap.Height);
        var source = new NativePoint(0, 0);
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };
        const uint ulwAlpha = 0x00000002;
        UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ulwAlpha);
        ShowWindow(hwnd, 8);

        SelectObject(memoryDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memoryDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectanglePath(System.Drawing.RectangleF rect, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        var arc = new System.Drawing.RectangleF(rect.X, rect.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static System.Drawing.Color ToDrawingColor(uint colorRef)
    {
        var red = (int)(colorRef & 0xff);
        var green = (int)((colorRef >> 8) & 0xff);
        var blue = (int)((colorRef >> 16) & 0xff);
        return System.Drawing.Color.FromArgb(255, red, green, blue);
    }

    private IntPtr EnsurePickOutlineWindow()
    {
        if (_pickOutlineWindow != IntPtr.Zero)
            return _pickOutlineWindow;

        const uint wsPopup = 0x80000000;
        const uint wsDisabled = 0x08000000;
        const uint wsExLayered = 0x00080000;
        const uint wsExTransparent = 0x00000020;
        const uint wsExToolWindow = 0x00000080;
        const uint wsExTopmost = 0x00000008;
        const uint wsExNoActivate = 0x08000000;

        _pickOutlineWindow = CreateWindowEx(
            wsExLayered | wsExTransparent | wsExToolWindow | wsExTopmost | wsExNoActivate,
            "STATIC",
            null,
            wsPopup | wsDisabled,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        return _pickOutlineWindow;
    }

    private static bool TryGetVisibleWindowRect(IntPtr hwnd, out RECT rect)
    {
        const int dwmwaExtendedFrameBounds = 9;
        if (DwmGetWindowAttribute(hwnd, dwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<RECT>()) == 0
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top)
        {
            return true;
        }

        return GetWindowRect(hwnd, out rect);
    }

    private static void RedrawPickOutlineArea(RECT rect)
    {
        const uint rdwInvalidate = 0x0001;
        const uint rdwUpdateNow = 0x0100;
        const uint rdwAllChildren = 0x0080;
        var padding = PickOutlineThickness + PickOutlineCornerRadius;
        var area = rect.Inflate(padding);
        RedrawWindow(IntPtr.Zero, ref area, IntPtr.Zero, rdwInvalidate | rdwUpdateNow | rdwAllChildren);
    }

    private static uint GetWindowsAccentColorRef()
    {
        if (DwmGetColorizationColor(out var colorizationColor, out _) == 0)
        {
            var red = (colorizationColor >> 16) & 0xff;
            var green = (colorizationColor >> 8) & 0xff;
            var blue = colorizationColor & 0xff;
            if (red != 0 || green != 0 || blue != 0)
                return red | (green << 8) | (blue << 16);
        }

        var systemHighlight = GetSysColor(13);
        return systemHighlight != 0 ? systemHighlight : 0x00D47800;
    }

    private static uint[] WindowsDefaultColorRefs()
    {
        var accent = GetWindowsAccentColorRef();
        return
        [
            accent,
            ToColorRef(0, 188, 242),
            ToColorRef(123, 97, 255),
            ToColorRef(16, 124, 16)
        ];
    }

    private static uint ToColorRef(byte red, byte green, byte blue)
        => (uint)(red | (green << 8) | (blue << 16));

    private static IntPtr TopLevelWindowFromPoint(NativePoint point)
    {
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var root = GetAncestor(hwnd, 2);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private static PickedWindowInfo? TryPickWindowAtPoint(NativePoint point)
    {
        var hwnd = TopLevelWindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return null;

        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        var className = new StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        GetWindowThreadProcessId(hwnd, out var pid);
        var fileName = "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            fileName = Path.GetFileName(process.MainModule?.FileName ?? "");
        }
        catch
        {
            fileName = "";
        }

        return new PickedWindowInfo(title.ToString(), className.ToString(), fileName);
    }

    private string MatchUsingText(int matchUsing)
    {
        return matchUsing switch
        {
            1 => L("窗口标题", "Window Title", "視窗標題", "ウィンドウタイトル", "창 제목"),
            2 => L("可执行文件", "Executable", "可執行檔", "実行ファイル", "실행 파일"),
            0 => L("窗口类", "Window Class", "視窗類別", "ウィンドウクラス", "창 클래스"),
            3 => L("窗口类", "Window Class", "視窗類別", "ウィンドウクラス", "창 클래스"),
            4 => L("全局", "Global", "全域", "グローバル", "전역"),
            _ => L("窗口类", "Window Class", "視窗類別", "ウィンドウクラス", "창 클래스")
        };
    }

    private enum DaemonCommand : byte
    {
        StartTeaching = 1,
        StopTraining = 2,
        LoadApplications = 3,
        LoadGestures = 4,
        LoadConfiguration = 5,
        EnableRecognition = 9,
        DisableRecognition = 10,
        Exit = 11
    }

    private sealed record RunningProcessInfo(string Name, string FileName);
    private sealed record PickedWindowInfo(string Title, string ClassName, string FileName);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public readonly int Width;
        public readonly int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RECT : IEquatable<RECT>
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public RECT Inflate(int padding)
            => new(Left - padding, Top - padding, Right + padding, Bottom + padding);

        public bool Equals(RECT other)
            => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    }

    private sealed class TrainingPipeServer
    {
        private readonly Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> _onGesture;
        private System.Threading.CancellationTokenSource? _cancellation;

        public TrainingPipeServer(Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> onGesture)
        {
            _onGesture = onGesture;
        }

        public void Start()
        {
            Stop();
            _cancellation = new System.Threading.CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(_cancellation.Token));
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            _cancellation = null;
        }

        private async Task ListenAsync(System.Threading.CancellationToken cancellationToken)
        {
            var user = WindowsIdentity.GetCurrent().User?.ToString();
            if (string.IsNullOrWhiteSpace(user))
                return;

            var pipeName = $"GestureSignControlPanel-{user}";
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(cancellationToken);
                    var patterns = ReadTrainingGesture(server);
                    if (patterns.Count > 0)
                    {
                        _onGesture(patterns);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                }
            }
        }

        private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ReadTrainingGesture(Stream stream)
        {
#pragma warning disable SYSLIB0011
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            var command = memory.ReadByte();
            if (command != 6)
                return [];
            var formatter = new BinaryFormatter();
            var data = formatter.Deserialize(memory);
#pragma warning restore SYSLIB0011
            if (data is not System.Drawing.Point[][][] raw)
                return [];

            var newestPattern = raw.FirstOrDefault();
            if (newestPattern is null)
                return [];

            return newestPattern
                .Select(stroke => stroke.Select(point => ((double)point.X, (double)point.Y)).ToList())
                .Where(stroke => stroke.Count > 0)
                .ToList();
        }
    }
}
