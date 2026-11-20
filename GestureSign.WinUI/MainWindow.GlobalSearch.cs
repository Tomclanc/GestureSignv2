using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GestureSign.PieMenu;
using GestureSign.Shared;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private const string ShowGlobalSearchOption = "ShowGlobalSearch";
    private const CompareOptions SearchCompareOptions =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    private sealed record AppSearchResult(string Title, string Category, string? Page, string Keywords = "",
        object? Anchor = null, string? Scope = null, string? MenuName = null)
    {
        public string DisplayText => Category.Length == 0 ? Title : $"{Title}  ·  {Category}";
    }

    private bool _showGlobalSearch;
    private AppSearchResult? _searchTarget;
    private InputNonClientPointerSource? _titleBarInput;
    private DateTime _quickMenuSearchStamp;
    private string[] _quickMenuSearchNames = [];

    private void InitializeGlobalSearch()
    {
        _showGlobalSearch = _legacyData.Options.ShowGlobalSearch;
        GlobalSearchBox.TextChanged += UpdateSearchSuggestions;
        GlobalSearchBox.QuerySubmitted += SubmitGlobalSearch;
        AppTitleBar.Loaded += (_, _) =>
        {
            _titleBarInput = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            if (AppTitleBar.XamlRoot is { } root)
                root.Changed += (_, _) => UpdateGlobalSearchLayout();
            UpdateGlobalSearchLayout();
        };
        AppTitleBar.SizeChanged += (_, _) => UpdateGlobalSearchLayout();
        GlobalSearchBox.SizeChanged += (_, _) => UpdateGlobalSearchInputRegion();
    }

    private string GlobalSearchToggleTitle => T("显示全局搜索框", "Show global search box");

    private void RefreshGlobalSearchText()
    {
        var label = T("搜索设置、动作或手势", "Search settings, actions, and gestures");
        GlobalSearchBox.PlaceholderText = label;
        AutomationProperties.SetName(GlobalSearchBox, label);
        GlobalSearchBox.FlowDirection = Root.FlowDirection;
        GlobalSearchBox.IsSuggestionListOpen = false;
    }

    private FrameworkElement NewGlobalSearchToggleRow()
    {
        var label = GlobalSearchToggleTitle;
        var toggle = new ToggleSwitch
        {
            IsOn = _showGlobalSearch,
            OnContent = L("开", "On", "開", "켬"),
            OffContent = L("关", "Off", "關", "끔")
        };
        AutomationProperties.SetName(toggle, label);
        toggle.Toggled += (_, _) =>
        {
            _showGlobalSearch = toggle.IsOn;
            if (!_showGlobalSearch)
            {
                GlobalSearchBox.Text = "";
                GlobalSearchBox.IsSuggestionListOpen = false;
            }
            UpdateGlobalSearchLayout();
            UpdateOptionAndReload(ShowGlobalSearchOption, toggle.IsOn ? "True" : "False");
        };
        return MarkSearchTarget(NewSettingRow(label, null, toggle), ShowGlobalSearchOption);
    }

    private void UpdateGlobalSearchLayout()
    {
        var scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1;
        var rightInset = AppWindow.TitleBar.RightInset / scale;
        var leftInset = AppWindow.TitleBar.LeftInset / scale;
        var width = AppTitleBar.ActualWidth;
        var showBrand = !_showGlobalSearch || width >= 640;
        AppTitleLabel.Visibility = showBrand ? Visibility.Visible : Visibility.Collapsed;
        var side = Math.Max(showBrand ? 182 : 44, Math.Max(rightInset, leftInset) + 12);
        var available = width - side * 2;
        var visible = _showGlobalSearch && available >= 96;
        AppTitleBar.Height = visible && _accessibility.LargeControls ? 64 : 48;
        GlobalSearchBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) GlobalSearchBox.Width = Math.Min(460, available);
        DispatcherQueue.TryEnqueue(UpdateGlobalSearchInputRegion);
    }

    private void UpdateGlobalSearchInputRegion()
    {
        if (_titleBarInput is null || GlobalSearchBox.XamlRoot is not { } root) return;
        if (GlobalSearchBox.Visibility != Visibility.Visible || GlobalSearchBox.ActualWidth <= 0)
        {
            _titleBarInput.ClearRegionRects(NonClientRegionKind.Passthrough);
            return;
        }

        var bounds = GlobalSearchBox.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, GlobalSearchBox.ActualWidth, GlobalSearchBox.ActualHeight));
        var scale = root.RasterizationScale;
        var left = (int)Math.Floor(bounds.Left * scale);
        var top = (int)Math.Floor(bounds.Top * scale);
        _titleBarInput.SetRegionRects(NonClientRegionKind.Passthrough,
        [
            new RectInt32(left, top, (int)Math.Ceiling(bounds.Right * scale) - left,
                (int)Math.Ceiling(bounds.Bottom * scale) - top)
        ]);
    }

    private void UpdateSearchSuggestions(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var query = sender.Text.Trim();
        if (query.Length == 0)
        {
            sender.ItemsSource = null;
            return;
        }
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var comparer = CultureInfo.GetCultureInfo(ResolveUiCultureName(_uiCultureName)).CompareInfo;
        var matches = AppSearchEntries()
            .Where(entry => terms.All(term => comparer.IndexOf(
                $"{entry.Title} {entry.Keywords} {entry.Category}", term, SearchCompareOptions) >= 0))
            .OrderBy(entry => comparer.Compare(entry.Title, query, SearchCompareOptions) == 0 ? 0
                : comparer.IsPrefix(entry.Title, query, SearchCompareOptions) ? 1
                : comparer.IndexOf(entry.Title, query, SearchCompareOptions) >= 0 ? 2 : 3)
            .Take(12)
            .ToArray();
        sender.ItemsSource = matches.Length > 0
            ? matches : new[] { new AppSearchResult(T("没有匹配的结果", "No matching results"), "", null) };
    }

    private void SubmitGlobalSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as AppSearchResult ??
            (sender.ItemsSource as AppSearchResult[])?.FirstOrDefault();
        if (result?.Page is not string page) return;
        var destination = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => Equals(item.Tag, page));
        if (destination is null) return;

        sender.IsSuggestionListOpen = false;
        sender.Text = "";
        sender.ItemsSource = null;
        if (result.Scope is not null) _selectedActionScope = result.Scope;
        _searchTarget = result.Anchor is not null || result.MenuName is not null ? result : null;
        var alreadySelected = ReferenceEquals(Navigation.SelectedItem, destination);
        Navigation.SelectedItem = destination;
        if (alreadySelected || _currentNavigationPage != page) ShowPage(page);
        MainContentScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        Navigation.Focus(FocusState.Programmatic);
    }

    private TElement MarkSearchTarget<TElement>(TElement element, object anchor) where TElement : FrameworkElement
    {
        if (_searchTarget is not { } result || !Equals(result.Anchor, anchor)) return element;
        element.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(result, _searchTarget) || result.Page != _currentNavigationPage) return;
            _searchTarget = null;
            if (element is Expander expander) expander.IsExpanded = true;
            element.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.15 });
        });
        return element;
    }

    private IEnumerable<AppSearchResult> AppSearchEntries()
    {
        var pages = Navigation.MenuItems.OfType<NavigationViewItem>()
            .Where(item => item.Tag is string && item.Content is string)
            .ToDictionary(item => (string)item.Tag, item => (string)item.Content);
        foreach (var (tag, title) in pages)
        {
            var keywords = _pageService?.Resolve(tag).ViewModel.Subtitle ?? "";
            if (tag == IntentLearningPageTag) keywords += " 本地意图学习 AI local intent learning";
            yield return new AppSearchResult(title, "", tag, keywords);
        }

        foreach (var (page, title, keywords, anchor) in SearchSettings())
            yield return new AppSearchResult(title, pages[page], page, keywords, anchor);

        foreach (var application in _legacyData.Applications)
        {
            var title = ApplicationDisplayName(application);
            if (application.Type == "忽略")
            {
                var rule = !string.IsNullOrWhiteSpace(application.IgnoredGesture)
                    ? BuiltInGestureDisplayName(application.IgnoredGesture)
                    : string.IsNullOrWhiteSpace(title) ? application.MatchString : title;
                if (!string.IsNullOrWhiteSpace(rule))
                    yield return new AppSearchResult(rule, pages["ignored"], "ignored", application.MatchString, application.Source);
                continue;
            }

            var scope = ActionScopeKey(application);
            if (!string.IsNullOrWhiteSpace(title))
                yield return new AppSearchResult(title, pages["actions"], "actions", $"{application.Name} {application.MatchString}", application.Source, scope);
            foreach (var action in application.Actions)
            {
                var commands = string.Join(' ', action.Commands.Select(command => $"{DisplayName(command.Name)} {command.Name}"));
                var gesture = string.IsNullOrWhiteSpace(action.GestureName) ? "" : BuiltInGestureDisplayName(action.GestureName);
                yield return new AppSearchResult(DisplayName(action.Name), title, "actions",
                    $"{action.Name} {gesture} {action.GestureName} {commands}", action.Source, scope);
            }
        }

        foreach (var gesture in _legacyData.Gestures)
            yield return new AppSearchResult(GestureDisplayName(gesture), pages["gestures"], "gestures", gesture.Name, gesture.Source);

        foreach (var menu in SearchableQuickMenus())
            yield return new AppSearchResult(menu, pages["quickActions"], "quickActions", MenuName: menu);
    }

    private IEnumerable<string> SearchableQuickMenus()
    {
        if (!KandoComponentPaths.IsNativePieMenu(_legacyData.Options.KandoExecutablePath)) return [];
        if (_pieDraft is not null && string.Equals(_pieDraftExecutable, _legacyData.Options.KandoExecutablePath, StringComparison.OrdinalIgnoreCase))
            return _pieDraft.Document.Menus.Select(menu => menu.Name);

        var file = Path.Combine(KandoComponentPaths.PieMenuConfigurationDirectory, "menus.json");
        try
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            if (stamp == _quickMenuSearchStamp) return _quickMenuSearchNames;
            _quickMenuSearchNames = File.Exists(file) ? MenuStorage.Read(file).Menus.Select(menu => menu.Name).ToArray() : [];
            _quickMenuSearchStamp = stamp;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _quickMenuSearchNames = [];
        }
        return _quickMenuSearchNames;
    }

    private IEnumerable<(string Page, string Title, string Keywords, string Anchor)> SearchSettings()
    {
        yield return ("actions", L("手势识别", "Gesture Recognition", "手勢辨識", "제스처 인식"), "识别 后台 recognition daemon", "gesture-recognition");
        yield return ("options", IntentText("启用鼠标手势", "Enable mouse gestures"), "鼠标 滑鼠 手势 mouse gestures", "mouse-gestures");
        yield return ("options", IntentText("鼠标手势启动键", "Mouse gesture start buttons"), "鼠标 左键 中键 右键 mouse drawing button", "mouse-gesture-buttons");
        yield return ("options", L("触摸屏屏蔽区（%）", "Touchscreen blocked areas (%)", "觸控螢幕封鎖區（%）", "터치스크린 차단 영역(%)"), "屏蔽区 触控屏 touch screen blocked area", "touchscreen-blocked-area");
        yield return (IntentLearningPageTag, IntentText("AI评分", "AI scoring"), "AI 否决 veto score train 本地意图学习", "intent-scoring");
        yield return ("options", L("显示手势轨迹", "Show gesture trail", "顯示手勢軌跡", "제스처 궤적 표시"), "显示手势轨迹 Show gesture trail", "VisualFeedbackWidth");
        yield return ("options", L("显示触发的手势操作", "Show triggered gesture action", "顯示觸發的手勢動作", "실행된 제스처 동작 표시"), "显示触发的手势操作 Show triggered gesture action", "ShowGestureActionHint");
        yield return ("options", L("轨迹透明度", "Trail opacity", "軌跡透明度", "궤적 투명도"), "轨迹透明度 Trail opacity", "Opacity");
        yield return ("options", L("轨迹宽度", "Trail width", "軌跡寬度", "궤적 너비"), "轨迹宽度 Trail width", "VisualFeedbackWidth-slider");
        yield return ("options", L("最小点距离", "Minimum point distance", "最小點距離", "최소 지점 거리"), "最小点距离 Minimum point distance", "MinimumPointDistance");
        yield return ("options", L("Edge 优先使用自带鼠标手势", "Prefer built-in Edge mouse gestures", "Edge 優先使用內建滑鼠手勢", "Edge 기본 마우스 제스처 우선 사용"), "Edge 优先使用自带鼠标手势 Prefer built-in Edge mouse gestures", "PreferEdgeMouseGestures");
        yield return ("options", L("启用触摸屏手势", "Enable touchscreen gestures", "啟用觸控螢幕手勢", "터치스크린 제스처 사용"), "启用触摸屏手势 Enable touchscreen gestures touch screen 触控屏", "RegisterTouchScreen");
        yield return ("options", L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "터치패드 제스처 사용"), "启用触控板手势 Enable touchpad gestures trackpad 触摸板", "RegisterTouchPad");
        yield return ("options", L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows 터치패드 제스처 우선 사용"), "优先使用 Windows 触控板系统手势 Prefer Windows touchpad gestures", "PreferWindowsTouchPadGestures");
        yield return ("options", L("启用触控笔手势", "Enable pen gestures", "啟用觸控筆手勢", "펜 제스처 사용"), "启用触控笔手势 Enable pen gestures", "PenGestureButton");
        yield return ("options", L("语言", "Language", "語言", "언어"), "语言 Language language locale 多语言", "CultureName");
        yield return ("options", L("启用初始超时", "Enable initial timeout", "啟用初始逾時", "초기 시간 제한 사용"), "启用初始超时 Enable initial timeout", "InitialTimeout");
        yield return ("options", L("初始超时", "Initial timeout", "初始逾時", "초기 시간 제한"), "初始超时 Initial timeout", "InitialTimeout-slider");
        yield return ("options", L("排除全屏游戏/应用", "Ignore fullscreen games/apps", "排除全螢幕遊戲/應用程式", "전체 화면 게임/앱 제외"), "排除全屏游戏/应用 Ignore fullscreen games/apps fullscreen game", "IgnoreFullScreen");
        yield return ("options", L("排除全屏播放视频（试验）", "Ignore fullscreen video playback (experimental)", "排除全螢幕影片播放（實驗）", "전체 화면 동영상 재생 제외(실험)"), "排除全屏播放视频（试验） Ignore fullscreen video playback (experimental)", "IgnoreFullScreenVideo");
        yield return ("options", L("使用笔时忽略触摸输入", "Ignore touch input while using pen", "使用筆時忽略觸控輸入", "펜 사용 중 터치 입력 무시"), "使用笔时忽略触摸输入 Ignore touch input while using pen", "IgnoreTouchInputWhenUsingPen");
        yield return ("options", L("显示托盘图标", "Show tray icon", "顯示系統匣圖示", "트레이 아이콘 표시"), "显示托盘图标 Show tray icon system tray notification area 托盘", "ShowTrayIcon");
        yield return ("options", L("错误日志提示", "Error log notifications", "錯誤記錄提示", "오류 로그 알림"), "错误日志提示 Error log notifications", "SendErrorReport");
        yield return ("touchpad", L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "터치패드 제스처 사용"), "启用触控板手势 Enable touchpad gestures trackpad 触摸板", "RegisterTouchPad");
        yield return ("touchpad", L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows 터치패드 시스템 제스처 우선 사용"), "优先使用 Windows 触控板系统手势 Prefer Windows touchpad gestures", "PreferWindowsTouchPadGestures");
        yield return ("options", GlobalSearchToggleTitle, "搜索 搜尋 search finder", "ShowGlobalSearch");
        yield return ("options", IntentText("无障碍：显示与操作", "Accessibility: display and interaction"), "无障碍 accessibility screen reader large controls", "accessibility");
        yield return ("options", L("Windows 启动时运行", "Run at Windows startup", "Windows 啟動時執行", "Windows 시작 시 실행"), "自启动 开机自启 开机启动 startup autostart", "startup");
        yield return ("options", L("以管理员身份启动", "Start as administrator", "以系統管理員身分啟動", "관리자 권한으로 시작"), "管理员 开机 UAC elevated", "admin-startup");
        yield return ("options", L("同步配置到 OneDrive", "Sync configuration to OneDrive", "同步設定到 OneDrive", "구성을 OneDrive에 동기화"), "OneDrive 同步 sync cloud", "onedrive");
        yield return ("options", L("快捷键打开设置", "Open settings hotkey", "快速鍵開啟設定", "설정 열기 단축키"), "打开设置 快捷键 hotkey shortcut", "open-settings");
        yield return ("options", L("轨迹颜色", "Trail color", "軌跡色彩", "궤적 색상"), "轨迹 颜色 colour color", "trail-color");
        yield return ("options", L("触控笔按钮", "Pen buttons", "觸控筆按鈕", "펜 버튼"), "笔 压力 pen stylus", "pen-buttons");
        yield return ("options", L("配置文件", "Configuration files", "設定檔", "구성 파일"), "设置 备份 恢复 配置 backup restore export config import", "configuration");
        if (!IsPackagedInstallation())
            yield return ("options", T("检查更新", "Check for updates"), "更新 update version", "updates");
        yield return ("touchpad", L("触控板边缘", "Touchpad Edges", "觸控板邊緣", "터치패드 가장자리"), "触控板 边缘 上滑 下滑 touchpad edge", "touchpad-map");
        yield return ("touchpad", L("触摸屏边缘", "Touchscreen Edges", "觸控螢幕邊緣", "터치스크린 가장자리"), "触摸屏 边缘 触控屏 touchscreen edge", "touchscreen-map");
        yield return ("touchpad", T("桌面边缘鼠标滚轮", "Desktop edge mouse wheel"), "桌面 鼠标 滚轮 滑鼠 mouse wheel desktop scroll", "desktop-wheel-map");
        yield return ("touchpad", "TipTap", "触控板 轻点 tap", "tiptap");
        yield return ("quickActions", T("环形菜单", "Pie menu"), "环形 圆环 快捷键 悬停 锚定 主题 配色 radial menu shortcut hover anchored theme", "quick-actions-component");
        yield return ("about", T("导出诊断包", "Export diagnostics"), "日志 诊断 导出 触控 触摸 输入 键盘 diagnostic log report", "export-diagnostics");
        yield return ("ignored", T("手势忽略", "Ignored gestures"), "忽略手势 ignored gestures block", "gesture-ignore");
    }
}
