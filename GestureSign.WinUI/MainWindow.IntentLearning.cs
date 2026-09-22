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
    private readonly IntentComponentService _intentComponent = new();
    private string IntentText(string zh, string en) => L(zh, en, zh, en, en);
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
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        content.Children.Add(progress);
        var download = NewPillButton(IntentText("下载学习组件", "Download learning component"), false);
        var import = NewPillButton(IntentText("导入组件包", "Import component package"), false);
        var remove = NewPillButton(IntentText("卸载组件", "Uninstall component"), false);
        var cancel = NewPillButton(IntentText("取消下载", "Cancel download"), false);
        cancel.Visibility = Visibility.Collapsed;
        var installActions = IntentButtonBar(download, import, remove, cancel);
        content.Children.Add(installActions);
        var settings = NewCardPanel(10); content.Children.Add(settings);
        var background = NewPillButton(IntentText("开启后台学习（不拦截）", "Enable background learning (no blocking)"), false);
        settings.Children.Add(background);
        settings.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("开启一次后，随手势后台自动恢复采样，不弹窗、不改变动作。有空确认少量样本；电脑空闲 2 分钟且确认样本充足时自动训练。未确认的轨迹和模型猜测不会作为训练答案，保护功能需另行手动开启。", "Once enabled, collection resumes with the gesture daemon, without popups or blocking. Confirm a few samples when convenient; training runs after 2 minutes of idle time with enough confirmed labels. Predictions never become training labels. Protection requires separate manual activation.") });
        var recordingActions = new StackPanel { Spacing = 8 };
        var recordScroll = NewPillButton(IntentText("录制滚动 · 45 秒", "Record scrolling · 45 seconds"), false);
        var recordGesture = NewPillButton(IntentText("录制有意画 L · 45 秒", "Record intentional L · 45 seconds"), false);
        var stop = NewPillButton(IntentText("停止 / 关闭判断", "Stop / disable classification"), false);
        var recordingRow = IntentButtonBar(recordScroll, recordGesture);
        recordingActions.Children.Add(recordingRow);
        recordingActions.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("3 秒倒计时后开始。每类至少 4 次独立录制、累计 40 条；采样期间暂停绘制手势命令。TipTap、边缘和连续动作不在暂停范围。", "Starts after 3 seconds. Collect at least 40 traces per label across 4 sessions. Traced commands are paused during recording; TipTap, edge and continuous actions are not.") });
        settings.Children.Add(new Expander { Header = IntentText("主动补充样本（可选）", "Guided recording (optional)"), Content = recordingActions, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        settings.Children.Add(stop);
        var counts = new TextBlock { TextWrapping = TextWrapping.Wrap }; settings.Children.Add(counts);
        var mode = new TextBlock { TextWrapping = TextWrapping.Wrap }; settings.Children.Add(mode);
        var learnActions = new StackPanel { Spacing = 8 };
        var train = NewPillButton(IntentText("训练 / 重新学习", "Train / retrain"), false);
        var observe = NewPillButton(IntentText("观察评分（不拦截）", "Observe scores (no blocking)"), false);
        var protect = NewPillButton(IntentText("启用双指智能关闭拦截", "Protect two-finger Smart Close"), false);
        foreach (var b in new[] { train, observe, protect }) learnActions.Children.Add(b);
        settings.Children.Add(learnActions);
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap }; settings.Children.Add(report);
        var backend = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7 }; settings.Children.Add(backend);
        var hardware = NewPillButton(IntentText("准备 NPU / GPU 组件", "Prepare NPU / GPU providers"), false); settings.Children.Add(hardware);
        settings.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("硬件组件按需联网下载。个性化训练使用 CPU，推理优先 NPU → GPU → CPU。观察模式仍执行原有动作；实验性拦截仅保护双指智能关闭。", "Hardware providers download on demand. Training uses CPU; inference prefers NPU → GPU → CPU. Observe mode still executes original actions. Experimental blocking protects only two-finger Smart Close.") });
        var samples = new ListView { Height = 220, SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var samplePanel = NewCardPanel(8);
        settings.Children.Add(new Expander { Header = IntentText("待确认样本与纠错", "Review samples and corrections"), Content = samplePanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        samplePanel.Children.Add(samples);
        var trace = new Canvas { Height = 140, HorizontalAlignment = HorizontalAlignment.Stretch };
        samplePanel.Children.Add(trace);
        var labelScroll = NewPillButton(IntentText("这是滚动", "This was scrolling"), false);
        var labelGesture = NewPillButton(IntentText("这是有意手势", "This was intentional"), false);
        var unlabel = NewPillButton(IntentText("撤销标注", "Clear label"), false);
        var delete = NewPillButton(IntentText("删除样本", "Delete sample"), false);
        var corrections = IntentButtonBar(labelScroll, labelGesture, unlabel, delete);
        samplePanel.Children.Add(corrections);
        samplePanel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Text = IntentText("选择轨迹修正标注后重新训练。保留最多 2000 条，列表显示最近 100 条。评分不是准确率，需真实样本验证。关闭设置窗口后后台继续工作；退出手势后台后自动停止。", "Correct labels and retrain. Keeps up to 2,000 traces; shows the latest 100. Scores are not measured accuracy. The engine continues when settings close, and stops with the gesture daemon.") });
        var files = NewPillButton(IntentText("打开本地学习数据", "Open local learning data"), false);
        files.Click += (_, _) => { System.IO.Directory.CreateDirectory(IntentFiles.Root); Process.Start(new ProcessStartInfo("explorer.exe", IntentFiles.Root) { UseShellExecute = true }); };
        samplePanel.Children.Add(files);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var cancellation = new CancellationTokenSource();
        bool transfer = false, refreshing = false, loaded = false, busy = false, updating = false;
        IntentSample? selectedTrace = null;
        string? SelectedId() => (samples.SelectedItem as ListViewItem)?.Tag as string;
        void DrawTrace()
        {
            trace.Children.Clear();
            if (selectedTrace == null || trace.ActualWidth < 32) return;
            var points = selectedTrace.Frames.SelectMany(f => f.Points).ToArray(); if (points.Length == 0) return;
            double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
            double scale = Math.Min((trace.ActualWidth - 24) / Math.Max(1, points.Max(p => p.X) - minX), 110 / Math.Max(1, points.Max(p => p.Y) - minY));
            foreach (var (id, i) in points.Select(p => p.Contact).Distinct().Select((id, i) => (id, i)))
            {
                var line = new Polyline { Stroke = new SolidColorBrush(i == 0 ? Colors.DodgerBlue : Colors.Orange), StrokeThickness = 2 };
                foreach (var p in selectedTrace.Frames.SelectMany(f => f.Points.Where(p => p.Contact == id))) line.Points.Add(new Point(12 + (p.X - minX) * scale, 12 + (p.Y - minY) * scale));
                trace.Children.Add(line);
            }
        }
        trace.SizeChanged += (_, _) => DrawTrace();
        void SelectionState()
        {
            foreach (var b in new[] { labelScroll, labelGesture, unlabel, delete }) b.IsEnabled = SelectedId() != null && !busy && !transfer;
        }
        void InstalledState()
        {
            bool installed = _intentComponent.Installed;
            settings.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            download.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            import.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            remove.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            download.IsEnabled = import.IsEnabled = remove.IsEnabled = !transfer;
            status.Text = installed ? IntentText("学习组件已安装 · 独立更新，默认关闭", "Learning component installed · updated separately, disabled by default") : IntentText("尚未安装，可选下载后启用。", "Not installed. Download only if you want to use learning.");
            try { if (!installed) status.Text += $"  {_intentComponent.Asset.Bytes / 1048576d:0.0} MB · {IntentComponentService.Architecture}"; } catch (Exception ex) { status.Text += " " + ex.Message; }
        }
        async Task Refresh()
        {
            if (!loaded || refreshing || transfer || !_intentComponent.Installed) return;
            refreshing = true;
            try
            {
                var state = await _intentComponent.SendAsync(new("status")); if (!loaded) return;
                status.Text = IntentText("学习组件已安装 · 后台引擎已连接", "Learning component installed · background engine connected");
                busy = state.Busy;
                background.IsEnabled = !busy && !state.BackgroundLearning;
                foreach (var b in new[] { recordScroll, recordGesture, train, observe, hardware }) b.IsEnabled = !busy;
                protect.IsEnabled = state.Eligible && !busy;
                report.Text = state.Message; backend.Text = IntentText("实际推理后端：", "Actual inference backend: ") + state.Backend;
                counts.Text = IntentText("本地样本：", "Local samples: ") + $"{state.Scrolls} / {state.Gestures} / {state.Unknown} " + IntentText("（滚动 / 有意手势 / 未标注）", "(scroll / gesture / unlabeled)");
                var c = state.Control; var now = DateTimeOffset.UtcNow;
                mode.Text = c.Mode == IntentMode.Off || now >= c.ExpiresUtc ? IntentText("当前：关闭", "Current mode: off") :
                    now < c.StartsUtc ? IntentText("开始倒计时：", "Starting in: ") + $"{Math.Ceiling((c.StartsUtc - now).TotalSeconds)} s" :
                    c.Recording ? (c.Mode == IntentMode.RecordScroll ? IntentText("正在录制滚动：", "Recording scrolling: ") : IntentText("正在录制有意手势：", "Recording gestures: ")) + $"{Math.Ceiling((c.ExpiresUtc - now).TotalSeconds)} s" :
                    c.Mode == IntentMode.BackgroundLearn ? IntentText("当前：后台学习 · 不拦截 · 待确认样本不会自动当成答案", "Current mode: background learning · no blocking · confirmed labels only") :
                    c.Mode == IntentMode.Observe ? IntentText("当前：观察，不拦截动作", "Current mode: observing, actions still execute") : IntentText("当前：双指智能关闭保护", "Current mode: protecting two-finger Smart Close");
                var selected = SelectedId(); updating = true; samples.Items.Clear();
                foreach (var s in state.Samples)
                {
                    var item = new ListViewItem { Tag = s.Id, Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"{s.CreatedUtc.ToLocalTime():HH:mm:ss} · {(s.Label == IntentLabel.Scroll ? IntentText("滚动", "Scroll") : s.Label == IntentLabel.Gesture ? IntentText("有意手势", "Gesture") : IntentText("未标注", "Unlabeled"))} · {s.Candidate ?? "—"} · {s.Score?.ToString("0.000") ?? "—"} · {s.Backend ?? "—"}" } };
                    samples.Items.Add(item); if (s.Id == selected) samples.SelectedItem = item;
                }
                updating = false; SelectionState();
            }
            catch (Exception ex) { if (loaded) status.Text = ex.Message; }
            finally { refreshing = false; updating = false; }
        }
        async Task Command(IntentHostRequest request)
        {
            try { await _intentComponent.SendAsync(request); await Refresh(); }
            catch (Exception ex) { report.Text = ex.Message; }
        }
        samples.SelectionChanged += async (_, _) =>
        {
            SelectionState(); if (updating) return; var id = SelectedId(); selectedTrace = null; DrawTrace(); if (id == null) return;
            try { var response = await _intentComponent.SendAsync(new("trace", SampleId: id)); if (SelectedId() == id) { selectedTrace = response.Sample; DrawTrace(); } }
            catch (Exception ex) { report.Text = ex.Message; }
        };
        recordScroll.Click += async (_, _) => await Command(new("mode", IntentMode.RecordScroll));
        background.Click += async (_, _) => await Command(new("mode", IntentMode.BackgroundLearn));
        recordGesture.Click += async (_, _) => await Command(new("mode", IntentMode.RecordGesture));
        stop.Click += async (_, _) => await Command(new("mode", IntentMode.Off));
        train.Click += async (_, _) => await Command(new("train"));
        observe.Click += async (_, _) => await Command(new("mode", IntentMode.Observe));
        protect.Click += async (_, _) => await Command(new("mode", IntentMode.ProtectSmartClose));
        hardware.Click += async (_, _) => await Command(new("hardware"));
        labelScroll.Click += async (_, _) => await Command(new("label", SampleId: SelectedId(), Label: IntentLabel.Scroll));
        labelGesture.Click += async (_, _) => await Command(new("label", SampleId: SelectedId(), Label: IntentLabel.Gesture));
        unlabel.Click += async (_, _) => await Command(new("label", SampleId: SelectedId()));
        delete.Click += async (_, _) => await Command(new("delete", SampleId: SelectedId()));
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
            catch (Exception ex) { status.Text = ex.Message; }
            finally { transfer = false; download.IsEnabled = import.IsEnabled = remove.IsEnabled = true; cancel.Visibility = progress.Visibility = Visibility.Collapsed; await Refresh(); }
        }
        download.Click += async (_, _) => await Install(false); import.Click += async (_, _) => await Install(true); cancel.Click += (_, _) => cancellation.Cancel();
        remove.Click += async (_, _) =>
        {
            if (transfer) return; transfer = true;
            try { await _intentComponent.UninstallAsync(); InstalledState(); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { transfer = false; download.IsEnabled = import.IsEnabled = remove.IsEnabled = true; }
        };
        content.Loaded += async (_, _) => { loaded = true; timer.Start(); await Refresh(); };
        content.Unloaded += (_, _) => { loaded = false; timer.Stop(); cancellation.Cancel(); };
        timer.Tick += async (_, _) => await Refresh(); InstalledState(); SelectionState();
        return NewSettingsGroup(IntentText("本地意图学习（可选组件 · 开发者预览）", "Local intent learning (optional · developer preview)"), [content]);
    }
}
