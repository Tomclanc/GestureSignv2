using GestureSign.Foundation.Intent;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Input;

// No ML dependencies in the daemon. The optional DLC owns training and inference.
internal sealed class TouchPadIntentBridge : IDisposable
{
    private IntentControl _control = new();
    private IntentControl _captureControl;
    private IntentTrace _trace;
    private IntentSample _sample;
    private bool _recordingCapture;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _started, _lastEnd = -5000, _gap;
    private readonly IntentWheelContext _wheelContext = new();
    private bool _recentWheel;
    private string _application = "";
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private static string ApplicationName()
    {
        try
        {
            if (!GetCursorPos(out var point)) return "";
            GetWindowThreadProcessId(GetAncestor(WindowFromPoint(point), 2), out var id);
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch { return ""; }
    }
    public void RecordWheel(int delta, bool injected)
    {
        var config = Volatile.Read(ref _control);
        if (injected || !config.Active || config.Recording) return;
        _wheelContext.Record(_windowContext(), _clock.Elapsed.TotalMilliseconds, delta);
    }
    private readonly IntentScrollContext _scrollContext = new();
    private readonly Func<long> _windowContext;
    private long _captureWindow;
    private string _contextSession;
    private bool _scrollContinuation;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    private static long WindowContext()
    {
        if (!GetCursorPos(out var point)) return 0;
        var target = GetAncestor(WindowFromPoint(point), 2);
        return target == IntPtr.Zero ? 0 : HashCode.Combine(target, GetForegroundWindow());
    }
    private double _lastBackgroundSample = -10000;
    private DateTimeOffset _nextStart;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<IntentSample> _queue = Channel.CreateBounded<IntentSample>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly Task _worker;
    private readonly Task _poller;
    private readonly string _dataRoot;
    private readonly string _pipeName;

    public TouchPadIntentBridge(string dataRoot = null, string pipeName = null, Func<long> windowContext = null)
    {
        _windowContext = windowContext ?? WindowContext;
        _dataRoot = dataRoot ?? IntentFiles.Root;
        _pipeName = pipeName ?? IntentFiles.PipeName;
        _poller = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var path = Path.Combine(_dataRoot, "control.json");
                    Volatile.Write(ref _control, File.Exists(path) ? IntentFiles.Read<IntentControl>(path) ?? new() : new());
                    if (dataRoot == null && !_control.Active && DateTimeOffset.UtcNow >= _nextStart)
                    {
                        _nextStart = DateTimeOffset.UtcNow.AddSeconds(30);
                        EnsureBackgroundHost();
                    }
                }
                catch { Volatile.Write(ref _control, new()); }
                try { await Task.Delay(500, _stop.Token); } catch (OperationCanceledException) { break; }
            }
        });
        _worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var sample in _queue.Reader.ReadAllAsync(_stop.Token))
                {
                    try
                    {
                        if (sample.Label == IntentLabel.Unknown && sample.Prediction == null)
                            sample.Prediction = await PredictAsync(IntentFeatures.Extract(sample), 150, _stop.Token);
                        var samplesPath = Path.Combine(_dataRoot, "samples");
                        Directory.CreateDirectory(samplesPath);
                        // Background samples must not crowd out the user's confirmed labels.
                        var old = new DirectoryInfo(samplesPath).GetFiles("*.json").OrderBy(f => f.LastWriteTimeUtc).ToArray();
                        int remove = Math.Max(0, old.Length - 1999);
                        if (remove > 0)
                        {
                            var evict = old.Where(f => { try { return IntentFiles.Read<IntentSample>(f.FullName)?.Label == IntentLabel.Unknown; } catch { return true; } }).Take(remove).ToList();
                            if (sample.Label == IntentLabel.Unknown && remove > evict.Count) continue;
                            foreach (var file in evict.Concat(old.Except(evict)).Take(remove)) file.Delete();
                        }
                        IntentFiles.Write(Path.Combine(samplesPath, sample.Id + ".json"), sample);
                    }
                    catch (Exception ex) { Common.Log.Logging.LogMessage("Intent DLC sample skipped: " + ex.Message); }
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void EnsureBackgroundHost()
    {
        var preferences = Path.Combine(_dataRoot, "preferences.json");
        if (!File.Exists(preferences) || IntentFiles.Read<IntentPreferences>(preferences)?.BackgroundLearning != true) return;
        var component = IntentComponentLocation.Resolve(AppContext.BaseDirectory);
        var manifest = Path.Combine(component, "component.json");
        if (!File.Exists(manifest) || IntentFiles.Read<IntentComponentManifest>(manifest)?.Version != IntentComponentPackage.ComponentVersion) return;
        // A live host holds this lock. Launch only when the previous process has exited.
        try { using var lease = new FileStream(Path.Combine(_dataRoot, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; }
        var executable = Path.Combine(component, "Runtime", "GestureSign.IntentDlc.exe");
        if (!File.Exists(executable)) return;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable) };
        info.ArgumentList.Add("--serve"); info.ArgumentList.Add("--daemon-pid"); info.ArgumentList.Add(Environment.ProcessId.ToString());
        using var process = Process.Start(info);
    }

    public void Begin(System.Collections.Generic.List<InputPoint> points)
    {
        var config = Volatile.Read(ref _control);
        if (!config.Active) { DropTrace(); return; }
        if (_trace == null)
        {
            if (_contextSession != config.Session) { _scrollContext.Reset(); _wheelContext.Reset(); _contextSession = config.Session; }
            _captureWindow = _windowContext(); _scrollContinuation = false;
            _captureControl = config;
            _recordingCapture = config.Recording;
            _started = _clock.Elapsed.TotalMilliseconds;
            _recentWheel = _wheelContext.WasScrolling(_captureWindow, _started);
            _application = _dataRoot == IntentFiles.Root ? ApplicationName() : "test";
            _gap = Math.Clamp(_started - _lastEnd, 0, 5000);
            _trace = new IntentTrace();
            _sample = null;
        }
        Add(points);
    }

    public void Add(System.Collections.Generic.List<InputPoint> points)
    {
        if (_trace == null || points == null) return;
        var current = Volatile.Read(ref _control);
        if (!current.Active || current.Session != _captureControl.Session || current.Mode != _captureControl.Mode) { DropTrace(); return; }
        _trace.Add(_clock.Elapsed.TotalMilliseconds - _started, points.Select(p => new IntentPoint(p.ContactIdentifier, p.Point.X, p.Point.Y)).ToArray());
    }

    public void End()
    {
        _lastEnd = _clock.Elapsed.TotalMilliseconds;
        var current = Volatile.Read(ref _control);
        if (!current.Active || current.Session != _captureControl?.Session || current.Mode != _captureControl?.Mode) { DropTrace(); return; }
        var frames = _trace?.Finish();
        _trace = null;
        if (frames == null || _captureControl == null) return;
        _sample = new IntentSample
        {
            Session = _captureControl.Mode == IntentMode.BackgroundLearn ? "background-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 1800 : _captureControl.Session, Frames = frames, PreviousGapMs = _gap,
            Label = _captureControl.Mode == IntentMode.RecordScroll ? IntentLabel.Scroll :
                    _captureControl.Mode == IntentMode.RecordGesture ? IntentLabel.Gesture : IntentLabel.Unknown
        };
        try
        {
            IntentFeatures.Extract(_sample);
            _sample.ContextReason = $"目标应用：{_application}；" + (_recentWheel ? "手势开始前检测到连续滚动输入（不代表页面已实际移动）。" : "未检测到近期连续滚动输入，结合双指轨迹判断。");
            if (_captureWindow != _windowContext()) _scrollContext.Reset();
            else _scrollContinuation = _scrollContext.Add(_sample, _captureWindow, _started, _lastEnd, _recentWheel);
        }
        catch { _sample = null; _scrollContext.Reset(); }
    }

    public string LastAiVetoReason { get; private set; }
    private bool Veto(string reason)
    {
        LastAiVetoReason = reason;
        if (_sample != null) { _sample.Blocked = true; _sample.AiVeto = true; _sample.ContextReason += " AI 否决：" + reason; }
        return true;
    }
    public bool ShouldSuppress(string candidate, bool smartClose, int contacts, string templateEvidence = null, bool missingTemplateTurn = false)
    {
        LastAiVetoReason = null;
        var current = Volatile.Read(ref _control);
        bool vetoEnabled = current.Mode is IntentMode.ProtectSmartClose or IntentMode.ExperimentalVeto;
        if (_sample != null) _sample.Candidate = candidate;
        // Explicit, short recording sessions never execute traced actions.
        // Latch through release: the recording timer expiring mid-L must not close a window.
        if (_recordingCapture || current.Active && current.Recording) { if (_sample != null) _sample.Blocked = true; return true; }
        if (!current.Active) return false;
        if (_sample != null && templateEvidence != null) _sample.ContextReason += " " + templateEvidence;
        if (smartClose && contacts == 2 && missingTemplateTurn)
        {
            bool blocked = vetoEnabled && (_scrollContinuation || _recentWheel) && _captureWindow == _windowContext();
            if (_sample != null) { _sample.ContextReason += " 双指均缺少模板要求的持续转向。"; _sample.Blocked = blocked; }
            Common.Log.Logging.LogMessage($"Intent template check: MissingSustainedTurn, {templateEvidence}, Mode={current.Mode}, Blocked={blocked}");
            if (blocked) return Veto("连续滚动中，双指均缺少模板要求的持续转向");
        }
        if (smartClose && contacts == 2 && _scrollContinuation && _captureWindow == _windowContext())
        {
            bool block = vetoEnabled;
            if (_sample != null) _sample.ContextReason += " 疑似连续滚动中的智能关闭误触。";
            Common.Log.Logging.LogMessage($"Intent context: Reason=RapidScrollContinuation, Application={_application}, WheelEvidence={_recentWheel}, Candidate={candidate}, Mode={current.Mode}, Blocked={block}");
            if (block) return Veto("疑似连续快速滚动中的智能关闭误触");
        }
        if (!vetoEnabled || !smartClose || contacts != 2) return false;
        if (_sample == null || _captureControl?.Session != current.Session) return Veto("轨迹不完整，未执行智能关闭");
        try
        {
            _sample.Prediction = PredictAsync(IntentFeatures.Extract(_sample), 45, _stop.Token).GetAwaiter().GetResult();
            _sample.Blocked = !_sample.Prediction.Allows;
        }
        catch { _sample.Blocked = true; }
        return _sample.Blocked ? Veto(_sample.Prediction?.Error != null ? "推理暂不可用，未执行智能关闭" : $"模型认为可能是滚动（评分 {_sample.Prediction?.GestureScore:F3}）") : false;
    }

    public void Publish()
    {
        // Keep candidate gestures, but thin routine traffic to at most one trace per 10 seconds.
        if (_sample != null && _captureControl?.Mode == IntentMode.BackgroundLearn)
        {
            if (string.IsNullOrEmpty(_sample.Candidate) && _clock.Elapsed.TotalMilliseconds - _lastBackgroundSample < 10000) _sample = null;
            else _lastBackgroundSample = _clock.Elapsed.TotalMilliseconds;
        }
        if (_sample != null) _queue.Writer.TryWrite(_sample);
        _sample = null;
        _captureControl = null;
        _recordingCapture = false;
    }
    private void DropTrace() { _trace = null; _sample = null; _scrollContinuation = false; _scrollContext.Reset(); }
    public void Cancel() { DropTrace(); _captureControl = null; _recordingCapture = false; }

    private async Task<IntentPrediction> PredictAsync(float[] features, int timeoutMs, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(timeoutMs);
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            // The input thread waits synchronously; never resume on its synchronization context.
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new IntentRequest(features)).AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line == null || line.Length > 8192) throw new IOException("Invalid DLC response.");
            return JsonSerializer.Deserialize<IntentPrediction>(line) ?? throw new IOException("Empty DLC response.");
        }
        catch (Exception ex) { return new IntentPrediction(0, "Unavailable", ex is OperationCanceledException ? "Inference deadline exceeded" : ex.Message); }
    }
    public void Dispose()
    {
        _stop.Cancel(); _queue.Writer.TryComplete(); Cancel();
        // Poller and writer exit asynchronously; input teardown never waits for disk or inference.
    }
}
