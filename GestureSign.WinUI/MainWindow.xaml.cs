using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Devices.Input;
using Windows.Storage.Pickers;
using Windows.System;
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

    private LegacyDataStore _legacyData;
    private readonly TrainingPipeServer _trainingPipeServer;
    private readonly DispatcherTimer _optionSaveTimer = new();
    private readonly Dictionary<string, string> _pendingOptionUpdates = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _pendingTrainingStatus;
    private bool _isSavingOptions;
    private string _selectedActionScope = "all";
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private TextBox? _activeHotKeyRecorder;
    private TextBox? _activeHotKeySettings;
    private Action<string>? _activeHotKeyRecorded;
    private bool _activeHotKeyUsesArrayKeyCode = true;
    private readonly HashSet<int> _hotKeyRecordingPressedKeys = new();
    private bool _stopHotKeyRecordingWhenReleased;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseHookProc;
    private TaskCompletionSource<PickedWindowInfo?>? _windowPickCompletion;
    private RECT? _pickOutlineRect;
    private IntPtr _pickOutlineHwnd;
    private IntPtr _pickOutlineWindow;

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
        _trainingPipeServer = new TrainingPipeServer(points => DispatcherQueue.TryEnqueue(async () => await SaveTrainedGestureAsync(points)));
        _optionSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _optionSaveTimer.Tick += OptionSaveTimer_Tick;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        ApplyMicaDimmingOverlay();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindow();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ShowPage("actions");
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplyMicaDimmingOverlay();
            ConfigureCaptionButtons();
            ShowSelectedPage();
        };
        _ = EnsureDaemonRunningAsync();
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

    private void ConfigureWindow()
    {
        AppWindow.Resize(ScaleLogicalSize(DefaultWindowWidth, DefaultWindowHeight));
        AppWindow.SetIcon("Assets/logo.ico");
        CenterWindow();
        ConfigureCaptionButtons();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleLogicalLength(MinimumWindowWidth);
            presenter.PreferredMinimumHeight = ScaleLogicalLength(MinimumWindowHeight);
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
        PageHost.Children.Clear();

        switch (tag)
        {
            case "ignored":
                PageTitle.Text = "忽略";
                PageSubtitle.Text = "设置不参与手势识别的程序和匹配规则。";
                PageHost.Children.Add(BuildIgnoredPage());
                break;
            case "gestures":
                PageTitle.Text = "手势";
                PageSubtitle.Text = "查看、导入和整理可用手势。";
                PageHost.Children.Add(BuildGesturesPage());
                break;
            case "options":
                PageTitle.Text = "选项";
                PageSubtitle.Text = "调整识别方式、轨迹反馈、启动项和设备开关。";
                PageHost.Children.Add(BuildOptionsPage());
                break;
            case "about":
                PageTitle.Text = "关于";
                PageSubtitle.Text = "GestureSign 的版本、项目和维护信息。";
                PageHost.Children.Add(BuildAboutPage());
                break;
            default:
                PageTitle.Text = "动作";
                PageSubtitle.Text = "按程序管理手势动作。";
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
        appsPanel.Children.Add(NewCardHeader("程序", $"添加、编辑、删除或按分组管理匹配程序。数据源: {_legacyData.DataSource}", "添加程序"));
        appsPanel.Children.Add(NewListRow("全部动作", $"{userApps.Sum(app => app.Actions.Count)} 个动作", null, _selectedActionScope == "all", () =>
        {
            _selectedActionScope = "all";
            ShowSelectedPage();
        }));
        foreach (var group in userApps.GroupBy(app => string.IsNullOrWhiteSpace(app.Group) ? "(默认)" : app.Group))
        {
            var groupKey = $"group:{group.Key}";
            if (ShouldShowApplicationGroup(group.Key))
            {
                appsPanel.Children.Add(NewListRow($"{group.Key}  {group.Count()} 程序", $"{group.Sum(app => app.Actions.Count)} 个动作", null, _selectedActionScope == groupKey, () =>
                {
                    _selectedActionScope = groupKey;
                    ShowSelectedPage();
                }));
            }
            foreach (var app in group)
            {
                var appKey = ActionScopeKey(app);
                var buttons = NewInlineButtons(
                    ("编辑", async () => await EditApplicationAsync(app)),
                    (app.IsEnabled ? "停用" : "启用", async () => await ToggleEnabledAsync(app.Source)),
                    ("新动作", async () => await AddActionAsync(app)),
                    ("删除", async () => await DeleteApplicationAsync(app)));
                appsPanel.Children.Add(NewApplicationRow(app.Name, $"{MatchSummary(app)} · {app.Actions.Count} 个动作", buttons, _selectedActionScope == appKey, () =>
                {
                    _selectedActionScope = appKey;
                    ShowSelectedPage();
                }));
            }
        }

        var actionsPanel = NewCardPanel(12);
        var selectedApps = FilterApplicationsByScope(userApps).ToList();
        var allActions = selectedApps.SelectMany(app => app.Actions.Select(action => (Application: app, Action: action))).ToList();
        actionsPanel.Children.Add(NewCardHeader(ActionScopeTitle(userApps), $"当前范围 {selectedApps.Count} 个程序、{allActions.Count} 个动作", "新动作"));
        actionsPanel.Children.Add(NewSmallCommandBar(["导入", "导出", "备份", "恢复"]));
        foreach (var action in allActions.Take(12))
            actionsPanel.Children.Add(NewActionRow(action.Application, action.Action));
        if (allActions.Count > 12)
            actionsPanel.Children.Add(NewListRow("更多动作", $"还有 {allActions.Count - 12} 个动作将在虚拟化列表接入后显示。", null));

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
        root.Children.Add(NewCard(NewCardHeader("忽略列表", "按窗口标题、窗口类名或可执行文件匹配。", "添加忽略项")));

        var table = NewCardPanel(10);
        var ignoredApps = _legacyData.Applications.Where(app => app.Type == "忽略").ToList();
        table.Children.Add(NewTableHeader(["启用", "匹配类型", "程序名称", "匹配文本", "正则"]));
        foreach (var app in ignoredApps)
            table.Children.Add(NewTableRow([app.IsEnabled ? "开" : "关", MatchUsingText(app.MatchUsing), app.Name, app.MatchString, app.IsRegEx ? "是" : "否"], false, NewInlineButtons(("编辑", async () => await EditApplicationAsync(app)), (app.IsEnabled ? "停用" : "启用", async () => await ToggleEnabledAsync(app.Source)), ("删除", async () => await DeleteApplicationAsync(app)))));
        if (ignoredApps.Count == 0)
            table.Children.Add(NewTableRow(["-", "-", "暂无忽略项", "可以从这里添加窗口标题、类名或 exe 匹配", "-"]));
        table.Children.Add(NewSmallCommandBar(["导入", "导出", "下载列表"]));
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
            return $"{_selectedActionScope["group:".Length..]} 分组";

        if (_selectedActionScope.StartsWith("app:", StringComparison.Ordinal))
            return applications.FirstOrDefault(app => string.Equals(ActionScopeKey(app), _selectedActionScope, StringComparison.Ordinal))?.Name ?? "程序动作";

        return "全部动作";
    }

    private static string ActionScopeKey(LegacyApplication app)
        => $"app:{app.Name}|{app.MatchUsing}|{app.MatchString}";

    private static bool ShouldShowApplicationGroup(string groupName)
        => !string.Equals(groupName, "(默认)", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(groupName, "Internet", StringComparison.OrdinalIgnoreCase);

    private UIElement BuildGesturesPage()
    {
        var root = NewSection();
        var header = NewCardPanel(10);
        header.Children.Add(NewCardHeader("手势库", "支持大图标、绘制训练和详细信息视图。", "新建手势"));
        header.Children.Add(NewSmallCommandBar(["绘制手势", "后台训练手势", "导入手势文件", "导出手势文件"]));
        root.Children.Add(NewCard(header));

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var groupedGestures = _legacyData.Gestures
            .GroupBy(gesture => gesture.FingerCount)
            .OrderBy(group => group.Key)
            .ToList();

        var twoFinger = NewGestureGroup("1-2 指手势", groupedGestures.Where(group => group.Key <= 2).SelectMany(group => group).Take(12).ToArray());
        var threeFinger = NewGestureGroup("3 指手势", groupedGestures.Where(group => group.Key == 3).SelectMany(group => group).Take(12).ToArray());
        var custom = NewGestureGroup("更多手势", groupedGestures.Where(group => group.Key >= 4).SelectMany(group => group).Take(16).ToArray());

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

    private UIElement BuildOptionsPage()
    {
        var root = NewSection();
        var options = _legacyData.Options;
        root.Children.Add(NewSettingsGroup("视觉反馈",
        [
            NewToggleRow("显示手势轨迹", options.VisualFeedbackWidth > 0, "VisualFeedbackWidth", options.VisualFeedbackWidth == 0 ? "9" : options.VisualFeedbackWidth.ToString(), "0"),
            NewSliderRow("轨迹透明度", options.Opacity, 0.05, 1, 0.01, "Opacity", value => value.ToString("0.00", CultureInfo.InvariantCulture), value => $"{Math.Round(value * 100)}%"),
            NewSliderRow("轨迹宽度", options.VisualFeedbackWidth, 0, 30, 1, "VisualFeedbackWidth", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewSliderRow("最小点距离", options.MinimumPointDistance, 1, 100, 1, "MinimumPointDistance", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewVisualFeedbackColorRow(options.VisualFeedbackColor)
        ]));

        root.Children.Add(NewSettingsGroup("输入设备",
        [
            NewToggleRow("启用鼠标手势", NormalizeDrawingButton(options.DrawingButton) != 0, "DrawingButton", NormalizeDrawingButton(options.DrawingButton, 2097152).ToString(CultureInfo.InvariantCulture), "0"),
            NewToggleRow("Edge 优先使用自带鼠标手势", options.PreferEdgeMouseGestures, "PreferEdgeMouseGestures"),
            NewComboRow("绘制按钮", ["右键", "中键", "X1 键", "X2 键"], ["2097152", "4194304", "8388608", "16777216"], "DrawingButton", DrawingButtonIndex(NormalizeDrawingButton(options.DrawingButton, 2097152))),
            NewToggleRow("启用触摸屏手势", options.RegisterTouchScreen, "RegisterTouchScreen"),
            NewToggleRow("启用触控板手势", options.RegisterTouchPad, "RegisterTouchPad"),
            NewToggleRow("优先使用 Windows 触控板系统手势", options.PreferWindowsTouchPadGestures, "PreferWindowsTouchPadGestures"),
            NewToggleRow("启用触控笔手势", options.PenGestureButton != 0, "PenGestureButton", options.PenGestureButton == 0 ? "4" : options.PenGestureButton.ToString(CultureInfo.InvariantCulture), "0"),
            NewPenButtonRow(options.PenGestureButton)
        ]));

        root.Children.Add(NewSettingsGroup("系统",
        [
            NewComboRow("语言", ["跟随系统", "中文", "English"], ["", "zh", "en"], "CultureName", CultureIndex(options.CultureName)),
            NewToggleRow("启用初始超时", options.InitialTimeout > 0, "InitialTimeout", options.InitialTimeout == 0 ? "1000" : options.InitialTimeout.ToString(), "0"),
            NewSliderRow("初始超时", options.InitialTimeout / 1000d, 0, 2, 0.1, "InitialTimeout", value => ((int)Math.Round(value * 1000)).ToString(CultureInfo.InvariantCulture), value => $"{value:0.0} 秒"),
            NewStartupToggleRow(),
            NewAdminStartupToggleRow(options.RunAsAdmin),
            NewToggleRow("排除全屏游戏/应用", options.IgnoreFullScreen, "IgnoreFullScreen"),
            NewToggleRow("排除全屏播放视频（试验）", options.IgnoreFullScreenVideo, "IgnoreFullScreenVideo"),
            NewToggleRow("使用笔时忽略触摸输入", options.IgnoreTouchInputWhenUsingPen, "IgnoreTouchInputWhenUsingPen"),
            NewToggleRow("显示托盘图标", options.ShowTrayIcon, "ShowTrayIcon"),
            NewOpenSettingsHotKeyRow(options.OpenSettingsHotKey),
            NewToggleRow("错误日志提示", options.SendErrorReport, "SendErrorReport"),
            NewButtonRow("配置文件", ["备份", "恢复", "打开配置文件夹"])
        ]));

        return root;
    }

    private UIElement BuildAboutPage()
    {
        var root = NewSection();
        var content = NewCardPanel();
        content.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/logo.png")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(new TextBlock { Text = "GestureSign V2", Style = Application.Current.Resources["TitleTextBlockStyle"] as Style, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { Text = "WinUI 3 前端重构预览\n版本：8.1.9735", Opacity = 0.72, Margin = new Thickness(0, 4, 0, 0) });
        content.Children.Add(new TextBlock { Text = "作者: TransposonY\n发现问题或建议欢迎反馈: 553078206@qq.com\nQQ 交流群: 576981420", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        content.Children.Add(NewSmallCommandBar(["打开官网", "Windows 应用商店版", "发送反馈", "查看日志"]));
        root.Children.Add(NewCard(content));
        root.Children.Add(NewInfoCard("Project Page", "https://github.com/TransposonY/GestureSign", "Thanks: highsign, MahApps.Metro, WGestures."));
        return root;
    }

    private StackPanel NewSection() => new() { Spacing = 14 };

    private StackPanel NewCardPanel(double spacing = 6) => new() { Spacing = spacing };

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
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = "手势识别", Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
        text.Children.Add(new TextBlock { Text = "移动到动作页后，这里负责控制后台识别服务的启停。", Opacity = 0.68 });
        grid.Children.Add(text);

        var toggle = new TextBlock { Text = "开", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return NewCard(grid);
    }

    private FrameworkElement NewCardHeader(string title, string subtitle, string buttonText)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = NewCardPanel(4);
        text.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
        text.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);

        var button = NewPillButton(buttonText);
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
        var combo = new ComboBox { Width = 240, PlaceholderText = "选择运行中的程序" };
        foreach (var process in processes)
            combo.Items.Add($"{process.Name} ({process.FileName})");
        var apply = NewPillButton("使用", false);
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
    {
        var button = new Button
        {
            Content = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (attachDefaultHandler)
            button.Click += (_, _) => HandleCommand(text);
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
                    await AddActionAsync(_legacyData.Applications.FirstOrDefault(app => app.Type != "忽略"));
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
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(matchText);
        panel.Children.Add(NewRunningProcessPicker(name, matchText, matchUsing));
        panel.Children.Add(NewWindowPicker(name, matchText, matchUsing));
        if (!ignored)
            panel.Children.Add(group);
        panel.Children.Add(matchUsing);
        panel.Children.Add(regex);

        if (!await ConfirmDialogAsync(ignored ? "添加忽略项" : "添加程序", panel, "添加"))
            return;

        var matchUsingValue = matchUsing.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 3, _ => 2 };
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
        var matchUsing = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = app.MatchUsing switch { 1 => 0, 3 => 2, _ => 1 } };
        matchUsing.Items.Add("窗口标题");
        matchUsing.Items.Add("可执行文件");
        matchUsing.Items.Add("窗口类");
        var regex = new CheckBox { Content = "使用正则匹配", IsChecked = app.IsRegEx, Margin = new Thickness(0, 8, 0, 0) };
        var enabled = new CheckBox { Content = "启用", IsChecked = app.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var limitFingers = new TextBox { PlaceholderText = "限制手指数，0 表示不限", Text = app.LimitNumberOfFingers.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };
        var blockThreshold = new TextBox { PlaceholderText = "触摸阻断阈值", Text = app.BlockTouchInputThreshold.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };

        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(matchText);
        panel.Children.Add(NewRunningProcessPicker(name, matchText, matchUsing));
        panel.Children.Add(NewWindowPicker(name, matchText, matchUsing));
        if (app.Type != "忽略")
        {
            panel.Children.Add(group);
            panel.Children.Add(limitFingers);
            panel.Children.Add(blockThreshold);
        }
        panel.Children.Add(matchUsing);
        panel.Children.Add(regex);
        panel.Children.Add(enabled);

        if (!await ConfirmDialogAsync($"编辑 {app.Name}", panel, "保存"))
            return;

        var matchUsingValue = matchUsing.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 3, _ => 2 };
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

    private Task ToggleEnabledAsync(System.Text.Json.Nodes.JsonObject source)
    {
        var isEnabled = source.BoolValue("IsEnabled", true);
        _legacyData.SetEnabled(source, !isEnabled);
        return Task.CompletedTask;
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
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        if (!await ConfirmDialogAsync($"给 {app.Name} 添加动作", panel, "添加"))
            return;
        _legacyData.AddAction(app, name.Text, gesture.Text);
        ReloadData();
    }

    private async Task EditActionAsync(LegacyAction action)
    {
        var name = new TextBox { PlaceholderText = "动作名称", Text = DisplayName(action.Name) };
        var gesture = new TextBox { PlaceholderText = "手势名称", Text = action.GestureName, Margin = new Thickness(0, 8, 0, 0) };
        var condition = new TextBox { PlaceholderText = "触发条件，可留空", Text = action.Condition, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var enabled = new CheckBox { Content = "启用", IsChecked = action.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var activateWindow = new CheckBox { Content = "执行前激活目标窗口", IsChecked = action.ActivateWindow, Margin = new Thickness(0, 8, 0, 0) };
        activateWindow.HorizontalAlignment = HorizontalAlignment.Right;
        activateWindow.Margin = new Thickness(0, -32, 32, 0);
        var mouseHotkey = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = MouseActionIndex(action.MouseHotkey) };
        foreach (var item in new[] { "无鼠标快捷键", "滚轮前", "滚轮后", "左键", "右键", "中键", "X1 键", "X2 键" })
            mouseHotkey.Items.Add(item);
        var ignoredDevices = new TextBox { PlaceholderText = "忽略设备位掩码，0 表示全部设备可触发", Text = action.IgnoredDevices.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };
        var hotkeyJson = new TextBox { Text = action.HotkeyJson };
        var hotkeyRecorder = NewHotKeyRecorderWithClear(hotkeyJson, action.HotkeyJson, usesArrayKeyCode: false);
        var continuousGestureJson = new TextBox { PlaceholderText = "连续手势 JSON，可留空", Text = action.ContinuousGestureJson, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 64 };
        var drawnPoints = new List<(double X, double Y)>();
        var drawPanel = NewInlineGestureDrawingPanel(drawnPoints, out var showRecordedGesture);
        var trainingStatus = new TextBlock { Text = "触控板录制会使用后台识别服务捕捉真实多指轨迹。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trainByTouchpad = NewPillButton("用触控板录制", false);
        trainByTouchpad.HorizontalAlignment = HorizontalAlignment.Right;
        trainByTouchpad.Margin = new Thickness(0, -38, 32, 0);
        trainByTouchpad.Click += async (_, _) =>
        {
            var gestureName = string.IsNullOrWhiteSpace(gesture.Text) ? name.Text : gesture.Text;
            gesture.Text = gestureName;
            await StartGestureTrainingForNameAsync(gestureName, trainingStatus, showRecordedGesture);
        };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        panel.Children.Add(new TextBlock { Text = "手势图案", Opacity = 0.68, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(drawPanel);
        panel.Children.Add(trainByTouchpad);
        panel.Children.Add(trainingStatus);
        panel.Children.Add(enabled);
        panel.Children.Add(activateWindow);
        panel.Children.Add(mouseHotkey);
        panel.Children.Add(ignoredDevices);
        panel.Children.Add(hotkeyRecorder);
        // panel.Children.Add(continuousGestureJson);
        if (!await ConfirmDialogAsync($"编辑动作 {DisplayName(action.Name)}", panel, "保存"))
            return;

        if (drawnPoints.Count >= 2)
        {
            var gestureName = string.IsNullOrWhiteSpace(gesture.Text) ? name.Text : gesture.Text;
            var existingGesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, gestureName, StringComparison.OrdinalIgnoreCase));
            if (existingGesture is null)
                _legacyData.AddGestureFromPoints(gestureName, 1, drawnPoints);
            else
                _legacyData.UpdateGesturePoints(existingGesture, 1, drawnPoints);
            gesture.Text = gestureName;
        }

        _legacyData.UpdateAction(action, name.Text, gesture.Text, condition.Text, enabled.IsChecked ?? true, activateWindow.IsChecked ?? true, MouseActionValue(mouseHotkey.SelectedIndex), ParseInt(ignoredDevices.Text, action.IgnoredDevices), hotkeyJson.Text, continuousGestureJson.Text);
        ReloadData();
    }

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
        void UpdateEditor()
        {
            var pluginClassValue = PluginClassFromIndex(plugin.SelectedIndex);
            pluginClass.Text = pluginClassValue;
            if (!pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(settings.Text))
                settings.Text = PluginSettingsTemplate(pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, pluginClass, hotkey, settings);
        }
        plugin.SelectionChanged += (_, _) =>
        {
            var pluginClassValue = PluginClassFromIndex(plugin.SelectedIndex);
            pluginClass.Text = pluginClassValue;
            settings.Text = pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) ? "" : PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClassValue, pluginClass, hotkey, settings);
        };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
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
        plugin.SelectionChanged += (_, _) =>
        {
            var knownClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(knownClass))
                pluginClass.Text = knownClass;
            settings.Text = PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings);
        };
        var enabled = new CheckBox { Content = "启用", IsChecked = command.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(settings);
        panel.Children.Add(enabled);
        UpdateCommandEditorVisibility(command.PluginClass, pluginClass, hotkey, settings);
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
            PlaceholderText = "单击这里，然后直接按下快捷键",
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

    private static void UpdateCommandEditorVisibility(string pluginClass, TextBox pluginClassBox, FrameworkElement hotkeyBox, TextBox settingsBox)
    {
        var isHotKey = pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase);
        var isCustom = string.IsNullOrWhiteSpace(pluginClass);
        pluginClassBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        hotkeyBox.Visibility = isHotKey ? Visibility.Visible : Visibility.Collapsed;
        settingsBox.Visibility = Visibility.Collapsed;
    }

    private static bool IsSettingsFreePlugin(string pluginClass)
    {
        if (string.IsNullOrWhiteSpace(pluginClass))
            return false;

        return pluginClass.Contains("DefaultBrowser", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("PreviousApplication", StringComparison.OrdinalIgnoreCase)
            || pluginClass.Contains("NextApplication", StringComparison.OrdinalIgnoreCase);
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

    private FrameworkElement NewInlineGestureDrawingPanel(List<(double X, double Y)> sample, out Action<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> showRecordedGesture)
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
            canvas.Children.Clear();
            DrawGestureLines(canvas, strokes, canvas.Width, canvas.Height);
        };

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
            line = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
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

        var clear = NewPillButton("清除图案", false);
        clear.Click += (_, _) =>
        {
            sample.Clear();
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
        panel.Children.Add(clear);
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
        text.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
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

    private FrameworkElement NewApplicationRow(string title, string subtitle, FrameworkElement trailing, bool isSelected = false, Action? onClick = null)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style,
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
        text.Children.Add(new TextBlock { Text = DisplayName(action.Name), Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
        text.Children.Add(new TextBlock { Text = ActionSummary(application, action), Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        foreach (var command in action.Commands.Take(1))
        {
            var commandRow = NewListRow(DisplayName(command.Name), $"{PluginName(command.PluginClass)} · {(command.IsEnabled ? "启用" : "停用")}", null);
            text.Children.Add(commandRow);
        }
        if (action.Commands.Count == 0)
            text.Children.Add(NewListRow("未设置命令", "这个手势暂时不会执行任何操作", null));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = NewInlineButtons(("编辑", async () => await EditActionAsync(action)), (action.IsEnabled ? "停用" : "启用", async () => await ToggleEnabledAsync(action.Source)), ("设置命令", async () => await SetCommandAsync(action)), ("删除", async () => await DeleteActionAsync(application, action)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return NewCard(grid, new Thickness(12));
    }

    private FrameworkElement NewGesturePreview(string gestureName, double width, double height)
    {
        var gesture = _legacyData.Gestures.FirstOrDefault(item => string.Equals(item.Name, gestureName, StringComparison.OrdinalIgnoreCase));
        UIElement child = gesture is not null && gesture.PointPatterns.Count > 0
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

    private FrameworkElement NewGestureCanvas(LegacyGesture gesture, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height };
        DrawGestureLines(canvas, gesture.PointPatterns, width, height);
        return canvas;
    }

    private static void DrawGestureLines(Canvas canvas, IReadOnlyList<IReadOnlyList<(double X, double Y)>> pointPatterns, double width, double height)
    {
        var allPoints = pointPatterns.SelectMany(line => line).ToList();
        if (allPoints.Count == 0)
            return;

        var minX = allPoints.Min(point => point.X);
        var maxX = allPoints.Max(point => point.X);
        var minY = allPoints.Min(point => point.Y);
        var maxY = allPoints.Max(point => point.Y);
        var rangeX = Math.Max(1, maxX - minX);
        var rangeY = Math.Max(1, maxY - minY);
        var padding = 14d;
        var scale = Math.Min((width - padding * 2) / rangeX, (height - padding * 2) / rangeY);
        var drawingWidth = rangeX * scale;
        var drawingHeight = rangeY * scale;
        var offsetX = (width - drawingWidth) / 2;
        var offsetY = (height - drawingHeight) / 2;

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
                var point = NormalizePreviewPoint(line[0], minX, minY, scale, offsetX, offsetY);
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
                polyline.Points.Add(NormalizePreviewPoint(point, minX, minY, scale, offsetX, offsetY));
            canvas.Children.Add(polyline);
        }
    }

    private static Windows.Foundation.Point NormalizePreviewPoint((double X, double Y) point, double minX, double minY, double scale, double offsetX, double offsetY)
        => new(offsetX + (point.X - minX) * scale, offsetY + (point.Y - minY) * scale);

    private FrameworkElement NewDialogMapCard()
    {
        var content = NewCardPanel(10);
        content.Children.Add(new TextBlock { Text = "编辑入口", Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
        content.Children.Add(new TextBlock { Text = "旧版对话框已整理为 WinUI 重构目标：程序匹配、动作设置、命令选择、触发条件、导入导出。", Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(NewSmallCommandBar(["程序设置", "动作设置", "命令设置", "触发条件", "导入/导出"]));
        return NewCard(content);
    }

    private FrameworkElement NewInfoCard(string title, string subtitle, string detail)
    {
        var content = NewCardPanel();
        content.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });
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
        panel.Children.Add(new TextBlock { Text = $"{title}  {gestures.Length} 个", Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style });

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var gesture in gestures)
        {
            var content = NewCardPanel(6);
            content.Children.Add(NewGestureCanvas(gesture, 148, 74));
            content.Children.Add(new TextBlock
            {
                Text = gesture.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{gesture.FingerCount} 指",
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.68
            });
            content.Children.Add(NewInlineButtons(("重训", async () => await DrawGestureAsync(gesture)), ("改名", async () => await RenameGestureAsync(gesture)), ("删除", async () => await DeleteGestureAsync(gesture))));

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
            wrap.Children.Add(new TextBlock { Text = "暂无手势", Opacity = 0.68 });

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
        panel.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style, Margin = new Thickness(0, 0, 0, 12) });
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
        var toggle = new ToggleSwitch { IsOn = isOn, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(configKey))
        {
            toggle.Toggled += (_, _) =>
            {
                UpdateOptionAndReload(configKey, toggle.IsOn ? onValue ?? "True" : offValue ?? "False");
            };
        }
        return NewSettingRow(title, null, toggle);
    }

    private FrameworkElement NewStartupToggleRow()
    {
        var toggle = new ToggleSwitch { IsOn = IsStartupEnabled(), VerticalAlignment = VerticalAlignment.Center };
        toggle.Toggled += async (_, _) =>
        {
            try
            {
                SetStartupEnabled(toggle.IsOn);
            }
            catch (Exception ex)
            {
                toggle.IsOn = IsStartupEnabled();
                await ShowInfoDialog("启动项设置失败", ex.Message);
            }
        };
        return NewSettingRow("Windows 启动时运行", "登录后启动后台托盘和手势识别服务。", toggle);
    }

    private FrameworkElement NewAdminStartupToggleRow(bool isOn)
    {
        var toggle = new ToggleSwitch { IsOn = isOn, VerticalAlignment = VerticalAlignment.Center };
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
                await ShowInfoDialog("管理员启动设置失败", ex.Message);
            }
        };
        return NewSettingRow("以管理员身份启动", "创建或删除 StartGestureSign 计划任务，需要 UAC 确认。", toggle);
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

        var clear = NewPillButton("清除", false);
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
        return NewSettingRow("快捷键打开设置", "设置后可直接唤起 GestureSign 设置窗口。", panel);
    }

    private FrameworkElement NewVisualFeedbackColorRow(string colorValue)
    {
        var originalValue = colorValue?.Trim() ?? string.Empty;
        var committedValue = originalValue;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Right };
        var editPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var color = new TextBox { Width = 190, Text = originalValue, PlaceholderText = "颜色名、#RRGGBB 或 theme:*" };
        var preview = new Border
        {
            Width = 46,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            Background = BrushForVisualFeedbackValue(originalValue)
        };
        var undo = NewPillButton("撤销修改", false);
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

        var save = NewPillButton("保存颜色", false);
        save.Click += async (_, _) =>
        {
            var value = color.Text.Trim();
            if (!IsVisualFeedbackPreviewValueValid(value))
            {
                await ShowInfoDialog("颜色无效", "请输入颜色名、#RRGGBB、#AARRGGBB，或选择下面的预设颜色。");
                return;
            }

            UpdateOptionAndReloadNow("VisualFeedbackColor", value);
            committedValue = value;
            color.Text = value;
            ApplyPreview(value, false);
        };
        var system = NewPillButton("使用系统色", false);
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

        return NewSettingRow("轨迹颜色", "点击颜色后立即生效。", panel);
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
        var right = new CheckBox { Content = "右键", IsChecked = (penGestureButton & 4) != 0 };
        var eraser = new CheckBox { Content = "橡皮擦", IsChecked = (penGestureButton & 16) != 0 };
        var tip = new CheckBox { Content = "笔尖", IsChecked = (penGestureButton & 1) != 0 };
        var hover = new CheckBox { Content = "悬停", IsChecked = (penGestureButton & 2) != 0 };
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
        return NewSettingRow("触控笔按钮", null, panel);
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
                UpdateOptionAndReload(configKey, values[combo.SelectedIndex]);
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
        text.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["BodyTextBlockStyle"] as Style });
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

    private static string MatchSummary(LegacyApplication app)
    {
        if (app.Type == "全局")
            return "全局动作";

        var match = string.IsNullOrWhiteSpace(app.MatchString) ? "匹配项" : app.MatchString;
        return $"{MatchUsingText(app.MatchUsing)} · {match}";
    }

    private static string DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "未命名";

        var trimmed = value.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "save" => "保存",
            "copy" => "复制",
            "cut" => "剪切",
            "paste" => "粘贴",
            "undo" => "撤销",
            "redo" => "重做",
            "delete" => "删除",
            "select all" => "全选",
            "open web browser" => "打开网页浏览器",
            "open browser" => "打开浏览器",
            "open default browser" => "打开默认浏览器",
            "close" => "关闭",
            "close tab" => "关闭标签页",
            "new tab" => "新建标签页",
            "reopen closed tab" => "重新打开关闭的标签页",
            "previous tab" => "上一个标签页",
            "next tab" => "下一个标签页",
            "back" => "后退",
            "forward" => "前进",
            "refresh" => "刷新",
            "minimize" => "最小化",
            "maximize" => "最大化",
            "restore" => "还原",
            "maximize/restore" => "最大化/还原",
            "show desktop" => "显示桌面",
            "switch window" => "切换窗口",
            "previous application" => "上一个窗口",
            "next application" => "下一个窗口",
            "increase volume" => "增大音量",
            "volume up" => "增大音量",
            "decrease volume" => "减小音量",
            "volume down" => "减小音量",
            "mute" => "静音",
            "play/pause" => "播放/暂停",
            "play pause" => "播放/暂停",
            "previous track" => "上一曲",
            "next track" => "下一曲",
            "run command" => "运行命令",
            "open file" => "打开文件",
            "launch app" => "启动应用",
            "delay" => "延迟等待",
            "mouse action" => "鼠标动作",
            "mouse actions" => "鼠标动作",
            "screen brightness" => "屏幕亮度",
            "brightness up" => "提高亮度",
            "brightness down" => "降低亮度",
            "activate window" => "激活窗口",
            "touch keyboard" => "触摸键盘",
            "toggle window topmost" => "窗口置顶",
            "temporarily disable" => "临时禁用手势",
            "toggle disable gestures" => "切换禁用手势",
            "left" => "向左",
            "right" => "向右",
            "up" => "向上",
            "down" => "向下",
            _ => trimmed
        };
    }

    private static string ActionSummary(LegacyApplication app, LegacyAction action)
    {
        var commands = action.Commands.Count == 0
            ? "无命令"
            : string.Join("、", action.Commands.Take(2).Select(command => string.IsNullOrWhiteSpace(command.Name) ? PluginName(command.PluginClass) : DisplayName(command.Name)));

        if (action.Commands.Count > 2)
            commands += $" 等 {action.Commands.Count} 个命令";

        var scope = app.Type == "全局" ? "全局动作" : app.Name;
        return $"{scope} · {commands}";
    }

    private static string PluginName(string pluginClass)
    {
        if (string.IsNullOrWhiteSpace(pluginClass))
            return "插件命令";

        if (pluginClass.Contains("HotKey", StringComparison.OrdinalIgnoreCase))
            return "快捷键";
        if (pluginClass.Contains("DefaultBrowser", StringComparison.OrdinalIgnoreCase))
            return "打开默认浏览器";
        if (pluginClass.Contains("PreviousApplication", StringComparison.OrdinalIgnoreCase))
            return "上一窗口";
        if (pluginClass.Contains("NextApplication", StringComparison.OrdinalIgnoreCase))
            return "下一窗口";
        if (pluginClass.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            return "音量";
        if (pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
            return "运行命令";
        if (pluginClass.Contains("OpenFile", StringComparison.OrdinalIgnoreCase))
            return "打开文件";
        if (pluginClass.Contains("LaunchApp", StringComparison.OrdinalIgnoreCase))
            return "启动应用";
        if (pluginClass.Contains("Delay", StringComparison.OrdinalIgnoreCase))
            return "延迟等待";
        if (pluginClass.Contains("MouseActions", StringComparison.OrdinalIgnoreCase))
            return "鼠标动作";
        if (pluginClass.Contains("ScreenBrightness", StringComparison.OrdinalIgnoreCase))
            return "屏幕亮度";
        if (pluginClass.Contains("ActivateWindow", StringComparison.OrdinalIgnoreCase))
            return "激活窗口";
        if (pluginClass.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase))
            return "触摸键盘";
        if (pluginClass.Contains("MaximizeRestore", StringComparison.OrdinalIgnoreCase))
            return "最大化/还原";
        if (pluginClass.Contains("Minimize", StringComparison.OrdinalIgnoreCase))
            return "最小化";
        if (pluginClass.Contains("ToggleWindowTopmost", StringComparison.OrdinalIgnoreCase))
            return "窗口置顶";
        if (pluginClass.Contains("TemporarilyDisable", StringComparison.OrdinalIgnoreCase))
            return "临时禁用手势";
        if (pluginClass.Contains("ToggleDisableGestures", StringComparison.OrdinalIgnoreCase))
            return "切换禁用手势";

        var lastDot = pluginClass.LastIndexOf('.');
        return lastDot >= 0 && lastDot + 1 < pluginClass.Length ? pluginClass[(lastDot + 1)..] : pluginClass;
    }

    private static void AddPluginItems(ComboBox plugin)
    {
        plugin.Items.Add("快捷键");
        plugin.Items.Add("音量");
        plugin.Items.Add("运行命令");
        plugin.Items.Add("打开文件");
        plugin.Items.Add("启动应用");
        plugin.Items.Add("延迟等待");
        plugin.Items.Add("鼠标动作");
        plugin.Items.Add("调整亮度");
        plugin.Items.Add("激活窗口");
        plugin.Items.Add("触摸键盘");
        plugin.Items.Add("最大化/还原");
        plugin.Items.Add("最小化");
        plugin.Items.Add("窗口置顶");
        plugin.Items.Add("临时禁用");
        plugin.Items.Add("切换禁用");
        plugin.Items.Add("自定义插件");
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
            return "{\"Command\":\"notepad\",\"ShowCmd\":false}";
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
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = daemonPath,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
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

    private static string MatchUsingText(int matchUsing)
    {
        return matchUsing switch
        {
            1 => "窗口标题",
            2 => "可执行文件",
            3 => "窗口类",
            4 => "全局",
            _ => "窗口类"
        };
    }

    private enum DaemonCommand : byte
    {
        StartTeaching = 1,
        StopTraining = 2,
        LoadApplications = 3,
        LoadGestures = 4,
        LoadConfiguration = 5
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
