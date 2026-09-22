using GestureSign.Foundation.Intent;
using GestureSign.IntentLearning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Windows.AI.MachineLearning;

namespace GestureSign.IntentDlc;

internal sealed class HardwareInference : IDisposable
{
    internal static Action<string>? DiagnosticTrace { get; set; }
    private readonly object _sync = new();
    private InferenceSession? _session;
    private IntentModel? _model;
    private string _backend = "未训练";
    private readonly List<string> _diagnostics = [];
    private readonly Queue<(string Name, Func<InferenceSession> Create)> _fallbacks = new();
    public string Status { get { lock (_sync) return _backend + Environment.NewLine + string.Join(Environment.NewLine, _diagnostics); } }
    public bool Eligible { get { lock (_sync) return _model?.EligibleForProtection == true; } }
    internal static bool IsSupportedGpu(uint vendorId, string? vendor) =>
        vendorId is 0x10de or 0x1002 or 0x8086 or 0x5143 or 0x17cb ||
        new[] { "NVIDIA", "Advanced Micro Devices", "AMD", "Intel", "Qualcomm" }.Any(name => vendor?.Contains(name, StringComparison.OrdinalIgnoreCase) == true);

    public async Task LoadAsync(IntentModel model, bool installProviders, string? testVendor = null)
    {
        DiagnosticTrace?.Invoke("Inference: validating model");
        model.Validate();
        if (installProviders)
        {
            try { await ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync(); }
            catch (Exception ex) { lock (_sync) _diagnostics.Add("硬件组件准备失败，将尝试现有后端：" + ex.Message); }
        }
        else
        {
            // Register only already-installed EPs. Startup/ordinary inference never downloads.
            try
            {
                DiagnosticTrace?.Invoke("Inference: creating ORT environment");
                var env = OrtEnv.Instance();
                DiagnosticTrace?.Invoke("Inference: enumerating installed provider catalog");
                foreach (var provider in ExecutionProviderCatalog.GetDefault().FindAllProviders())
                {
                    if (string.IsNullOrEmpty(provider.LibraryPath)) continue;
                    try { env.RegisterExecutionProviderLibrary(provider.Name, provider.LibraryPath); } catch { }
                }
            }
            catch (Exception ex) { lock (_sync) _diagnostics.Add("无法枚举硬件组件：" + ex.Message); }
        }
        await Task.Run(() =>
        {
            lock (_sync)
            {
                _session?.Dispose(); _session = null; _model = model;
                _fallbacks.Clear();
                var bytes = IntentOnnx.Export(model);
                DiagnosticTrace?.Invoke("Inference: enumerating hardware devices");
                try
                {
                    var env = OrtEnv.Instance();
                    var devices = env.GetEpDevices();
                    foreach (var kind in new[] { OrtHardwareDeviceType.NPU, OrtHardwareDeviceType.GPU })
                        foreach (var device in devices.Where(d => d.HardwareDevice.Type == kind && (testVendor == null || d.HardwareDevice.Vendor.Contains(testVendor, StringComparison.OrdinalIgnoreCase))))
                        {
                            // Software/anonymous adapters can be enumerated as GPU on headless systems.
                            // Do not initialize native DirectML on an unverified adapter or assume index 0 is hardware.
                            if (kind == OrtHardwareDeviceType.GPU && !IsSupportedGpu(device.HardwareDevice.VendorId, device.HardwareDevice.Vendor))
                            { _diagnostics.Add($"跳过未确认的 GPU：vendor=0x{device.HardwareDevice.VendorId:X} {device.HardwareDevice.Vendor}"); continue; }
                            _fallbacks.Enqueue(($"{kind} · {device.EpName} · {device.HardwareDevice.Vendor}", () =>
                            {
                                using var options = Options(accelerated: true);
                                options.AppendExecutionProvider(env, [device], new Dictionary<string, string>());
                                return new InferenceSession(bytes, options);
                            }));
                        }
                }
                catch (Exception ex) { _diagnostics.Add("硬件设备枚举失败：" + ex.Message); }
                // No identifiable accelerator: go straight to CPU, never probe an arbitrary adapter 0.
                _fallbacks.Enqueue(("CPU · ONNX Runtime", () =>
                {
                    using var options = Options(accelerated: false); return new InferenceSession(bytes, options);
                }));
                Advance();
                DiagnosticTrace?.Invoke("Inference: selected " + _backend);
            }
        });
    }

    // Used both at initialization and after a runtime failure; failed devices are not retried per gesture.
    private void Advance()
    {
        while (_fallbacks.TryDequeue(out var backend))
        {
            try
            {
                DiagnosticTrace?.Invoke("Inference: initializing " + backend.Name);
                var session = backend.Create();
                if (Accept(session, _model!)) { _session = session; _backend = backend.Name; return; }
            }
            catch (Exception ex) { _diagnostics.Add(backend.Name + " 不可用：" + ex.Message); }
        }
        _backend = "CPU · 本地分类器";
    }

    private static SessionOptions Options(bool accelerated)
    {
        var options = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        // Never report NPU/GPU when its unsupported operators silently ran on CPU.
        if (accelerated) options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
        return options;
    }
    private static bool Accept(InferenceSession session, IntentModel model)
    {
        try
        {
            foreach (var offset in new[] { -1f, 0f, 1f })
            {
                var input = model.Mean.Select((x, i) => x + offset * model.Scale[i]).ToArray();
                float score = Run(session, input);
                if (!float.IsFinite(score) || Math.Abs(score - model.Score(input)) > 0.002) throw new InvalidDataException("Backend numerical check failed.");
            }
            return true;
        }
        catch { session.Dispose(); throw; }
    }
    private static float Run(InferenceSession session, float[] features)
    {
        var input = NamedOnnxValue.CreateFromTensor("features", new DenseTensor<float>(features, [1, IntentFeatures.Count]));
        using var output = session.Run([input]);
        return output.First().AsTensor<float>().First();
    }
    public IntentPrediction Predict(float[] features)
    {
        lock (_sync)
        {
            if (_model == null) return new(0, _backend, "No trained model");
            if (features.Length != IntentFeatures.Count || features.Any(v => !float.IsFinite(v))) return new(0, _backend, "Invalid feature vector");
            while (_session != null)
            {
                try
                {
                    float score = Run(_session, features);
                    if (!float.IsFinite(score) || score < 0 || score > 1) throw new InvalidDataException("Invalid runtime score.");
                    return new(score, _backend);
                }
                catch (Exception ex)
                {
                    _session.Dispose(); _session = null;
                    _diagnostics.Add("运行中后端失败：" + ex.Message);
                    Advance();
                }
            }
            return new(_model.Score(features), _backend);
        }
    }
    public void Dispose() { lock (_sync) { _session?.Dispose(); _session = null; } }
}
