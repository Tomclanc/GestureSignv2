using GestureSign.Foundation.Intent;
using GestureSign.WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private Expander? _intentReview;
    private Expander? _intentSection;
    private bool _intentSectionExpanded;
    public void OpenIntentReview()
    {
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == "options");
        if (_intentReview == null) ShowPage("options");
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_intentReview == null) return;
            if (_intentSection != null) _intentSection.IsExpanded = true;
            _intentReview.IsExpanded = true;
            MainContentScrollViewer.UpdateLayout();
            var position = _intentReview.TransformToVisual(MainContentScrollViewer).TransformPoint(new Point(0, 0));
            MainContentScrollViewer.ChangeView(null, MainContentScrollViewer.VerticalOffset + position.Y, null, true);
        });
    }
    private readonly IntentComponentService _intentComponent = new();
    private string IntentText(string zh, string en) => IntentFormat(en);
    private string IntentFormat(string english, params object[] args) => GestureSign.Foundation.Localization.IntentLocalization.Format(ResolveUiCultureName(_uiCultureName), english, args);
    private string IntentMessage(string? text) => GestureSign.Foundation.Localization.IntentLocalization.Message(ResolveUiCultureName(_uiCultureName), text);
    private static Grid IntentButtonBar(params Button[] buttons)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        foreach (var button in buttons) { button.HorizontalAlignment = HorizontalAlignment.Stretch; grid.Children.Add(button); }
        int previous = 0;
        grid.SizeChanged += (_, _) =>
        {
            int columns = grid.ActualWidth >= 700 ? 3 : grid.ActualWidth >= 460 ? 2 : 1;
            if (columns == previous) return; previous = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < (buttons.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < buttons.Length; i++) { Grid.SetColumn(buttons[i], i % columns); Grid.SetRow(buttons[i], i / columns); }
        };
        return grid;
    }

    private FrameworkElement NewIntentLearningSettings()
    {
        var content = NewCardPanel(12);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("按需安装学习引擎。采样、训练和纠错都在这里完成，个人轨迹与模型只保存在本机。", "Install the optional learning engine. Record, train and correct labels here; personal traces and models stay on this PC.") };
        content.Children.Add(info); content.Children.Add(status);
        var componentBackend = new ComboBox { Header = IntentText("推理后端", "Backend"), HorizontalAlignment = HorizontalAlignment.Stretch };
        componentBackend.Items.Add("CPU");
        componentBackend.Items.Add("NPU / GPU / CPU");
        componentBackend.SelectedIndex = 0;
        content.Children.Add(componentBackend);
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7,
            Text = IntentText("CPU 版包含完整学习和 AI 否决，占用更小；NPU / GPU 版另含硬件加速。两版均需要对应架构的 .NET 10 Runtime。切换版本请先卸载组件，样本和模型会保留。", "CPU includes learning and AI veto with a smaller download. NPU / GPU adds hardware acceleration. Both require .NET 10 Runtime for the matching architecture. Uninstall the component to switch versions; samples and models are preserved.") });
        var installLocation = new ComboBox { Header = IntentText("AI 组件安装位置", "AI component location"), HorizontalAlignment = HorizontalAlignment.Stretch };
        installLocation.Items.Add(IntentText("用户数据目录（推荐）", "User data folder (recommended)"));
        if (!IsPackagedInstallation()) installLocation.Items.Add(IntentText("程序目录（便携版 / MSI）", "Application folder (portable / MSI)"));
        installLocation.SelectedIndex = IntentComponentLocation.UsesProgramDirectory(AppContext.BaseDirectory) ? 1 : 0;
        var installPath = new TextBlock { Text = IntentComponentService.InstallDirectory, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        content.Children.Add(installLocation); content.Children.Add(installPath);
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText(
            "安装后如需更换位置，请先卸载组件再选择。训练数据保留在用户目录；NPU/GPU 系统组件由 Windows 管理。程序目录受保护时需管理员权限。",
            "To change location, uninstall the component first. Training data stays in the user folder; Windows manages hardware providers. Protected application folders require administrator rights.") });
        installLocation.SelectionChanged += (_, _) =>
        {
            try { IntentComponentLocation.Select(AppContext.BaseDirectory, installLocation.SelectedIndex == 1); installPath.Text = IntentComponentService.InstallDirectory; }
            catch (Exception ex) { status.Text = UiExceptionMessage(ex); }
        };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        content.Children.Add(progress);
        var download = NewPillButton(IntentText("下载学习组件", "Download learning component"), false);
        var import = NewPillButton(IntentText("导入组件包", "Import component package"), false);
        var remove = NewPillButton(IntentText("卸载组件", "Uninstall component"), false);
        var cancel = NewPillButton(IntentText("取消下载", "Cancel download"), false);
        cancel.Visibility = Visibility.Collapsed;
        var installActions = IntentButtonBar(download, import, cancel);
        content.Children.Add(installActions);
        var settings = NewCardPanel(10); content.Children.Add(settings);
        bool backgroundActive = false;
        var background = NewPillButton(IntentText("开启后台学习", "Enable background learning"), false);
        var learningHeader = new Grid { ColumnSpacing = 16 };
        learningHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        learningHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        background.HorizontalAlignment = HorizontalAlignment.Left;
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        background.VerticalAlignment = remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 1);
        learningHeader.Children.Add(background);
        learningHeader.Children.Add(remove);
        settings.Children.Add(learningHeader);
        settings.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("开启一次后，随手势后台自动恢复采样，不弹窗、不改变动作。有空确认少量样本；电脑接通电源、空闲 2 分钟且确认样本充足时自动训练。未确认的轨迹和模型猜测不会作为训练答案，可与 AI 否决同时开启，两个开关独立控制。", "Once enabled, collection resumes with the gesture daemon. Confirm samples when convenient; automatic training requires AC power, 2 minutes of idle time and enough confirmed labels. Predictions never become training labels. Background learning and AI veto can run together with independent switches.") });
        var stop = NewPillButton(IntentText("停止 / 关闭判断", "Stop / disable classification"), false);
        settings.Children.Add(stop);
        var counts = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = IntentText("正在读取手势样本统计…", "Loading gesture sample counts…") }; settings.Children.Add(counts);
        var mode = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = IntentText("当前：正在连接学习引擎…", "Current mode: connecting to the learning engine…") }; settings.Children.Add(mode);
        var learnActions = new StackPanel { Spacing = 8 };
        var train = NewPillButton(IntentText("训练 / 重新学习", "Train / retrain"), false);
        var observe = NewPillButton(IntentText("AI评分", "AI scoring"), false);
        var protect = NewPillButton(IntentText("启用双指智能关闭拦截", "Protect two-finger Smart Close"), false);
        var trainingText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var trainingStatus = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        trainingStatus.Children.Add(trainingText);
        // Legacy validated-protection entry is hidden; retain its implementation.
        var experimental = NewPillButton(IntentText("启用 AI 否决", "Enable AI veto"), false);
        bool experimentalActive = false;
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var button in new[] { train, observe, experimental })
        {
            button.VerticalAlignment = VerticalAlignment.Center;
            actionRow.Children.Add(button);
        }
        learnActions.Children.Add(actionRow);
        learnActions.Children.Add(trainingStatus);
        settings.Children.Add(learnActions);
        // Hide the scope panel and explanatory text; the review policy is unchanged.
        // settings.Children.Add(NewIntentReviewScope());
        // settings.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("自由绘制 AI 复核：每次触控板绘制结束后、执行动作前判断一次。结合模板与意图评分；TipTap、边缘和快捷键不参与。模型未覆盖该指数量或超时时，普通动作沿用原识别，智能关闭暂停执行。可在下方纠正。", "Reviews each completed touchpad drawing before execution, using template evidence and intent scores. TipTap, edges and shortcuts are excluded. Unsupported counts or timeouts preserve ordinary actions but withhold Smart Close. Correct mistakes below.") });
        var vetoNotifications = new ToggleSwitch
        {
            Header = IntentText("AI 否决时发送通知", "Notify when AI vetoes an action"),
            OnContent = IntentText("开", "On"), OffContent = IntentText("关", "Off"),
            IsOn = IntentNotificationSettings.ReadEnabled()
        };
        bool savingNotification = false;
        vetoNotifications.Toggled += (_, _) =>
        {
            if (savingNotification) return;
            try { IntentNotificationSettings.Save(vetoNotifications.IsOn); }
            catch (Exception ex)
            {
                savingNotification = true;
                vetoNotifications.IsOn = IntentNotificationSettings.ReadEnabled();
                savingNotification = false;
                status.Text = IntentText("通知设置保存失败：", "Could not save notification setting: ") + UiExceptionMessage(ex);
            }
        };
        settings.Children.Add(vetoNotifications);
        settings.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7,
            Text = IntentText("关闭仅停止通知，AI 否决和样本记录仍继续。开启通知时附带提示音，最多每 30 秒通知一次，连续否决合并统计。", "Turning this off only silences notifications; AI veto and sample recording continue. Notifications include a sound and are grouped, at most once every 30 seconds.") });
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed }; settings.Children.Add(report);
        var hardwarePreview = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = IntentText("本机推理设备：等待学习引擎检测…", "Inference devices: waiting for hardware detection…") }; settings.Children.Add(hardwarePreview);
        var backend = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7 }; settings.Children.Add(backend);
        var hardware = NewPillButton(IntentText("准备 NPU / GPU 组件", "Prepare NPU / GPU providers"), false); settings.Children.Add(hardware);
        var hardwareInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("硬件组件按需联网下载。个性化训练使用 CPU，推理优先 NPU → GPU → CPU。AI评分仍执行原有动作；两指、三／四指及自定义绘制始终复核；不同指数量需相应训练样本。", "Hardware providers download on demand. Training uses CPU; inference prefers NPU → GPU → CPU. AI scoring still executes original actions. Two-, three/four-finger and custom drawings are always reviewed; each finger count needs training coverage.") };
        settings.Children.Add(hardwareInfo);
        var samples = new ListView { Height = 320, SelectionMode = ListViewSelectionMode.Multiple, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var samplePanel = NewCardPanel(8);
        var review = new Expander { Header = IntentText("待确认样本与纠错", "Review samples and corrections"), Content = samplePanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        settings.Children.Add(review);
        var refreshSamples = NewPillButton(IntentText("刷新样本列表", "Refresh sample list"), false);
        samplePanel.Children.Add(refreshSamples);
        samplePanel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("查看期间保持列表位置；新样本请点击刷新载入，标注结果自动更新。", "The list stays in place while reviewing. Refresh to load new samples; label changes update automatically.") });
        _intentReview = review;
        samplePanel.Children.Add(new Expander
        {
            Header = IntentText("标注与评分说明", "Labels and scores explained"),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = IntentText(
                "样本标注：滚动 / 有意手势是用于训练的标签，来自录制模式或人工纠正；未标注表示尚未确认，不会作为训练答案。\n识别候选：原手势识别器匹配到的名称，例如 L 形手势；不代表一定有意，也不代表已执行。\n意图评分：0～1，越高越倾向有意手势，越低越倾向滚动；不是模板相似度或准确率。0.000 也可能是很小数值四舍五入后的结果。\n未评分（原“—”）：没有保存模型评分，例如主动录制的样本。推理超时 / 失败：没有有效评分，不能当作 0 分。\nAI 否决：本次动作被拦截；可用下方按钮确认或纠正。历史评分记录的是当时结果，修改标签后不会立即变化。",
                "Label: scrolling / intentional labels come from guided recording or human corrections. Unlabeled samples are not training answers.\nCandidate: the original recognizer's match, not proof of intent or execution.\nIntent score: 0–1; higher means more gesture-like, lower more scroll-like. It is neither template similarity nor measured accuracy. 0.000 can also be a rounded small value.\nNot scored (formerly —): no stored prediction, for example a guided recording. Timeout / failure means no valid score, not zero.\nAI veto: the action was blocked. Confirm or correct it below. Relabeling does not change historical scores.") }
        });
        var viewMode = new ComboBox { Header = IntentText("显示方式", "View"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160 };
        foreach (var name in new[] { IntentText("列表", "List"), IntentText("网格", "Grid"), IntentText("磁贴", "Tiles") }) viewMode.Items.Add(name);
        viewMode.SelectedIndex = 0;
        samplePanel.Children.Add(viewMode);
        var selectAll = NewPillButton(IntentText("全选当前列表", "Select all shown"), false);
        var clearSelection = NewPillButton(IntentText("取消选择", "Clear selection"), false);
        samplePanel.Children.Add(IntentButtonBar(selectAll, clearSelection));
        var selectionCount = new TextBlock(); samplePanel.Children.Add(selectionCount);
        samplePanel.Children.Add(samples);
        var trace = new Canvas { Height = 140, HorizontalAlignment = HorizontalAlignment.Stretch };
        samplePanel.Children.Add(trace);
        var contextReason = new TextBlock { TextWrapping = TextWrapping.Wrap };
        samplePanel.Children.Add(contextReason);
        var labelScroll = NewPillButton(IntentText("这是滚动", "This was scrolling"), false);
        var unlabel = NewPillButton(IntentText("撤销标注", "Clear label"), false);
        var delete = NewPillButton(IntentText("删除样本", "Delete sample"), false);
        var vetoWrong = NewPillButton(IntentText("纠正为指定手势…", "Correct to gesture…"), false);
        var corrections = new Grid { ColumnSpacing = 8 };
        var correctionButtons = new[] { labelScroll, unlabel, delete, vetoWrong };
        for (int i = 0; i < correctionButtons.Length; i++)
        {
            corrections.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            correctionButtons[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            correctionButtons[i].VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(correctionButtons[i], i);
            corrections.Children.Add(correctionButtons[i]);
        }
        samplePanel.Children.Add(corrections);
        samplePanel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("选择样本并指定正确手势，支持批量纠正。补充模板参与后续识别；意图标注用于下次训练，不补执行历史动作。", "Select samples and choose the correct gesture. Bulk corrections are supported. Added examples participate in later recognition; intent labels apply to the next training run. Past actions are never replayed.") });
        var correctionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        samplePanel.Children.Add(correctionStatus);
        samplePanel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("选择轨迹修正标注后重新训练。保留最多 2000 条，列表显示最近 100 条。评分不是准确率，需真实样本验证。关闭设置窗口后后台继续工作；退出手势后台后自动停止。", "Correct labels and retrain. Keeps up to 2,000 traces; shows the latest 100. Scores are not measured accuracy. The engine continues when settings close, and stops with the gesture daemon.") });
        var files = NewPillButton(IntentText("打开本地学习数据", "Open local learning data"), false);
        files.Click += (_, _) => { System.IO.Directory.CreateDirectory(IntentFiles.Root); Process.Start(new ProcessStartInfo("explorer.exe", IntentFiles.Root) { UseShellExecute = true }); };
        samplePanel.Children.Add(files);

        bool progressPolling = false;
        var trainingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void ShowTrainingProgress(IntentHostResponse value)
        {
            if (value.TrainingPercent is int percent)
            {
                trainingStatus.Visibility = Visibility.Visible;
                // Older optional components still append the legacy protection-gate result.
                var stage = value.TrainingStage;
                if (stage == "训练完成 · 已达到保护门槛" || stage == "训练完成 · 尚未达到保护门槛" || stage == "训练完成")
                    stage = IntentText("训练完成", "Training complete");
                trainingText.Text = $"{percent}% · {IntentMessage(stage)}";
            }
            if (!value.Busy) trainingTimer.Stop();
            else if (value.TrainingPercent != null) trainingTimer.Start();
        }
        trainingTimer.Tick += async (_, _) =>
        {
            if (progressPolling) return; progressPolling = true;
            try { ShowTrainingProgress(await _intentComponent.SendAsync(new("progress"))); }
            catch (Exception ex) { trainingTimer.Stop(); trainingText.Text = UiExceptionMessage(ex); }
            finally { progressPolling = false; }
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var cancellation = new CancellationTokenSource();
        bool transfer = false, refreshing = false, loaded = false, busy = false, updating = false;
        bool samplesInitialized = false, reloadSamples = false, correcting = false;
        int correctionRevision = 0;
        var correctedNames = new Dictionary<string, string>();
        string ScoreText(IntentSampleSummary sample)
        {
            if (sample.Backend == "Not trained") return IntentText("该指数量尚未训练", "Finger count not trained");
            if (!string.IsNullOrEmpty(sample.PredictionError))
                return sample.PredictionError.Contains("deadline", StringComparison.OrdinalIgnoreCase) || sample.PredictionError.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    ? IntentText("推理超时", "Inference timed out") : IntentText("推理失败", "Inference failed");
            if (sample.Backend == "Unavailable") return IntentText("推理不可用", "Inference unavailable");
            return sample.Score is float score && float.IsFinite(score) ? score.ToString("0.000") : IntentText("未评分", "Not scored");
        }
        string LabelText(IntentSampleSummary sample) => correctedNames.TryGetValue(sample.Id, out var corrected) ? IntentText("已纠正为：", "Corrected to: ") + DisplayName(corrected) : sample.Label == IntentLabel.Scroll ? IntentText("滚动", "Scroll") : sample.Label == IntentLabel.Gesture ? IntentText("有意手势", "Intentional") : IntentText("未标注", "Unlabeled");
        string SampleText(IntentSampleSummary s) => (s.AiVeto ? "[" + IntentFormat("AI veto") + "] " : "") +
            $"{s.CreatedUtc.ToLocalTime():HH:mm:ss} · {IntentText("样本标注", "Label")}：{LabelText(s)} · {IntentText("识别候选", "Candidate")}：{(s.Candidate == null ? IntentText("无", "None") : DisplayName(s.Candidate))} · {IntentText("意图评分", "Intent score")}：{ScoreText(s)} · {IntentText("推理后端", "Backend")}：{(s.Backend == null ? IntentText("无记录", "Not recorded") : IntentMessage(s.Backend))}" +
            (s.PredictionError == null ? "" : " · " + s.PredictionError);
        IntentSample? selectedTrace = null;
        string? SelectedId() => samples.SelectedItems.Cast<ListViewItem>().LastOrDefault()?.Tag as string;
        void UpdateSampleItem(ListViewItem item, IntentSampleSummary value)
        {
            item.DataContext = value;
            var text = (TextBlock)((StackPanel)item.Content).Children[0];
            string display = viewMode.SelectedIndex == 0 ? SampleText(value) : (value.AiVeto ? "[" + IntentFormat("AI veto") + "] " : "") +
                $"{value.CreatedUtc.ToLocalTime():HH:mm:ss}\n{IntentText("样本标注", "Label")}：{LabelText(value)}\n{IntentText("识别候选", "Candidate")}：{(value.Candidate == null ? IntentText("无", "None") : DisplayName(value.Candidate))}\n{IntentText("意图评分", "Intent score")}：{ScoreText(value)}";
            if (text.Text != display) text.Text = display;
            ToolTipService.SetToolTip(item, SampleText(value));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, SampleText(value));
        }
        void RenderSampleItem(ListViewItem item)
        {
            var value = (IntentSampleSummary)item.DataContext;
            var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4), Width = viewMode.SelectedIndex == 0 ? double.NaN : viewMode.SelectedIndex == 1 ? 220 : 260 };
            panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap });
            if (viewMode.SelectedIndex == 2)
            {
                var preview = new Canvas { Width = 248, Height = 90 };
                panel.Children.Add(preview);
                bool drawn = false;
                preview.Loaded += async (_, _) =>
                {
                    if (drawn) return; drawn = true;
                    try
                    {
                        var sample = await Task.Run(() => IntentFiles.Read<IntentSample>(IntentFiles.SamplePath(value.Id)));
                        if (sample == null) return;
                        var points = sample.Frames.SelectMany(f => f.Points).ToArray();
                        if (points.Length == 0) return;
                        double minX = points.Min(v => v.X), minY = points.Min(v => v.Y);
                        double scale = Math.Min(232 / Math.Max(1, points.Max(v => v.X) - minX), 74 / Math.Max(1, points.Max(v => v.Y) - minY));
                        foreach (var (id, index) in points.Select(v => v.Contact).Distinct().Select((id, index) => (id, index)))
                        {
                            var line = new Polyline { Stroke = new SolidColorBrush(index == 0 ? Colors.DodgerBlue : Colors.Orange), StrokeThickness = 2 };
                            foreach (var point in points.Where(v => v.Contact == id)) line.Points.Add(new Point(8 + (point.X - minX) * scale, 8 + (point.Y - minY) * scale));
                            preview.Children.Add(line);
                            AddAccessibleTraceMarker(preview, line, index);
                        }
                    }
                    catch { ToolTipService.SetToolTip(preview, IntentText("预览不可用，请刷新列表。", "Preview unavailable; refresh the list.")); }
                };
            }
            item.Content = panel;
            UpdateSampleItem(item, value);
        }
        ListViewItem NewSampleItem(IntentSampleSummary value)
        {
            var item = new ListViewItem { Tag = value.Id, DataContext = value, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            RenderSampleItem(item); return item;
        }
        viewMode.SelectionChanged += (_, _) =>
        {
            var textScale = Math.Clamp(new Windows.UI.ViewManagement.UISettings().TextScaleFactor, 1, 2.25);
            string panel = viewMode.SelectedIndex == 0 ? "<ItemsStackPanel />" : viewMode.SelectedIndex == 1 ? $"<ItemsWrapGrid Orientation='Horizontal' ItemWidth='244' ItemHeight='{Math.Ceiling(160 * textScale)}' />" : $"<ItemsWrapGrid Orientation='Horizontal' ItemWidth='284' ItemHeight='{Math.Ceiling(264 * textScale)}' />";
            samples.ItemsPanel = (Microsoft.UI.Xaml.Controls.ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + panel + "</ItemsPanelTemplate>");
            foreach (var item in samples.Items.Cast<ListViewItem>()) RenderSampleItem(item);
        };
        selectAll.Click += (_, _) => samples.SelectAll();
        clearSelection.Click += (_, _) => samples.SelectedItems.Clear();
        void DrawTrace()
        {
            trace.Children.Clear();
            contextReason.Text = IntentMessage(selectedTrace?.ContextReason ?? "");
            if (selectedTrace == null || trace.ActualWidth < 32) return;
            var points = selectedTrace.Frames.SelectMany(f => f.Points).ToArray(); if (points.Length == 0) return;
            double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
            double scale = Math.Min((trace.ActualWidth - 24) / Math.Max(1, points.Max(p => p.X) - minX), 110 / Math.Max(1, points.Max(p => p.Y) - minY));
            foreach (var (id, i) in points.Select(p => p.Contact).Distinct().Select((id, i) => (id, i)))
            {
                var line = new Polyline { Stroke = new SolidColorBrush(i == 0 ? Colors.DodgerBlue : Colors.Orange), StrokeThickness = 2 };
                foreach (var p in selectedTrace.Frames.SelectMany(f => f.Points.Where(p => p.Contact == id))) line.Points.Add(new Point(12 + (p.X - minX) * scale, 12 + (p.Y - minY) * scale));
                trace.Children.Add(line);
                AddAccessibleTraceMarker(trace, line, i);
            }
        }
        trace.SizeChanged += (_, _) => DrawTrace();
        void SelectionState()
        {
            int count = samples.SelectedItems.Count;
            selectionCount.Text = IntentFormat("{0} selected / {1} shown. Click to select multiple samples; the preview shows the last selected item.", count, samples.Items.Count);
            foreach (var b in new[] { labelScroll, unlabel, delete }) b.IsEnabled = count > 0 && !busy && !transfer && !correcting;
            vetoWrong.IsEnabled = count > 0 && !busy && !transfer && !correcting;
            samples.IsEnabled = viewMode.IsEnabled = selectAll.IsEnabled = clearSelection.IsEnabled = refreshSamples.IsEnabled = !correcting;
        }
        void InstalledState()
        {
            bool installed = _intentComponent.Installed;
            componentBackend.IsEnabled = !installed && !transfer;
            if (installed) componentBackend.SelectedIndex = _intentComponent.HardwareInstalled ? 1 : 0;
            hardware.Visibility = _intentComponent.HardwareInstalled ? Visibility.Visible : Visibility.Collapsed;
            hardwareInfo.Visibility = hardware.Visibility;
            installLocation.IsEnabled = !installed && !transfer;
            installPath.Text = IntentComponentService.InstallDirectory;
            settings.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            installActions.Visibility = installed && !transfer ? Visibility.Collapsed : Visibility.Visible;
            download.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            import.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            remove.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            download.IsEnabled = import.IsEnabled = remove.IsEnabled = !transfer;
            status.Text = installed ? IntentText("学习组件已安装 · 独立更新，默认关闭", "Learning component installed · updated separately, disabled by default") : IntentText("尚未安装，可选下载后启用。", "Not installed. Download only if you want to use learning.");
            try { if (!installed) status.Text += $"  {_intentComponent.Asset.Bytes / 1048576d:0.0} MB · {IntentComponentService.Architecture}"; } catch (Exception ex) { status.Text += " " + UiExceptionMessage(ex); }
        }
        async Task Refresh()
        {
            if (!loaded || refreshing || transfer || correcting || !_intentComponent.Installed) return;
            refreshing = true;
            int revision = correctionRevision;
            try
            {
                var state = await _intentComponent.SendAsync(new("status")); if (!loaded || revision != correctionRevision) return;
                correctedNames = IntentGestureCorrections.Read(IntentFiles.Root).ToDictionary(c => c.SampleId, c => c.Gesture);
                status.Text = IntentText("学习组件已安装 · 后台引擎已连接", "Learning component installed · background engine connected");
                busy = state.Busy;
                ShowTrainingProgress(state);
                background.IsEnabled = !busy;
                foreach (var b in new[] { train, observe, hardware }) b.IsEnabled = !busy;
                protect.IsEnabled = state.Eligible && !busy;
                experimental.IsEnabled = state.HasModel && !busy;
                hardwarePreview.Text = string.IsNullOrWhiteSpace(state.HardwarePreview)
                    ? IntentText("本机设备尚未检测，请更新学习组件或等待引擎就绪。", "Hardware not detected yet. Update the learning component or wait for the engine.") : IntentMessage(state.HardwarePreview);
                backend.Text = IntentText("实际推理后端：", "Actual inference backend: ") + IntentMessage(state.Backend);
                counts.Text = IntentFormat("Gesture samples: labeled scrolling {0} \u00B7 labeled intentional gestures {1} \u00B7 unlabeled {2}", state.Scrolls, state.Gestures, state.Unknown);
                var c = state.Control; var now = DateTimeOffset.UtcNow;
                backgroundActive = state.BackgroundLearning;
                background.Content = backgroundActive
                    ? IntentText("后台学习已开启 · 点击关闭", "Background learning enabled · Click to turn off")
                    : IntentText("开启后台学习", "Enable background learning");
                background.Style = backgroundActive ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
                experimentalActive = c.Mode == IntentMode.ExperimentalVeto && now >= c.StartsUtc && now < c.ExpiresUtc;
                experimental.Content = experimentalActive
                    ? IntentText("已启用 AI 否决 · 点击关闭", "AI veto enabled · Click to turn off")
                    : IntentText("启用 AI 否决", "Enable AI veto");
                experimental.Style = experimentalActive ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
                mode.Text = c.Mode == IntentMode.Off || now >= c.ExpiresUtc ? IntentText("当前：关闭", "Current mode: off") :
                    now < c.StartsUtc ? IntentText("开始倒计时：", "Starting in: ") + $"{Math.Ceiling((c.StartsUtc - now).TotalSeconds)} s" :
                    c.Mode == IntentMode.BackgroundLearn ? IntentText("当前：后台学习 · 不拦截 · 待确认样本不会自动当成答案", "Current mode: background learning · no blocking · confirmed labels only") :
                    c.Mode == IntentMode.ExperimentalVeto ? IntentText("当前：AI 否决已开启 · 所有自由绘制动作", "Current mode: AI veto · drawing review") :
                    c.Mode == IntentMode.Observe ? IntentText("当前：AI评分 · 所有自由绘制动作 · 不拦截", "Current mode: AI scoring · all drawing actions · no blocking") : IntentText("当前：双指智能关闭保护", "Current mode: protecting two-finger Smart Close");
                if (state.BackgroundLearning && c.Mode == IntentMode.ExperimentalVeto && now < c.ExpiresUtc)
                    mode.Text += IntentText(" · 后台学习已开启", " · Background learning enabled");
                // Keep item containers and selection intact during polling. In particular,
                // never reselect an item: ListView may bring it into view on every tick.
                var latest = state.Samples.ToDictionary(s => s.Id);
                updating = true;
                for (int i = samples.Items.Count - 1; i >= 0; i--)
                {
                    var item = (ListViewItem)samples.Items[i];
                    if (latest.TryGetValue((string)item.Tag, out var sample))
                    {
                        UpdateSampleItem(item, sample);
                    }
                    else if (!review.IsExpanded || reloadSamples)
                        samples.Items.RemoveAt(i);
                }
                if (!review.IsExpanded || reloadSamples || !samplesInitialized)
                {
                    for (int i = 0; i < state.Samples.Length; i++)
                    {
                        var sample = state.Samples[i];
                        if (i < samples.Items.Count && (string)((ListViewItem)samples.Items[i]).Tag == sample.Id) continue;
                        var existing = samples.Items.Cast<ListViewItem>().FirstOrDefault(item => (string)item.Tag == sample.Id);
                        if (existing != null) samples.Items.Remove(existing);
                        samples.Items.Insert(i, existing ?? NewSampleItem(sample));
                    }
                    samplesInitialized = true; reloadSamples = false;
                }
                int newSamples = state.Samples.Count(s => !samples.Items.Cast<ListViewItem>().Any(item => (string)item.Tag == s.Id));
                refreshSamples.Content = newSamples == 0 ? IntentText("刷新样本列表", "Refresh sample list") : IntentFormat("Refresh sample list ({0} new)", newSamples);
                if (SelectedId() == null) { selectedTrace = null; DrawTrace(); }
                updating = false; SelectionState();
            }
            catch (OperationCanceledException) { if (loaded) status.Text = IntentText("学习引擎响应较慢，正在重试；已有显示暂未更新。", "The learning engine is responding slowly. Retrying; displayed data has not refreshed."); }
            catch (Exception ex) { if (loaded) status.Text = UiExceptionMessage(ex); }
            finally { refreshing = false; updating = false; }
        }
        async Task Command(IntentHostRequest request)
        {
            try
            {
                var response = await _intentComponent.SendAsync(request);
                if (request.Command == "train") ShowTrainingProgress(response);
                if (response.Error == null && request.Command == "delete")
                {
                    var item = samples.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag == request.SampleId);
                    if (item != null) samples.Items.Remove(item);
                }
                await Refresh();
            }
            catch (Exception ex) { report.Text = UiExceptionMessage(ex); report.Visibility = Visibility.Visible; if (request.Command == "train") { trainingTimer.Stop(); trainingText.Text = IntentText("训练未启动：", "Training did not start: ") + UiExceptionMessage(ex); train.IsEnabled = true; } }
        }
        samples.SelectionChanged += async (_, _) =>
        {
            SelectionState(); if (updating) return; var id = SelectedId(); selectedTrace = null; DrawTrace(); if (id == null) return;
            try { var response = await _intentComponent.SendAsync(new("trace", SampleId: id)); if (SelectedId() == id) { selectedTrace = response.Sample; DrawTrace(); } }
            catch (Exception ex) { report.Text = UiExceptionMessage(ex); report.Visibility = Visibility.Visible; }
        };
        refreshSamples.Click += async (_, _) => { reloadSamples = true; await Refresh(); };
        background.Click += async (_, _) =>
        {
            background.IsEnabled = false;
            await Command(new(backgroundActive ? "background-off" : "background-on"));
        };
        stop.Click += async (_, _) => await Command(new("mode", IntentMode.Off));
        train.Click += async (_, _) =>
        {
            train.IsEnabled = false;
            trainingStatus.Visibility = Visibility.Visible;
            trainingText.Text = IntentText("0% · 正在开始训练", "0% · Starting training");
            await Command(new("train"));
        };
        observe.Click += async (_, _) => await Command(new("mode", IntentMode.Observe));
        experimental.Click += async (_, _) =>
        {
            experimental.IsEnabled = false;
            await Command(experimentalActive ? new("veto-off") : new("mode", IntentMode.ExperimentalVeto));
        };
        protect.Click += async (_, _) => await Command(new("mode", IntentMode.ProtectSmartClose));
        hardware.Click += async (_, _) => await Command(new("hardware"));
        async Task CorrectSamples(string command, IntentLabel label = IntentLabel.Unknown, bool vetoOnly = false, string? gesture = null, string[]? targetIds = null)
        {
            if (correcting) return;
            // Snapshot IDs before awaiting: selection and host sorting must not redirect edits.
            var ids = targetIds ?? samples.SelectedItems.Cast<ListViewItem>().Where(i => !vetoOnly || (i.DataContext as IntentSampleSummary)?.AiVeto == true).Select(i => (string)i.Tag).Distinct().ToArray();
            if (ids.Length == 0) return;
            correcting = true; correctionRevision++; SelectionState();
            int saved = 0, failed = 0, refreshFailed = 0;
            string lastError = "";
            try
            {
                foreach (var id in ids)
                {
                    correctionStatus.Text = IntentFormat("Processing {0}/{1}\u2026", saved + failed + 1, ids.Length);
                    try
                    {
                        var previous = IntentGestureCorrections.Read(IntentFiles.Root).FirstOrDefault(c => c.SampleId == id);
                        var source = (await _intentComponent.SendAsync(new("trace", SampleId: id))).Sample ?? throw new InvalidOperationException("样本不存在。");
                        if (gesture != null)
                        {
                            var target = LegacyDataStore.Load().Gestures.FirstOrDefault(g => g.Name == gesture);
                            if (target == null || target.FingerCount != IntentGestureCorrections.Contacts(source.Frames)) throw new InvalidOperationException("手势已删除或指数量不匹配。");
                        }
                        IntentGestureCorrections.Save(IntentFiles.Root, source, gesture);
                        try { await _intentComponent.SendAsync(new(command, SampleId: id, Label: label)); }
                        catch
                        {
                            IntentGestureCorrections.Save(IntentFiles.Root, previous == null ? source : new IntentSample { Id = id, Frames = previous.Frames }, previous?.Gesture);
                            throw;
                        }
                        if (gesture == null) correctedNames.Remove(id); else correctedNames[id] = gesture;
                        saved++;
                    }
                    catch (Exception ex) { failed++; lastError = UiExceptionMessage(ex); continue; }
                    try
                    {
                        var item = samples.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag == id);
                        if (command == "delete")
                        {
                            if (item != null) samples.Items.Remove(item);
                        }
                        else
                        {
                            var result = await _intentComponent.SendAsync(new("trace", SampleId: id));
                            var sample = result.Sample ?? throw new InvalidOperationException("Updated sample unavailable.");
                            if (item != null) UpdateSampleItem(item, new IntentSampleSummary(sample.Id, sample.CreatedUtc, sample.Label, sample.Candidate, sample.Prediction?.GestureScore, sample.Prediction?.Backend, sample.Blocked, sample.AiVeto, sample.Prediction?.Error));
                            if (SelectedId() == sample.Id) { selectedTrace = sample; DrawTrace(); }
                        }
                    }
                    catch (Exception ex) { refreshFailed++; lastError = UiExceptionMessage(ex); }
                }
                string operation = command == "delete" ? IntentText("删除", "Delete") : label == IntentLabel.Scroll ? IntentText("标记为滚动", "Mark as scrolling") : label == IntentLabel.Gesture ? IntentText("标记为有意手势", "Mark as intentional") : IntentText("撤销标注", "Clear labels");
                correctionStatus.Text = IntentFormat("{0}: {1} succeeded, {2} failed.", operation, saved, failed) +
                    (refreshFailed > 0 ? IntentFormat(" {0} saved but failed to refresh.", refreshFailed) : "") +
                    (lastError.Length > 0 ? " " + lastError : "") + " " +
                    IntentText("标注用于下次训练，不会立即改变模型评分。", "Corrections apply to the next training run; scores do not change immediately.");
            }
            finally { correcting = false; correctionRevision++; SelectionState(); }
            if (saved > 0)
            {
                if (!await NotifyDaemonAsync(DaemonCommand.LoadGestures)) correctionStatus.Text += IntentText(" 后台未连接，模板将在下次启动生效。", " Backend disconnected; templates apply on next launch.");
                else if (gesture != null) correctionStatus.Text += IntentFormat(" Added recognition examples for \u0027{0}\u0027.", gesture);
            }
            await Refresh();
        }
        vetoWrong.Click += async (_, _) =>
        {
            var ids = samples.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).Distinct().ToArray();
            if (ids.Length == 0 || correcting) return;
            try
            {
                var traces = new List<IntentSample>();
                foreach (var id in ids) traces.Add((await _intentComponent.SendAsync(new("trace", SampleId: id))).Sample ?? throw new InvalidOperationException("样本不存在。"));
                var counts = traces.Select(t => IntentGestureCorrections.Contacts(t.Frames)).Distinct().ToArray();
                if (counts.Length != 1) { await ShowInfoDialog(IntentFormat("Choose samples with the same finger count"), IntentFormat("All samples must have the same finger count to correct them to one gesture.")); return; }
                var available = LegacyDataStore.Load().Gestures.Where(g => g.FingerCount == counts[0] && g.Source["PointPatterns"] is System.Text.Json.Nodes.JsonArray patterns && patterns.Count == 1).ToArray();
                if (available.Length == 0) { await ShowInfoDialog(IntentFormat("No matching gestures"), IntentFormat("Create a drawing gesture with the same finger count in the gesture library first.")); return; }
                var choice = new ComboBox { Header = IntentText("正确的手势", "Correct gesture"), HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = IntentText("请选择手势", "Choose a gesture") };
                foreach (var g in available) choice.Items.Add(new ComboBoxItem { Content = DisplayName(g.Name) + " · " + IntentFormat("{0} fingers", g.FingerCount), Tag = g });
                var preview = new ContentControl();
                choice.SelectionChanged += (_, _) => preview.Content = choice.SelectedItem is ComboBoxItem { Tag: LegacyGesture g } ? NewGestureCanvas(g, 300, 130) : null;
                var panel = NewCardPanel(10);
                panel.Children.Add(new TextBlock { Text = IntentFormat("Add {0} traces as recognition examples, preserving existing templates and original recognition records. Label as intentional for the next training run.", ids.Length), TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(choice); panel.Children.Add(preview);
                var dialog = new ContentDialog { Title = IntentText("纠正为指定手势", "Correct to gesture"), Content = panel, PrimaryButtonText = IntentText("保存纠正", "Save correction"), CloseButtonText = IntentText("取消", "Cancel"), IsPrimaryButtonEnabled = false, XamlRoot = Content.XamlRoot };
                choice.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = choice.SelectedItem != null;
                if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary && choice.SelectedItem is ComboBoxItem { Tag: LegacyGesture selected })
                    await CorrectSamples("label", IntentLabel.Gesture, gesture: selected.Name, targetIds: ids);
            }
            catch (Exception ex) { correctionStatus.Text = UiExceptionMessage(ex); }
        };
        labelScroll.Click += async (_, _) => await CorrectSamples("label", IntentLabel.Scroll);
        unlabel.Click += async (_, _) => await CorrectSamples("label");
        delete.Click += async (_, _) => await CorrectSamples("delete");
        async Task Install(bool offline)
        {
            if (transfer) return;
            var archive = offline ? await PickOpenFileAsync([".zip"]) : null; if (offline && archive == null) return;
            transfer = true; cancellation.Dispose(); cancellation = new CancellationTokenSource(); InstalledState();
            cancel.Visibility = Visibility.Visible; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = offline;
            try
            {
                if (archive != null) await _intentComponent.InstallAsync(archive, cancellation.Token);
                else await _intentComponent.DownloadAsync(new Progress<double>(p => { progress.IsIndeterminate = false; progress.Value = p; status.Text = IntentText("下载并安装：", "Downloading and installing: ") + $"{p:0}%"; }), cancellation.Token);
                InstalledState();
            }
            catch (OperationCanceledException) { status.Text = IntentText("已取消。", "Canceled."); }
            catch (Exception ex) { status.Text = UiExceptionMessage(ex); }
            finally { transfer = false; componentBackend.IsEnabled = installLocation.IsEnabled = !_intentComponent.Installed; download.IsEnabled = import.IsEnabled = remove.IsEnabled = true; cancel.Visibility = progress.Visibility = Visibility.Collapsed; await Refresh(); }
        }
        download.Click += async (_, _) => await Install(false); import.Click += async (_, _) => await Install(true); cancel.Click += (_, _) => cancellation.Cancel();
        componentBackend.SelectionChanged += (_, _) => { _intentComponent.Backend = componentBackend.SelectedIndex == 1 ? "Hardware" : "Cpu"; InstalledState(); };
        remove.Click += async (_, _) =>
        {
            if (transfer) return; transfer = true; installLocation.IsEnabled = false;
            try { await _intentComponent.UninstallAsync(); InstalledState(); }
            catch (Exception ex) { status.Text = UiExceptionMessage(ex); }
            finally { transfer = false; componentBackend.IsEnabled = installLocation.IsEnabled = !_intentComponent.Installed; download.IsEnabled = import.IsEnabled = remove.IsEnabled = true; }
        };
        content.Loaded += async (_, _) => { loaded = true; ApplyAccessibleTree(Root); timer.Start(); await Refresh(); };
        content.Unloaded += (_, _) => { loaded = false; timer.Stop(); trainingTimer.Stop(); cancellation.Cancel(); };
        timer.Tick += async (_, _) => await Refresh(); InstalledState(); SelectionState();
        _intentSection = new Expander
        {
            Header = IntentText("AI：本地意图学习（可选组件）", "AI: local intent learning (optional)"),
            Content = content, IsExpanded = _intentSectionExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _intentSection.Expanding += (_, _) => _intentSectionExpanded = true;
        _intentSection.Collapsed += (_, _) => _intentSectionExpanded = false;
        return _intentSection;
    }
}
