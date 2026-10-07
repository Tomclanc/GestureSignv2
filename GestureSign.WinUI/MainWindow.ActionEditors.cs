using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private async Task AddApplicationAsync(bool ignored)
    {
        var name = new TextBox { PlaceholderText = ignored ? T("忽略项名称", "Ignored application name") : T("程序名称", "App Name"), Text = ignored ? T("新忽略项", "New ignored application") : T("新程序", "New application") };
        var matchText = new TextBox { PlaceholderText = T("窗口标题、类名或 exe", "Window title, class name or executable"), Margin = new Thickness(0, 8, 0, 0) };
        var group = new TextBox { PlaceholderText = T("分组，可留空", "Group (optional)"), Margin = new Thickness(0, 8, 0, 0) };
        var matchUsing = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 1 };
        matchUsing.Items.Add(T("窗口标题", "Window Title"));
        matchUsing.Items.Add(T("可执行文件", "Executable"));
        matchUsing.Items.Add(T("窗口类", "Window Class"));
        var regex = new CheckBox { Content = T("使用正则匹配", "Use regular expressions"), Margin = new Thickness(0, 8, 0, 0) };
        var panel = NewCardPanel(12);
        panel.Children.Add(NewDialogField(T("程序名称", "App Name"), T("用于在动作页列表中显示，建议填写容易识别的名称。", "Displayed in the actions list. Choose a recognizable name."), name));
        panel.Children.Add(NewDialogField(T("匹配文本", "Match Text"), T("后台会用这里的文本匹配窗口。可执行文件示例: msedge.exe；多个程序可用 | 分隔。", "The service matches windows using this text. Executable example: msedge.exe. Separate multiple programs with |."), matchText));
        panel.Children.Add(NewDialogField(T("从运行中程序选择", "Choose a running application"), T("自动填入程序名称和可执行文件名，适合普通桌面程序。", "Fills in the application name and executable file name for desktop applications."), NewRunningProcessPicker(name, matchText, matchUsing)));
        panel.Children.Add(NewDialogField(T("拾取窗口", "Pick a window"), T("点击后在目标窗口上单击，可按 exe、标题或类名提取匹配信息。", "Click this button, then click the target window to capture its executable, title or class name."), NewWindowPicker(name, matchText, matchUsing)));
        if (!ignored)
            panel.Children.Add(NewDialogField(T("分组", "Group"), T("可留空。相同分组会在动作页中归在一起，方便管理。", "Optional. Applications in the same group appear together on the actions page."), group));
        panel.Children.Add(NewDialogField(T("匹配方式", "Match by"), T("可执行文件最稳定；窗口标题适合标题固定的窗口；窗口类适合系统窗口或特殊程序。", "Executable matching is most reliable. Use titles for windows with fixed titles, or classes for system windows and special applications."), matchUsing));
        panel.Children.Add(NewDialogField(T("正则匹配", "Regular expression matching"), T("开启后匹配文本会作为正则表达式处理，例如 chrome|firefox 可匹配多个浏览器。", "Treats the match text as a regular expression. For example, chrome|firefox matches multiple browsers."), regex));

        if (!await ConfirmDialogAsync(ignored ? T("添加忽略项", "Add Ignored Item") : T("添加程序", "Add App"), panel, T("添加", "Add")))
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
        var name = new TextBox { PlaceholderText = T("名称", "Name"), Text = app.Name };
        var matchText = new TextBox { PlaceholderText = T("窗口标题、类名或 exe", "Window title, class name or executable"), Text = app.MatchString, Margin = new Thickness(0, 8, 0, 0) };
        var group = new TextBox { PlaceholderText = T("分组，可留空", "Group (optional)"), Text = app.Group, Margin = new Thickness(0, 8, 0, 0) };
        var matchUsing = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = app.MatchUsing switch { 1 => 0, 0 => 2, 3 => 2, _ => 1 } };
        matchUsing.Items.Add(T("窗口标题", "Window Title"));
        matchUsing.Items.Add(T("可执行文件", "Executable"));
        matchUsing.Items.Add(T("窗口类", "Window Class"));
        var regex = new CheckBox { Content = T("使用正则匹配", "Use regular expressions"), IsChecked = app.IsRegEx, Margin = new Thickness(0, 8, 0, 0) };
        var enabled = new CheckBox { Content = T("启用", "Enable"), IsChecked = app.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var limitFingers = new TextBox { PlaceholderText = T("限制手指数，0 表示不限", "Maximum fingers (0 means unlimited)"), Text = app.LimitNumberOfFingers.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };
        var blockThreshold = new TextBox { PlaceholderText = T("触摸阻断阈值", "Touch blocking threshold"), Text = app.BlockTouchInputThreshold.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 8, 0, 0) };

        var panel = NewCardPanel(12);
        panel.Children.Add(NewDialogField(T("程序名称", "App Name"), T("用于在动作页列表中显示，建议填写容易识别的名称。", "Displayed in the actions list. Choose a recognizable name."), name));
        panel.Children.Add(NewDialogField(T("匹配文本", "Match Text"), T("后台会用这里的文本匹配窗口。可执行文件示例: msedge.exe；多个程序可用 | 分隔。", "The service matches windows using this text. Executable example: msedge.exe. Separate multiple programs with |."), matchText));
        panel.Children.Add(NewDialogField(T("从运行中程序选择", "Choose a running application"), T("自动填入程序名称和可执行文件名，适合普通桌面程序。", "Fills in the application name and executable file name for desktop applications."), NewRunningProcessPicker(name, matchText, matchUsing)));
        panel.Children.Add(NewDialogField(T("拾取窗口", "Pick a window"), T("点击后在目标窗口上单击，可按 exe、标题或类名提取匹配信息。", "Click this button, then click the target window to capture its executable, title or class name."), NewWindowPicker(name, matchText, matchUsing)));
        if (app.Type != "忽略")
        {
            panel.Children.Add(NewDialogField(T("分组", "Group"), T("可留空。相同分组会在动作页中归在一起，方便管理。", "Optional. Applications in the same group appear together on the actions page."), group));
            panel.Children.Add(NewDialogField(T("限制手指数", "Maximum fingers"), T("该程序允许识别的最大触点数。填 0 表示不限制；填 2 表示只响应 1 指和 2 指手势，忽略更多触点。", "Maximum contacts recognized for this application. Use 0 for unlimited, or 2 to recognize only one- and two-finger gestures."), limitFingers));
            panel.Children.Add(NewDialogField(T("触摸阻断阈值", "Touch blocking threshold"), T("触摸屏/触控板专用。开始手势后达到这个触点数时阻止原始触摸输入，避免页面同时滚动或点击；鼠标手势通常不受影响。", "For touchscreens and touchpads. Blocks native touch input once this contact count is reached during a gesture, preventing simultaneous scrolling or clicks. Mouse gestures are usually unaffected."), blockThreshold));
        }
        panel.Children.Add(NewDialogField(T("匹配方式", "Match by"), T("可执行文件最稳定；窗口标题适合标题固定的窗口；窗口类适合系统窗口或特殊程序。", "Executable matching is most reliable. Use titles for windows with fixed titles, or classes for system windows and special applications."), matchUsing));
        panel.Children.Add(NewDialogField(T("正则匹配", "Regular expression matching"), T("开启后匹配文本会作为正则表达式处理，例如 chrome|firefox 可匹配多个浏览器。", "Treats the match text as a regular expression. For example, chrome|firefox matches multiple browsers."), regex));
        panel.Children.Add(NewDialogField(T("启用状态", "Enabled"), T("关闭后该程序分组不会参与手势匹配，已有动作会保留。", "Disable gesture matching for this application group while keeping its actions."), enabled));

        if (!await ConfirmDialogAsync(F("编辑 {0}", "Edit {0}", app.Name), panel, T("保存", "Save")))
            return;

        var matchUsingValue = matchUsing.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 0, _ => 2 };
        _legacyData.UpdateApplication(app, name.Text, matchUsingValue, matchText.Text, group.Text, regex.IsChecked ?? false, enabled.IsChecked ?? true, ParseInt(limitFingers.Text, app.LimitNumberOfFingers), ParseInt(blockThreshold.Text, app.BlockTouchInputThreshold));
        ReloadData();
    }

    private async Task DeleteApplicationAsync(LegacyApplication app)
    {
        if (!await ConfirmDialogAsync(
                DeleteConfirmationTitle(),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L("确定删除 {0}？", "Delete {0}?", "確定刪除 {0}？", "{0} を削除しますか？", "{0} 항목을 삭제하시겠습니까?"),
                    app.Name),
                DeleteButtonText()))
            return;
        _legacyData.DeleteApplication(app);
        ReloadData();
    }

    private async Task<bool> ToggleEnabledAsync(System.Text.Json.Nodes.JsonObject source, Button? toggleButton = null)
    {
        var isEnabled = source.BoolValue("IsEnabled", true);
        var newEnabled = !isEnabled;
        _legacyData.SetEnabled(source, newEnabled);
        if (toggleButton != null)
        {
            toggleButton.Content = newEnabled
                ? L("停用", "Disable", "停用", "無効化", "사용 안 함")
                : L("启用", "Enable", "啟用", "有効化", "사용");
            toggleButton.UpdateLayout();
        }
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        return newEnabled;
    }

    private async Task ToggleApplicationEnabledAsync(LegacyApplication app, Button toggleButton)
    {
        var toggleKey = ActionScopeKey(app);
        if (!_pendingApplicationEnabledToggles.Add(toggleKey))
            return;

        toggleButton.IsEnabled = false;
        try
        {
            _legacyData = LegacyDataStore.Load();
            var currentApp = FindMatchingApplication(app) ?? app;
            var isEnabled = currentApp.Source.BoolValue("IsEnabled", true);
            var newEnabled = !isEnabled;
            _legacyData.SetEnabled(currentApp.Source, newEnabled);
            toggleButton.Content = newEnabled
                ? L("停用", "Disable", "停用", "無効化", "사용 안 함")
                : L("启用", "Enable", "啟用", "有効化", "사용");
            toggleButton.UpdateLayout();
            _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        }
        finally
        {
            toggleButton.IsEnabled = true;
            _pendingApplicationEnabledToggles.Remove(toggleKey);
        }

        await Task.CompletedTask;
    }

    private async Task AddActionAsync(LegacyApplication? app)
    {
        if (app is null)
        {
            await ShowInfoDialog(T("没有可用程序", "No applications available"), T("请先添加一个程序。", "Add an application first."));
            return;
        }

        var name = new TextBox { PlaceholderText = T("动作名称", "Action name"), Text = T("新动作", "New Action") };
        var gesture = new TextBox { PlaceholderText = T("手势名称，例如 3Right", "Gesture name, for example 3Right"), Margin = new Thickness(0, 8, 0, 0) };
        var deviceSelector = NewActionDeviceSelector(0);
        var drawnPointPatterns = new List<List<(double X, double Y)>>();
        var drawPanel = NewInlineGestureDrawingPanel(drawnPointPatterns, out var showRecordedGesture, out var clearGestureButton);
        var trainingStatus = new TextBlock { Text = T("可以直接绘制单指或多指图案，也可以用触控板录制真实轨迹。", "Draw a single- or multi-finger pattern, or record real strokes using the touchpad."), Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trainByTouchpad = NewPillButton(T("用触控板或触控录制", "Record with touchpad or touchscreen"), false);
        trainByTouchpad.Click += async (_, _) =>
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            SetGestureText(gesture, gestureName);
            await StartGestureTrainingForNameAsync(gestureName, trainingStatus, showRecordedGesture);
        };
        var commandName = new TextBox { PlaceholderText = T("命令名称", "Command name"), Text = T("发送快捷键", "Send Hotkey"), Margin = new Thickness(0, 8, 0, 0) };
        var commandPlugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 0 };
        AddPluginItems(commandPlugin);
        var commandPluginDescription = NewPluginDescriptionTextBlock();
        var commandPluginClass = new TextBox { PlaceholderText = T("自定义插件类名", "Custom plugin class name"), Text = PluginClassFromIndex(0), Margin = new Thickness(0, 8, 0, 0) };
        var commandSettings = new TextBox { PlaceholderText = T("命令设置 JSON，可留空", "Command settings JSON (optional)"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
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
            UpdatePluginDescription(commandPluginDescription, pluginClassValue);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, pluginClassValue, commandSettings.Text);
            RefreshCommandPreview();
        }
        commandPlugin.SelectionChanged += (_, _) =>
        {
            var pluginClassValue = PluginClassFromIndex(commandPlugin.SelectedIndex);
            commandPluginClass.Text = pluginClassValue;
            commandSettings.Text = pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) ? "" : PluginSettingsTemplate(pluginClassValue);
            UpdateDefaultCommandName(commandName, pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, commandPluginClass, commandHotkey, commandSettings, commandAppPicker);
            UpdatePluginDescription(commandPluginDescription, pluginClassValue);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, pluginClassValue, commandSettings.Text);
            RefreshCommandPreview();
        };
        commandName.TextChanged += (_, _) => RefreshCommandPreview();
        commandPluginClass.TextChanged += (_, _) =>
        {
            UpdateCommandEditorVisibility(commandPluginClass.Text, commandPluginClass, commandHotkey, commandSettings, commandAppPicker);
            UpdatePluginDescription(commandPluginDescription, commandPluginClass.Text);
            UpdateTypedCommandSettingsEditor(commandTypedSettings, commandPluginClass.Text, commandSettings.Text);
            RefreshCommandPreview();
        };
        commandSettings.TextChanged += (_, _) => RefreshCommandPreview();
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        panel.Children.Add(NewGesturePickerRow(
            gesture,
            selectedGesture =>
            {
                showRecordedGesture(selectedGesture.PointPatterns);
                drawnPointPatterns.Clear();
            },
            () =>
            {
                showRecordedGesture(Array.Empty<IReadOnlyList<(double X, double Y)>>());
                drawnPointPatterns.Clear();
            }));
        panel.Children.Add(new TextBlock { Text = T("手势图案", "Gesture pattern"), Opacity = 0.68, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(drawPanel);
        panel.Children.Add(NewGestureControlRow(clearGestureButton, trainByTouchpad));
        panel.Children.Add(trainingStatus);
        panel.Children.Add(deviceSelector.Content);
        panel.Children.Add(new TextBlock { Text = T("要执行的命令", "Command to execute"), Opacity = 0.68, Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(commandName);
        panel.Children.Add(commandPlugin);
        panel.Children.Add(commandPluginDescription);
        panel.Children.Add(commandPluginClass);
        panel.Children.Add(commandHotkey);
        panel.Children.Add(commandAppPicker);
        panel.Children.Add(commandTypedSettings);
        panel.Children.Add(commandSettings);
        panel.Children.Add(commandPreview);
        UpdateCommandEditor();
        var scrollOffsetsBeforeDialog = CaptureActionsPageScrollOffsets(PageHost);
        var mainScrollOffsetBeforeDialog = MainContentScrollViewer.VerticalOffset;
        if (!await ConfirmDialogAsync(F("给 {0} 添加动作", "Add an action to {0}", app.Name), panel, T("添加", "Add")))
            return;

        CommitSelectedAppCommandChoice(commandAppPicker, commandPlugin, commandPluginClass, commandSettings);

        var validDrawnPointPatterns = drawnPointPatterns
            .Where(pattern => pattern.Count >= 2)
            .Cast<IReadOnlyList<(double X, double Y)>>()
            .ToList();
        if (validDrawnPointPatterns.Count > 0)
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            gestureName = _legacyData.SaveGesturePointPatternsForAction(gestureName, null, validDrawnPointPatterns);
            SetGestureText(gesture, gestureName);
            _legacyData = LegacyDataStore.Load();
        }

        var finalGestureName = ResolveGestureName(gesture, "");
        if (string.IsNullOrWhiteSpace(finalGestureName))
        {
            await ShowInfoDialog(T("缺少手势", "No gesture selected"), T("请先选择、输入或绘制一个手势。", "Select, enter or draw a gesture first."));
            return;
        }
        SetGestureText(gesture, finalGestureName);

        var ignoredDevices = GetIgnoredActionDevices(deviceSelector);
        if (ignoredDevices == ActionDeviceAll)
        {
            await ShowInfoDialog(T("请选择触发设备", "Select an input device"), T("至少选择一种可以触发这个动作的输入设备。", "Select at least one input device to trigger this action."));
            return;
        }

        var targetApp = FindMatchingApplication(app);
        if (targetApp is null)
        {
            await ShowInfoDialog(T("程序分组已变化", "Application group changed"), T("刚才录制手势后配置已刷新，请重新打开该分组再添加动作。", "The configuration refreshed after recording. Reopen this group before adding the action."));
            ReloadData();
            return;
        }

        var actionName = name.Text;
        var gestureNameValue = finalGestureName;
        var commandPluginClassValue = commandPluginClass.Text.Trim();
        var commandSettingsValue = commandSettings.Text;
        var addInitialCommand = ShouldCreateCommand(commandPluginClassValue, commandSettingsValue);
        _legacyData.AddAction(targetApp, name.Text, finalGestureName, ignoredDevices);
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
        if (validDrawnPointPatterns.Count > 0)
            _ = NotifyDaemonAsync(DaemonCommand.LoadGestures);
        ReloadActionDataOnly(scrollOffsetsBeforeDialog, mainScrollOffsetBeforeDialog);
    }

    private async Task EditActionAsync(LegacyAction action)
    {
        var originalApp = FindApplicationForAction(action);
        var originalActionIndex = originalApp?.Actions.ToList().FindIndex(candidate => ReferenceEquals(candidate.Source, action.Source)) ?? -1;
        var name = new TextBox { PlaceholderText = T("动作名称", "Action name"), Text = DisplayName(action.Name) };
        var gesture = new TextBox { PlaceholderText = T("手势名称", "Gesture name"), Margin = new Thickness(0, 8, 0, 0) };
        SetGestureText(gesture, action.GestureName);
        var condition = new TextBox { PlaceholderText = T("触发条件，可留空", "Trigger condition (optional)"), Text = action.Condition, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var enabled = new CheckBox { Content = T("启用", "Enable"), IsChecked = action.IsEnabled };
        var activateWindow = new CheckBox { Content = T("执行前激活目标窗口", "Activate the target window before execution"), IsChecked = action.ActivateWindow };
        var mouseHotkey = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = MouseActionIndex(action.MouseHotkey) };
        foreach (var item in new[] { T("无鼠标快捷键", "No mouse shortcut"), T("滚轮前", "Wheel up"), T("滚轮后", "Wheel down"), T("左键", "Left"), T("右键", "Right"), T("中键", "Middle"), T("X1 键", "X1 button"), T("X2 键", "X2 button") })
            mouseHotkey.Items.Add(item);
        var deviceSelector = NewActionDeviceSelector(action.IgnoredDevices);
        var hotkeyJson = new TextBox { Text = action.HotkeyJson };
        var hotkeyRecorder = NewHotKeyRecorderWithClear(hotkeyJson, action.HotkeyJson, usesArrayKeyCode: false);
        var continuousGestureJson = new TextBox { PlaceholderText = T("连续手势 JSON，可留空", "Continuous gesture JSON (optional)"), Text = action.ContinuousGestureJson, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 64 };
        var drawnPointPatterns = new List<List<(double X, double Y)>>();
        var drawPanel = NewInlineGestureDrawingPanel(drawnPointPatterns, out var showRecordedGesture, out var clearGestureButton);
        var trainingStatus = new TextBlock { Text = T("触控板或触控录制会使用后台识别服务捕捉真实多指轨迹。", "Touchpad and touchscreen recording use the background recognition service to capture real multi-finger strokes."), Opacity = 0.68, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trainByTouchpad = NewPillButton(T("用触控板或触控录制", "Record with touchpad or touchscreen"), false);
        trainByTouchpad.Click += async (_, _) =>
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            SetGestureText(gesture, gestureName);
            await StartGestureTrainingForNameAsync(gestureName, trainingStatus, showRecordedGesture);
        };
        var panel = NewCardPanel(0);
        panel.Children.Add(name);
        panel.Children.Add(gesture);
        panel.Children.Add(NewGesturePickerRow(
            gesture,
            selectedGesture =>
            {
                showRecordedGesture(selectedGesture.PointPatterns);
                drawnPointPatterns.Clear();
            },
            () =>
            {
                showRecordedGesture(Array.Empty<IReadOnlyList<(double X, double Y)>>());
                drawnPointPatterns.Clear();
            }));
        panel.Children.Add(new TextBlock { Text = T("手势图案", "Gesture pattern"), Opacity = 0.68, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(drawPanel);
        panel.Children.Add(NewGestureControlRow(clearGestureButton, trainByTouchpad));
        panel.Children.Add(trainingStatus);
        panel.Children.Add(NewTwoColumnRow(enabled, activateWindow));
        panel.Children.Add(mouseHotkey);
        panel.Children.Add(deviceSelector.Content);
        panel.Children.Add(hotkeyRecorder);
        // panel.Children.Add(continuousGestureJson);
        var scrollOffsetsBeforeDialog = CaptureActionsPageScrollOffsets(PageHost);
        var mainScrollOffsetBeforeDialog = MainContentScrollViewer.VerticalOffset;
        if (!await ConfirmDialogAsync(F("编辑动作 {0}", "Edit action {0}", DisplayName(action.Name)), panel, T("保存", "Save")))
            return;

        var validDrawnPointPatterns = drawnPointPatterns
            .Where(pattern => pattern.Count >= 2)
            .Cast<IReadOnlyList<(double X, double Y)>>()
            .ToList();
        if (validDrawnPointPatterns.Count > 0)
        {
            var gestureName = ResolveGestureName(gesture, name.Text);
            gestureName = _legacyData.SaveGesturePointPatternsForAction(gestureName, action, validDrawnPointPatterns);
            SetGestureText(gesture, gestureName);
            _legacyData = LegacyDataStore.Load();
            if (originalApp is not null)
            {
                var currentApp = FindMatchingApplication(originalApp);
                var currentAction = originalActionIndex >= 0
                    ? currentApp?.Actions.ElementAtOrDefault(originalActionIndex)
                    : null;
                if (currentAction is null)
                {
                    await ShowInfoDialog(T("动作已变化", "Action changed"), T("保存手势图案后动作列表已刷新，但没有找到正在编辑的动作。请重新打开这个动作再保存。", "The action list refreshed after saving the pattern, but this action could not be found. Reopen it before saving."));
                    ReloadActionDataOnly(scrollOffsetsBeforeDialog, mainScrollOffsetBeforeDialog);
                    return;
                }

                action = currentAction;
            }
        }

        var ignoredDevices = GetIgnoredActionDevices(deviceSelector);
        if (ignoredDevices == ActionDeviceAll)
        {
            await ShowInfoDialog(T("请选择触发设备", "Select an input device"), T("至少选择一种可以触发这个动作的输入设备。", "Select at least one input device to trigger this action."));
            return;
        }

        _legacyData.UpdateAction(action, name.Text, ResolveGestureName(gesture, name.Text), condition.Text, enabled.IsChecked ?? true, activateWindow.IsChecked ?? true, MouseActionValue(mouseHotkey.SelectedIndex), ignoredDevices, hotkeyJson.Text, continuousGestureJson.Text);
        _ = NotifyDaemonAsync(DaemonCommand.LoadGestures);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadActionDataOnly(scrollOffsetsBeforeDialog, mainScrollOffsetBeforeDialog);
    }

    private FrameworkElement NewGesturePickerRow(
        TextBox gesture,
        Action<LegacyGesture>? onRecordedGestureSelected = null,
        Action? onBuiltInGestureSelected = null)
    {
        ComboBox? recordedPicker = null;
        ComboBox? tipTapPicker = null;
        var builtInPicker = NewBuiltInGesturePicker(gesture, () =>
        {
            if (recordedPicker is not null && recordedPicker.SelectedIndex > 0)
                recordedPicker.SelectedIndex = 0;
            if (tipTapPicker is not null)
            {
                var index = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text));
                tipTapPicker.SelectedIndex = index >= 25 ? index - 24 : 0;
            }
            onBuiltInGestureSelected?.Invoke();
        });
        recordedPicker = NewRecordedGesturePicker(gesture, selectedGesture =>
        {
            if (builtInPicker.SelectedIndex > 0)
                builtInPicker.SelectedIndex = 0;
            if (tipTapPicker is not null)
                tipTapPicker.SelectedIndex = 0;
            onRecordedGestureSelected?.Invoke(selectedGesture);
        });

        builtInPicker.Margin = new Thickness(0);
        recordedPicker.Margin = new Thickness(0);
        builtInPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        recordedPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        tipTapPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        tipTapPicker.Items.Add(L("选择 TipTap 触发方式", "Choose a TipTap trigger", "選擇 TipTap 觸發方式", "TipTap トリガーを選択", "TipTap 트리거 선택"));
        for (var index = 25; index <= 36; index++)
            tipTapPicker.Items.Add(BuiltInGestureDisplayNameFromIndex(index));
        var selectedIndex = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text));
        tipTapPicker.SelectedIndex = selectedIndex >= 25 ? selectedIndex - 24 : 0;
        tipTapPicker.SelectionChanged += (_, _) =>
        {
            if (tipTapPicker.SelectedIndex <= 0) return;
            // Reuse the built-in selection path for stored gesture IDs and
            // clearing recorded patterns, including when editing an existing action.
            builtInPicker.SelectedIndex = tipTapPicker.SelectedIndex + 24;
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(NewGestureControlRow(builtInPicker, recordedPicker));
        panel.Children.Add(tipTapPicker);
        var tipTapPreview = new Border { HorizontalAlignment = HorizontalAlignment.Center };
        void RefreshTipTapPreview()
        {
            tipTapPreview.Child = NewTipTapGesturePreview(ResolveGestureName(gesture, gesture.Text), 240, 122);
            tipTapPreview.Visibility = tipTapPreview.Child is null ? Visibility.Collapsed : Visibility.Visible;
        }
        gesture.TextChanged += (_, _) => RefreshTipTapPreview();
        RefreshTipTapPreview();
        panel.Children.Add(tipTapPreview);
        panel.Children.Add(new TextBlock
        {
            Text = L("TipTap：在触控板上按住 1～3 指，用另一指在手指组左、右、上或下方轻点，无需录制图案。",
                "TipTap: hold 1–3 fingers on the touchpad and tap with another finger to the left, right, above or below the held group. No drawing is needed.",
                "TipTap：在觸控板上按住 1～3 指，用另一指在手指組左、右、上或下方輕點，無需錄製圖案。",
                "TipTap：タッチパッドに 1～3 本の指を置いたまま、別の指でそのグループの左・右・上・下をタップします。図形の記録は不要です。",
                "TipTap: 터치패드에 손가락 1~3개를 댄 채 다른 손가락으로 그룹의 왼쪽, 오른쪽, 위 또는 아래를 탭하세요. 패턴을 그릴 필요가 없습니다."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });
        return panel;
    }

    private ActionDeviceSelector NewActionDeviceSelector(int ignoredDevices)
    {
        var touchScreen = new CheckBox
        {
            Content = L("触摸屏", "Touchscreen", "觸控螢幕", "タッチスクリーン", "터치스크린"),
            IsChecked = (ignoredDevices & ActionDeviceTouchScreen) == 0
        };
        var touchPad = new CheckBox
        {
            Content = L("触控板", "Touchpad", "觸控板", "タッチパッド", "터치패드"),
            IsChecked = (ignoredDevices & ActionDeviceTouchPad) == 0
        };
        var mouse = new CheckBox
        {
            Content = L("鼠标", "Mouse", "滑鼠", "マウス", "마우스"),
            IsChecked = (ignoredDevices & ActionDeviceMouse) == 0
        };
        var pen = new CheckBox
        {
            Content = L("触控笔", "Pen", "觸控筆", "ペン", "펜"),
            IsChecked = (ignoredDevices & ActionDevicePen) == 0
        };

        var choices = NewCardPanel(0);
        choices.Children.Add(NewTwoColumnRow(touchScreen, touchPad, 360));
        choices.Children.Add(NewTwoColumnRow(mouse, pen, 360));
        var content = NewDialogField(
            L("触发设备", "Trigger devices", "觸發裝置", "トリガーデバイス", "트리거 장치"),
            L("选择可以执行这个动作的输入设备。", "Select the input devices that can run this action.", "選擇可以執行這個動作的輸入裝置。", "このアクションを実行できる入力デバイスを選択します。", "이 동작을 실행할 입력 장치를 선택합니다."),
            choices);
        content.Margin = new Thickness(0, 8, 0, 0);
        return new ActionDeviceSelector(content, touchScreen, touchPad, mouse, pen);
    }

    private static int GetIgnoredActionDevices(ActionDeviceSelector selector)
    {
        var allowedDevices = (selector.TouchScreen.IsChecked == true ? ActionDeviceTouchScreen : 0)
            | (selector.TouchPad.IsChecked == true ? ActionDeviceTouchPad : 0)
            | (selector.Mouse.IsChecked == true ? ActionDeviceMouse : 0)
            | (selector.Pen.IsChecked == true ? ActionDevicePen : 0);
        return ActionDeviceAll & ~allowedDevices;
    }

    private ComboBox NewRecordedGesturePicker(TextBox gesture, Action<LegacyGesture>? onGestureSelected = null)
    {
        var gestures = _legacyData.Gestures
            .OrderBy(item => item.FingerCount)
            .ThenBy(item => DisplayName(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        combo.Items.Add(L("选择已录制手势", "Choose recorded gesture", "選擇已錄製手勢", "記録済みジェスチャを選択", "녹화된 제스처 선택"));
        foreach (var item in gestures)
        {
            combo.Items.Add($"{DisplayName(item.Name)} · {CountText(item.FingerCount, L("指", "finger(s)", "指", "本指", "손가락"))}");
        }

        var currentGestureName = ResolveGestureName(gesture, gesture.Text);
        var selectedGestureIndex = Array.FindIndex(gestures, item =>
            string.Equals(item.Name, currentGestureName, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = selectedGestureIndex >= 0 ? selectedGestureIndex + 1 : 0;
        combo.SelectionChanged += (_, _) =>
        {
            var index = combo.SelectedIndex - 1;
            if (index < 0 || index >= gestures.Length)
                return;

            var selectedGesture = gestures[index];
            SetGestureText(gesture, selectedGesture.Name);
            onGestureSelected?.Invoke(selectedGesture);
        };
        return combo;
    }

    private ComboBox NewBuiltInGesturePicker(TextBox gesture, Action? onGestureSelected = null)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text)) };
        for (var index = 0; index <= 36; index++)
            combo.Items.Add(BuiltInGestureDisplayNameFromIndex(index));
        combo.SelectedIndex = BuiltInGestureIndex(ResolveGestureName(gesture, gesture.Text));

        combo.SelectionChanged += (_, _) =>
        {
            var gestureName = BuiltInGestureNameFromIndex(combo.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(gestureName))
            {
                SetGestureText(gesture, gestureName);
                onGestureSelected?.Invoke();
            }
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

        for (var index = 1; index <= 36; index++)
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
            25 => L("按住 1 指 · 左侧 TipTap", "Hold 1 · left TipTap", "按住 1 指 · 左侧 TipTap", "1 本保持 · left TipTap", "1개 유지 · left TipTap"),
            26 => L("按住 1 指 · 右侧 TipTap", "Hold 1 · right TipTap", "按住 1 指 · 右侧 TipTap", "1 本保持 · right TipTap", "1개 유지 · right TipTap"),
            27 => L("按住 1 指 · 上方 TipTap", "Hold 1 · up TipTap", "按住 1 指 · 上方 TipTap", "1 本保持 · up TipTap", "1개 유지 · up TipTap"),
            28 => L("按住 1 指 · 下方 TipTap", "Hold 1 · down TipTap", "按住 1 指 · 下方 TipTap", "1 本保持 · down TipTap", "1개 유지 · down TipTap"),
            29 => L("按住 2 指 · 左侧 TipTap", "Hold 2 · left TipTap", "按住 2 指 · 左侧 TipTap", "2 本保持 · left TipTap", "2개 유지 · left TipTap"),
            30 => L("按住 2 指 · 右侧 TipTap", "Hold 2 · right TipTap", "按住 2 指 · 右侧 TipTap", "2 本保持 · right TipTap", "2개 유지 · right TipTap"),
            31 => L("按住 2 指 · 上方 TipTap", "Hold 2 · up TipTap", "按住 2 指 · 上方 TipTap", "2 本保持 · up TipTap", "2개 유지 · up TipTap"),
            32 => L("按住 2 指 · 下方 TipTap", "Hold 2 · down TipTap", "按住 2 指 · 下方 TipTap", "2 本保持 · down TipTap", "2개 유지 · down TipTap"),
            33 => L("按住 3 指 · 左侧 TipTap", "Hold 3 · left TipTap", "按住 3 指 · 左侧 TipTap", "3 本保持 · left TipTap", "3개 유지 · left TipTap"),
            34 => L("按住 3 指 · 右侧 TipTap", "Hold 3 · right TipTap", "按住 3 指 · 右侧 TipTap", "3 本保持 · right TipTap", "3개 유지 · right TipTap"),
            35 => L("按住 3 指 · 上方 TipTap", "Hold 3 · up TipTap", "按住 3 指 · 上方 TipTap", "3 本保持 · up TipTap", "3개 유지 · up TipTap"),
            36 => L("按住 3 指 · 下方 TipTap", "Hold 3 · down TipTap", "按住 3 指 · 下方 TipTap", "3 本保持 · down TipTap", "3개 유지 · down TipTap"),
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
            "TouchPadTipTap.Left" => 25,
            "TouchPadTipTap.Right" => 26,
            "TouchPadTipTap.Up" => 27,
            "TouchPadTipTap.Down" => 28,
            "TouchPadTipTap.Hold2.Left" => 29,
            "TouchPadTipTap.Hold2.Right" => 30,
            "TouchPadTipTap.Hold2.Up" => 31,
            "TouchPadTipTap.Hold2.Down" => 32,
            "TouchPadTipTap.Hold3.Left" => 33,
            "TouchPadTipTap.Hold3.Right" => 34,
            "TouchPadTipTap.Hold3.Up" => 35,
            "TouchPadTipTap.Hold3.Down" => 36,
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
            25 => "TouchPadTipTap.Left",
            26 => "TouchPadTipTap.Right",
            27 => "TouchPadTipTap.Up",
            28 => "TouchPadTipTap.Down",
            29 => "TouchPadTipTap.Hold2.Left",
            30 => "TouchPadTipTap.Hold2.Right",
            31 => "TouchPadTipTap.Hold2.Up",
            32 => "TouchPadTipTap.Hold2.Down",
            33 => "TouchPadTipTap.Hold3.Left",
            34 => "TouchPadTipTap.Hold3.Right",
            35 => "TouchPadTipTap.Hold3.Up",
            36 => "TouchPadTipTap.Hold3.Down",
            _ => string.Empty
        };

    private async Task DeleteActionAsync(LegacyApplication app, LegacyAction action)
    {
        if (!await ConfirmDialogAsync(
                DeleteConfirmationTitle(),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L("确定删除动作 {0}？", "Delete action {0}?", "確定刪除動作 {0}？", "アクション {0} を削除しますか？", "{0} 동작을 삭제하시겠습니까?"),
                    action.Name),
                DeleteButtonText()))
            return;
        _legacyData.DeleteAction(app, action);
        ReloadData();
    }

    private async Task AddCommandAsync(LegacyAction action)
    {
        var name = new TextBox { PlaceholderText = T("命令名称", "Command name"), Text = T("发送快捷键", "Send Hotkey") };
        var plugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = 0 };
        AddPluginItems(plugin);
        var pluginDescription = NewPluginDescriptionTextBlock();
        var pluginClass = new TextBox { PlaceholderText = T("自定义插件类名", "Custom plugin class name"), Text = PluginClassFromIndex(0), Margin = new Thickness(0, 8, 0, 0) };
        var settings = new TextBox { PlaceholderText = T("命令设置 JSON，可留空", "Command settings JSON (optional)"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
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
            UpdatePluginDescription(pluginDescription, pluginClassValue);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClassValue, settings.Text);
        }
        plugin.SelectionChanged += (_, _) =>
        {
            var pluginClassValue = PluginClassFromIndex(plugin.SelectedIndex);
            pluginClass.Text = pluginClassValue;
            settings.Text = pluginClassValue.Contains("HotKey", StringComparison.OrdinalIgnoreCase) ? "" : PluginSettingsTemplate(pluginClass.Text);
            UpdateDefaultCommandName(name, pluginClassValue);
            UpdateCommandEditorVisibility(pluginClassValue, pluginClass, hotkey, settings, appPicker);
            UpdatePluginDescription(pluginDescription, pluginClassValue);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClassValue, settings.Text);
        };
        var panel = NewCardPanel(0);
        panel.MinWidth = 520;
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginDescription);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        panel.Children.Add(settings);
        UpdateEditor();
        if (!await ConfirmDialogAsync(F("给 {0} 添加命令", "Add a command to {0}", action.Name), panel, T("添加", "Add")))
            return;

        CommitSelectedAppCommandChoice(appPicker, plugin, pluginClass, settings);
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
        var name = new TextBox { PlaceholderText = T("命令名称", "Command name"), Text = command.Name };
        var plugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0), SelectedIndex = PluginIndex(command.PluginClass) };
        AddPluginItems(plugin);
        var pluginDescription = NewPluginDescriptionTextBlock();
        var pluginClass = new TextBox { PlaceholderText = T("自定义插件类名", "Custom plugin class name"), Text = command.PluginClass, Margin = new Thickness(0, 8, 0, 0) };
        var settings = new TextBox { PlaceholderText = T("命令设置 JSON，可留空", "Command settings JSON (optional)"), Text = command.Settings, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
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
            UpdatePluginDescription(pluginDescription, pluginClass.Text);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClass.Text, settings.Text);
        };
        var enabled = new CheckBox { Content = T("启用", "Enable"), IsChecked = command.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var panel = NewCardPanel(0);
        panel.MinWidth = 520;
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginDescription);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        panel.Children.Add(settings);
        panel.Children.Add(enabled);
        UpdateCommandEditorVisibility(command.PluginClass, pluginClass, hotkey, settings, appPicker);
        UpdatePluginDescription(pluginDescription, command.PluginClass);
        UpdateTypedCommandSettingsEditor(typedSettings, command.PluginClass, settings.Text);
        if (!await ConfirmDialogAsync(F("编辑命令 {0}", "Edit command {0}", command.Name), panel, T("保存", "Save")))
            return;

        CommitSelectedAppCommandChoice(appPicker, plugin, pluginClass, settings);
        _legacyData.UpdateCommand(command, name.Text, pluginClass.Text, settings.Text, enabled.IsChecked ?? true);
        _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        ReloadActionDataOnly();
    }

    private async Task DeleteCommandAsync(LegacyAction action, LegacyCommand command)
    {
        if (!await ConfirmDialogAsync(
                DeleteConfirmationTitle(),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L("确定删除命令 {0}？", "Delete command {0}?", "確定刪除命令 {0}？", "コマンド {0} を削除しますか？", "{0} 명령을 삭제하시겠습니까?"),
                    command.Name),
                DeleteButtonText()))
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
}

