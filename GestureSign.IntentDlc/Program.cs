using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.InteropServices;
using GestureSign.Foundation.Intent;
using GestureSign.IntentLearning;

namespace GestureSign.IntentDlc;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            try { await RuntimeSelfTest.RunAsync(args); return 0; }
            catch (Exception ex) { try { File.WriteAllText(args.Last(), ex.ToString()); } catch { } return 1; }
        }
        // Optional background engine: never open a second settings window.
        if (!args.Contains("--serve")) return 0;
        int index = Array.IndexOf(args, "--daemon-pid");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int pid)) return 2;
        Directory.CreateDirectory(IntentFiles.Root);
        FileStream singleton;
        try { singleton = new FileStream(Path.Combine(IntentFiles.Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 0; }
        using (singleton)
        using (var daemon = Process.GetProcessById(pid))
        using (var host = new IntentHost()) await host.RunAsync(daemon);
        return 0;
    }
}

internal sealed class IntentHost : IDisposable
{
    private readonly string _root;
    private readonly string _pipe;
    private readonly HardwareInference _inference = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _sync = new();
    private IntentControl _control = new();
    private IntentModel? _model;
    private bool _busy;
    private IntentPreferences _preferences = new();
    private DateTimeOffset _nextTrainingCheck;
    private string _attemptedLabels = "";
    private readonly Func<bool> _isIdle;
    private readonly Func<DateTimeOffset> _now;
    private string _message = "可开启后台学习，日常使用时收集轨迹。有空在待确认样本中标注滚动或有意手势，样本充足后空闲自动训练。";
    private string _backend = "未训练";
    public IntentHost(string? root = null, string? pipe = null, Func<bool>? isIdle = null, Func<DateTimeOffset>? now = null)
    { _root = root ?? IntentFiles.Root; _pipe = pipe ?? IntentFiles.PipeName; _isIdle = isIdle ?? IsComputerIdle; _now = now ?? (() => DateTimeOffset.UtcNow); }
    private string ModelPath => Path.Combine(_root, "model.json");
    private string ControlPath => Path.Combine(_root, "control.json");
    private string SamplesPath => Path.Combine(_root, "samples");
    private string PreferencesPath => Path.Combine(_root, "preferences.json");
    private string TrainingFingerprintPath => Path.Combine(_root, "trained-labels.txt");
    private string SamplePath(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid sample ID."); return Path.Combine(SamplesPath, id + ".json"); }

    public async Task RunAsync(Process daemon)
    {
        try { if (File.Exists(PreferencesPath)) _preferences = IntentFiles.Read<IntentPreferences>(PreferencesPath) ?? new(); } catch { }
        try { if (File.Exists(ModelPath) && File.Exists(TrainingFingerprintPath)) _attemptedLabels = File.ReadAllText(TrainingFingerprintPath); } catch { }
        SetMode(IntentMode.Off);
        var commands = ServeAsync(true); var predictions = ServeAsync(false);
        if (File.Exists(ModelPath)) StartJob(async () => await LoadAsync(IntentFiles.Read<IntentModel>(ModelPath) ?? throw new InvalidDataException("模型文件为空。"), false));
        else if (_preferences.BackgroundLearning) SetMode(IntentMode.BackgroundLearn);
        try
        {
            while (!_stop.IsCancellationRequested && !daemon.HasExited)
            {
                lock (_sync)
                {
                    if (_control.Mode is IntentMode.Observe or IntentMode.ProtectSmartClose or IntentMode.BackgroundLearn)
                    { _control.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(15); IntentFiles.Write(ControlPath, _control); }
                    else if (_control.Recording && DateTimeOffset.UtcNow >= _control.ExpiresUtc) SetMode(IntentMode.Off);
                }
                TryAutomaticTraining();
                await Task.Delay(2000, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { SetMode(IntentMode.Off); _stop.Cancel(); }
        await Task.WhenAny(Task.WhenAll(commands, predictions), Task.Delay(1000));
    }
    private void SetMode(IntentMode mode)
    {
        lock (_sync)
        {
            bool recording = mode is IntentMode.RecordScroll or IntentMode.RecordGesture;
            _control = new IntentControl { Mode = mode, StartsUtc = DateTimeOffset.UtcNow.AddSeconds(recording ? 3 : 0), ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(recording ? 48 : 15) };
            IntentFiles.Write(ControlPath, _control);
        }
    }
    private void StartJob(Func<Task> action)
    {
        lock (_sync) { if (_busy) throw new InvalidOperationException("正在处理，请稍后。"); SetMode(IntentMode.Off); _busy = true; _message = "正在处理，请稍候…"; }
        _ = Task.Run(async () =>
        {
            try { await action(); }
            catch (Exception ex) { lock (_sync) _message = ex.Message; }
            finally { lock (_sync) { _busy = false; if (_preferences.BackgroundLearning && !_stop.IsCancellationRequested) SetMode(IntentMode.BackgroundLearn); } }
        });
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    private static bool IsComputerIdle()
    {
        var input = new LastInput { Size = 8 };
        return GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Tick) >= 120000;
    }
    internal void TryAutomaticTraining()
    {
        lock (_sync)
        {
            if (!_preferences.BackgroundLearning || _busy || _now() < _nextTrainingCheck) return;
            _nextTrainingCheck = _now().AddMinutes(1);
            if (!_isIdle()) return;
            var library = ReadLibrary();
            if (!BackgroundLearningPolicy.HasEnoughLabels(library)) return;
            var fingerprint = BackgroundLearningPolicy.Fingerprint(library);
            if (fingerprint == _attemptedLabels) return;
            _attemptedLabels = fingerprint;
            _nextTrainingCheck = _now().AddMinutes(5);
            StartJob(() => TrainAsync(library));
        }
    }
    private async Task TrainAsync(List<IntentSample> library)
    {
        var model = IntentModel.Train(library); await LoadAsync(model, false);
        IntentFiles.Write(ModelPath, model); File.WriteAllBytes(Path.Combine(_root, "model.onnx"), IntentOnnx.Export(model));
        _attemptedLabels = BackgroundLearningPolicy.Fingerprint(library);
        File.WriteAllText(TrainingFingerprintPath, _attemptedLabels);
    }
    private async Task LoadAsync(IntentModel model, bool install)
    {
        model.Validate(); await _inference.LoadAsync(model, install);
        lock (_sync) { _model = model; _message = model.Report; _backend = _inference.Status; }
    }
    private List<IntentSample> ReadLibrary()
    {
        var result = new List<IntentSample>();
        if (!Directory.Exists(SamplesPath)) return result;
        foreach (var file in Directory.EnumerateFiles(SamplesPath, "*.json").Take(2000))
        {
            try { if (new FileInfo(file).Length <= 500_000 && IntentFiles.Read<IntentSample>(file) is { } sample) { IntentFeatures.Extract(sample); result.Add(sample); } }
            catch { }
        }
        return result;
    }
    private IntentHostResponse Handle(IntentHostRequest request)
    {
        lock (_sync)
        {
            if (_busy && request.Command is not ("status" or "stop") && !(request.Command == "mode" && request.Mode == IntentMode.Off)) throw new InvalidOperationException("训练或硬件初始化正在进行，请稍后。");
            switch (request.Command)
            {
                case "status": break;
                case "mode":
                    if (!Enum.IsDefined(request.Mode)) throw new InvalidDataException("Invalid mode.");
                    if (request.Mode == IntentMode.ProtectSmartClose && _model?.EligibleForProtection != true) throw new InvalidOperationException("请先训练并通过留出验证。");
                    _preferences.BackgroundLearning = request.Mode == IntentMode.BackgroundLearn;
                    IntentFiles.Write(PreferencesPath, _preferences);
                    SetMode(request.Mode); break;
                case "train":
                    StartJob(() => TrainAsync(ReadLibrary())); break;
                case "hardware":
                    var current = _model ?? throw new InvalidOperationException("先采样并训练模型，再准备硬件后端。");
                    StartJob(() => LoadAsync(current, true)); break;
                case "label":
                case "delete":
                    if (request.SampleId == null || !Enum.IsDefined(request.Label)) throw new InvalidDataException("Invalid sample request.");
                    SetMode(IntentMode.Off); var path = SamplePath(request.SampleId);
                    var previous = File.Exists(path) ? IntentFiles.Read<IntentSample>(path) : null;
                    bool labelsChanged = request.Command == "delete" ? previous?.Label is IntentLabel.Scroll or IntentLabel.Gesture : previous?.Label != request.Label;
                    if (request.Command == "delete") File.Delete(path);
                    else { var sample = previous ?? throw new IOException("样本不存在。"); sample.Label = request.Label; IntentFiles.Write(path, sample); }
                    if (labelsChanged && _model != null) { _model.ValidationScrolls = 0; IntentFiles.Write(ModelPath, _model); }
                    if (_preferences.BackgroundLearning) SetMode(IntentMode.BackgroundLearn);
                    _message = _preferences.BackgroundLearning ? "已确认。样本充足后，在电脑空闲时自动训练；未确认样本不参与训练。" : "标注已更改，请重新训练。"; break;
                case "trace": return new IntentHostResponse { Sample = IntentFiles.Read<IntentSample>(SamplePath(request.SampleId ?? "")) };
                case "stop": _preferences.BackgroundLearning = false; IntentFiles.Write(PreferencesPath, _preferences); SetMode(IntentMode.Off); _ = Task.Delay(100).ContinueWith(_ => _stop.Cancel()); break;
                default: throw new InvalidDataException("Unsupported command.");
            }
        }
        var library = request.Command == "status" ? ReadLibrary() : [];
        lock (_sync) return new IntentHostResponse
        {
            BackgroundLearning = _preferences.BackgroundLearning, Busy = _busy, Eligible = !_busy && _model?.EligibleForProtection == true, Message = _message, Backend = _backend, Control = _control,
            Scrolls = library.Count(s => s.Label == IntentLabel.Scroll), Gestures = library.Count(s => s.Label == IntentLabel.Gesture), Unknown = library.Count(s => s.Label == IntentLabel.Unknown),
            Samples = library.OrderByDescending(s => _preferences.BackgroundLearning && s.Label == IntentLabel.Unknown && !string.IsNullOrEmpty(s.Candidate)).ThenByDescending(s => s.CreatedUtc).Take(100).Select(s => new IntentSampleSummary(s.Id, s.CreatedUtc, s.Label, s.Candidate, s.Prediction?.GestureScore, s.Prediction?.Backend, s.Blocked)).ToArray()
        };
    }
    private async Task ServeAsync(bool commands)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipe + (commands ? ".control" : ""), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(commands ? 5000 : 250);
                using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(deadline.Token); if (line == null || line.Length > 8192) continue;
                string result;
                if (commands)
                {
                    try { result = JsonSerializer.Serialize(Handle(JsonSerializer.Deserialize<IntentHostRequest>(line) ?? throw new InvalidDataException())); }
                    catch (Exception ex) { result = JsonSerializer.Serialize(new IntentHostResponse { Error = ex.Message }); }
                }
                else
                {
                    var request = JsonSerializer.Deserialize<IntentRequest>(line); if (request?.Features == null) continue;
                    var prediction = _inference.Predict(request.Features); result = JsonSerializer.Serialize(prediction);
                    lock (_sync) _backend = prediction.Backend;
                }
                await writer.WriteLineAsync(result.AsMemory(), deadline.Token);
            }
            catch (Exception) when (!_stop.IsCancellationRequested) { await Task.Delay(20); }
            catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() { _stop.Cancel(); }
}
