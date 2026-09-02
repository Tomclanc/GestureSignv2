using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GestureSign.WinUI.Services;
using GestureSign.WinUI.ViewModels;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private UIElement BuildActionsPage()
    {
        var root = NewSection();
        root.Children.Add(NewRecognitionCard());
        foreach (var element in BuildActionsContent())
            root.Children.Add(element);
        return root;
    }

    private MainWindowPageService CreatePageService()
    {
        return new MainWindowPageService(new[]
        {
            RegisterPage("actions", L("动作", "Actions", "動作", "アクション", "동작"), L("按程序管理手势动作。", "Manage gesture actions by application.", "依程式管理手勢動作。", "アプリごとにジェスチャアクションを管理します。", "프로그램별로 제스처 동작을 관리합니다."), BuildActionsPage),
            RegisterPage("ignored", L("忽略", "Ignored", "忽略", "無視", "무시"), L("设置不参与手势识别的程序和匹配规则。", "Configure applications and matching rules excluded from gesture recognition.", "設定不參與手勢辨識的程式與比對規則。", "ジェスチャ認識から除外するアプリと一致ルールを設定します。", "제스처 인식에서 제외할 프로그램과 매칭 규칙을 설정합니다."), BuildIgnoredPageFromService),
            RegisterPage("gestures", L("手势", "Gestures", "手勢", "ジェスチャ", "제스처"), L("查看、导入和整理可用手势。", "View, import, and organize available gestures.", "檢視、匯入與整理可用手勢。", "利用可能なジェスチャを表示、インポート、整理します。", "사용 가능한 제스처를 보고 가져오고 정리합니다."), BuildGesturesPageFromService),
            RegisterPage("quickActions", L("快捷操作", "Quick Actions", "快捷操作", "クイック操作", "빠른 작업"), L("用独立快捷键唤起 Kando 圆环菜单。", "Open the Kando radial menu with dedicated shortcuts.", "使用獨立快速鍵叫出 Kando 環形選單。", "専用ショートカットで Kando ラジアルメニューを開きます。", "전용 단축키로 Kando 원형 메뉴를 엽니다."), BuildQuickActionsPageFromService),
            RegisterPage("touchpad", L("边缘交互", "Edge Interaction", "邊緣互動", "エッジ操作", "가장자리 상호작용"), L("设置触控板和触摸屏边缘点击、滑动动作。", "Configure touchpad and touchscreen edge taps and swipes.", "設定觸控板與觸控螢幕邊緣點擊、滑動動作。", "タッチパッドとタッチスクリーンのエッジタップ、スワイプ操作を設定します。", "터치패드와 터치스크린 가장자리 탭 및 스와이프 동작을 설정합니다."), BuildTouchPadPageFromService),
            RegisterPage("options", L("选项", "Options", "選項", "オプション", "옵션"), L("调整识别方式、轨迹反馈、启动项和设备开关。", "Adjust recognition, visual feedback, startup, and device switches.", "調整辨識方式、軌跡回饋、啟動項與裝置開關。", "認識方式、軌跡表示、スタートアップ、デバイス設定を調整します。", "인식 방식, 궤적 표시, 시작 항목 및 장치 스위치를 조정합니다."), BuildOptionsPageFromService),
            RegisterPage("about", L("关于", "About", "關於", "情報", "정보"), L("GestureSign 的版本、项目和维护信息。", "Version, project, and maintenance information for GestureSign.", "GestureSign 的版本、專案與維護資訊。", "GestureSign のバージョン、プロジェクト、メンテナンス情報。", "GestureSign의 버전, 프로젝트 및 유지 관리 정보입니다."), BuildAboutPage)
        }, "actions");
    }

    private static MainWindowPageRegistration RegisterPage(string tag, string title, string subtitle, Func<UIElement> build)
        => new(new MainWindowPageViewModel(tag, title, title, subtitle), build);

    private IEnumerable<UIElement> BuildActionsContent(
        IReadOnlyDictionary<string, double>? scrollOffsetsToRestore = null,
        double? mainScrollOffsetToRestore = null)
    {
        _actionsScopeRefreshTimer.Stop();
        _actionsPageActionsPanel = null;
        _actionsPageScopeRows.Clear();
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var appsPanel = NewCardPanel(12);
        var userApps = _legacyData.Applications.Where(app => app.Type != "忽略").ToList();
        appsPanel.Children.Add(NewCardHeader(L("程序", "Applications", "程式", "アプリ", "프로그램"), $"{L("添加、编辑、删除或按分组管理匹配程序。数据源:", "Add, edit, delete, or group matching applications. Source:", "新增、編輯、刪除或依群組管理比對程式。資料來源:", "一致するアプリを追加、編集、削除、グループ管理します。データ元:", "매칭 프로그램을 추가, 편집, 삭제하거나 그룹별로 관리합니다. 데이터 원본:")} {_legacyData.DataSource}", L("添加程序", "Add App", "新增程式", "アプリを追加", "프로그램 추가"), "添加程序"));
        AddActionScopeRow(appsPanel, "all", NewListRow(L("全部动作", "All Actions", "全部動作", "すべてのアクション", "모든 동작"), CountText(userApps.Sum(app => app.Actions.Count), L("个动作", "actions", "個動作", "個のアクション", "개 동작")), null, _selectedActionScope == "all", () => SelectActionScope("all")));
        foreach (var group in userApps.GroupBy(app => string.IsNullOrWhiteSpace(app.Group) ? "(默认)" : app.Group))
        {
            var groupKey = $"group:{group.Key}";
            if (ShouldShowApplicationGroup(group.Key))
            {
                AddActionScopeRow(appsPanel, groupKey, NewListRow($"{group.Key}  {CountText(group.Count(), L("程序", "apps", "程式", "アプリ", "개 프로그램"))}", CountText(group.Sum(app => app.Actions.Count), L("个动作", "actions", "個動作", "個のアクション", "개 동작")), null, _selectedActionScope == groupKey, () => SelectActionScope(groupKey)));
            }
            foreach (var app in group)
            {
                var appKey = ActionScopeKey(app);
                var buttons = NewInlineButtonsWithContext(
                    (L("编辑", "Edit", "編輯", "編集", "편집"), async _ => await EditApplicationAsync(app)),
                    (app.Source.BoolValue("IsEnabled", true) ? L("停用", "Disable", "停用", "無効化", "사용 안 함") : L("启用", "Enable", "啟用", "有効化", "사용"), async button => await ToggleApplicationEnabledAsync(app, button)),
                    (L("新动作", "New Action", "新增動作", "新規アクション", "새 동작"), async _ => await AddActionAsync(app)),
                    (L("删除", "Delete", "刪除", "削除", "삭제"), async _ => await DeleteApplicationAsync(app)));
                AddActionScopeRow(appsPanel, appKey, NewApplicationRow(ApplicationDisplayName(app.Name), $"{MatchSummary(app)} · {CountText(app.Actions.Count, L("个动作", "actions", "個動作", "個のアクション", "개 동작"))}", buttons, _selectedActionScope == appKey, () => SelectActionScope(appKey)));
            }
        }

        var actionsPanel = NewCardPanel(12);
        _actionsPageActionsPanel = actionsPanel;
        PopulateActionsScopePanel(actionsPanel, userApps, scrollOffsetsToRestore: scrollOffsetsToRestore, mainScrollOffsetToRestore: mainScrollOffsetToRestore);

        var appsCard = NewCard(NewActionsPageScrollViewer(appsPanel, hideScrollBar: true, name: ActionsPageAppsScrollViewerName), new Thickness(14));
        var actionsCard = NewCard(actionsPanel, new Thickness(14));
        Grid.SetColumn(actionsCard, 1);
        grid.Children.Add(appsCard);
        grid.Children.Add(actionsCard);
        ConfigureResponsiveTwoColumnGrid(grid, appsCard, actionsCard, 900, new GridLength(320));
        yield return grid;
    }

    private void AddActionScopeRow(StackPanel panel, string scopeKey, FrameworkElement row)
    {
        if (row is Border border)
            _actionsPageScopeRows[scopeKey] = border;

        panel.Children.Add(row);
    }

    private void SelectActionScope(string scopeKey)
    {
        if (string.Equals(_selectedActionScope, scopeKey, StringComparison.Ordinal))
            return;

        _selectedActionScope = scopeKey;
        UpdateActionScopeSelectionRows();
        ScheduleActionsScopePanelRefresh();
    }

    private void ScheduleActionsScopePanelRefresh()
    {
        _actionsScopeRefreshTimer.Stop();
        RefreshActionsScopePanel(preserveScroll: false);
    }

    private void UpdateActionScopeSelectionRows()
    {
        foreach (var (scopeKey, row) in _actionsPageScopeRows)
            row.Background = string.Equals(_selectedActionScope, scopeKey, StringComparison.Ordinal)
                ? SelectionBrush()
                : SubtleBrush();
    }

    private void RefreshActionsScopePanel(bool preserveScroll = true)
    {
        if (_actionsPageActionsPanel is null)
        {
            ShowSelectedPage();
            return;
        }

        var scrollOffsets = preserveScroll ? CaptureActionsPageScrollOffsets(PageHost) : null;
        var mainScrollOffset = preserveScroll ? MainContentScrollViewer.VerticalOffset : (double?)null;
        var userApps = _legacyData.Applications.Where(app => app.Type != "忽略").ToList();
        PopulateActionsScopePanel(_actionsPageActionsPanel, userApps, ++_actionsScopeRenderVersion, scrollOffsets, mainScrollOffset);
    }

    private void PopulateActionsScopePanel(
        StackPanel actionsPanel,
        IReadOnlyList<LegacyApplication> userApps,
        int renderVersion = 0,
        IReadOnlyDictionary<string, double>? scrollOffsetsToRestore = null,
        double? mainScrollOffsetToRestore = null)
    {
        if (renderVersion == 0)
            renderVersion = ++_actionsScopeRenderVersion;

        actionsPanel.Children.Clear();

        var selectedApps = FilterApplicationsByScope(userApps).ToList();
        var allActions = selectedApps.SelectMany(app => app.Actions.Select(action => (Application: app, Action: action))).ToList();
        actionsPanel.Children.Add(NewCardHeader(ActionScopeTitle(userApps), $"{L("当前范围", "Current scope", "目前範圍", "現在の範囲", "현재 범위")} {CountText(selectedApps.Count, L("个程序", "apps", "個程式", "個のアプリ", "개 프로그램"))}、{CountText(allActions.Count, L("个动作", "actions", "個動作", "個のアクション", "개 동작"))}", L("新动作", "New Action", "新增動作", "新規アクション", "새 동작"), "新动作"));
        // actionsPanel.Children.Add(NewSmallCommandBar([(L("导入", "Import", "匯入", "インポート", "가져오기"), "导入"), (L("导出", "Export", "匯出", "エクスポート", "내보내기"), "导出"), (L("备份", "Backup", "備份", "バックアップ", "백업"), "备份"), (L("恢复", "Restore", "還原", "復元", "복원"), "恢复")]));
        var actionList = NewCardPanel(12);
        actionsPanel.Children.Add(NewActionsPageScrollViewer(actionList, name: ActionsPageActionListScrollViewerName));
        PopulateActionRowsInBatches(actionList, allActions, renderVersion, 0, scrollOffsetsToRestore, mainScrollOffsetToRestore);
    }

    private void PopulateActionRowsInBatches(
        StackPanel actionList,
        IReadOnlyList<(LegacyApplication Application, LegacyAction Action)> actions,
        int renderVersion,
        int startIndex,
        IReadOnlyDictionary<string, double>? scrollOffsetsToRestore = null,
        double? mainScrollOffsetToRestore = null)
    {
        if (renderVersion != _actionsScopeRenderVersion)
            return;

        const int batchSize = 3;
        var endIndex = Math.Min(actions.Count, startIndex + batchSize);
        for (var index = startIndex; index < endIndex; index++)
            actionList.Children.Add(NewActionRow(actions[index].Application, actions[index].Action));

        if (endIndex >= actions.Count)
        {
            RestoreActionsPageScrollOffsetsAfterRender(renderVersion, scrollOffsetsToRestore, mainScrollOffsetToRestore);
            return;
        }

        DispatcherQueue.TryEnqueue(() => PopulateActionRowsInBatches(actionList, actions, renderVersion, endIndex, scrollOffsetsToRestore, mainScrollOffsetToRestore));
    }

    private void RestoreActionsPageScrollOffsetsAfterRender(
        int renderVersion,
        IReadOnlyDictionary<string, double>? scrollOffsetsToRestore,
        double? mainScrollOffsetToRestore)
    {
        if (scrollOffsetsToRestore is null && mainScrollOffsetToRestore is null)
            return;

        void Restore()
        {
            if (renderVersion != _actionsScopeRenderVersion)
                return;

            if (scrollOffsetsToRestore is not null)
                RestoreActionsPageScrollOffsets(PageHost, scrollOffsetsToRestore);
            if (mainScrollOffsetToRestore is not null)
                MainContentScrollViewer.ChangeView(null, mainScrollOffsetToRestore.Value, null, disableAnimation: true);
        }

        Restore();
        DispatcherQueue.TryEnqueue(Restore);
        var restoreAttempts = 0;
        var restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        restoreTimer.Tick += (_, _) =>
        {
            restoreAttempts++;
            Restore();
            if (restoreAttempts >= 8 || renderVersion != _actionsScopeRenderVersion)
                restoreTimer.Stop();
        };
        restoreTimer.Start();
    }

    private UIElement BuildIgnoredPageCore()
    {
        var root = NewSection();
        root.Children.Add(NewCard(NewCardHeader(L("忽略列表", "Ignored List", "忽略清單", "無視リスト", "무시 목록"), L("按窗口标题、窗口类名或可执行文件匹配。", "Match by window title, window class, or executable file.", "依視窗標題、視窗類別或可執行檔比對。", "ウィンドウタイトル、クラス名、実行ファイルで一致します。", "창 제목, 창 클래스 또는 실행 파일로 매칭합니다."), L("添加忽略项", "Add Ignored Item", "新增忽略項", "無視項目を追加", "무시 항목 추가"), "添加忽略项")));

        var table = NewCardPanel(10);
        var ignoredApps = _legacyData.Applications.Where(app => app.Type == "忽略").ToList();
        table.Children.Add(NewTableHeader([L("启用", "Enabled", "啟用", "有効", "사용"), L("匹配类型", "Match Type", "比對類型", "一致タイプ", "매칭 유형"), L("程序名称", "App Name", "程式名稱", "アプリ名", "프로그램 이름"), L("匹配文本", "Match Text", "比對文字", "一致テキスト", "매칭 텍스트"), L("正则", "Regex", "正則", "正規表現", "정규식")]));
        foreach (var app in ignoredApps)
            table.Children.Add(NewTableRow([app.IsEnabled ? L("开", "On", "開", "オン", "켬") : L("关", "Off", "關", "オフ", "끔"), MatchUsingText(app.MatchUsing), app.Name, app.MatchString, app.IsRegEx ? L("是", "Yes", "是", "はい", "예") : L("否", "No", "否", "いいえ", "아니요")], false, NewInlineButtonsWithContext(
                (L("编辑", "Edit", "編輯", "編集", "편집"), async _ => await EditApplicationAsync(app)),
                (app.IsEnabled ? L("停用", "Disable", "停用", "無効化", "사용 안 함") : L("启用", "Enable", "啟用", "有効化", "사용"), async button =>
                {
                    await ToggleApplicationEnabledAsync(app, button);
                    ReloadData();
                }),
                (L("删除", "Delete", "刪除", "削除", "삭제"), async _ => await DeleteApplicationAsync(app)))));
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

    private LegacyApplication? FindApplicationForAction(LegacyAction action)
        => _legacyData.Applications.FirstOrDefault(app =>
            app.Actions.Any(candidate => ReferenceEquals(candidate.Source, action.Source)));

    private static string ActionScopeKey(LegacyApplication app)
        => $"app:{app.Name}|{app.MatchUsing}|{app.MatchString}";

    private static bool ShouldShowApplicationGroup(string groupName)
        => !string.Equals(groupName, "(默认)", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(groupName, "Internet", StringComparison.OrdinalIgnoreCase);

    private UIElement BuildGesturesPageCore()
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
        ConfigureResponsiveGestureGrid(grid, twoFinger, threeFinger, custom, 760);
        root.Children.Add(grid);
        return root;
    }

}

