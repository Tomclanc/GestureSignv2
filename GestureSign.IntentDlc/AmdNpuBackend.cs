using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GestureSign.IntentLearning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace GestureSign.IntentDlc;
internal static class AmdNpuBackend
{
    private static readonly object Gate = new();
    public static bool Registered { get; private set; }
    private static string _providerKey = "bundled";
    public static long Runs, Corrections;
    public static double MaxRawError;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetShortPathName(string path, System.Text.StringBuilder result, uint length); public static void Register(string? installedLibrary = null)
    {
        lock (Gate)
        {
            if (Registered) return;

            string dir = Path.Combine(AppContext.BaseDirectory, "AMD");
            string library = Path.Combine(dir, "onnxruntime_vitisai_ep.dll");
            if (installedLibrary != null)
            {
                // Use the certified package already installed on this computer.
                // Relocation avoids the AMD compiler VFS failure under WindowsApps.
                string source = Path.GetDirectoryName(installedLibrary)!;
                _providerKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..16];
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GestureSign V2", "IntentDlc", "amd-provider", _providerKey);
                Directory.CreateDirectory(dir);
                foreach (string file in Directory.EnumerateFiles(source, "*.dll"))
                {
                    string target = Path.Combine(dir, Path.GetFileName(file));
                    if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(file).Length)
                    {
                        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.Copy(file, temporary); File.Move(temporary, target, true); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
                library = Path.Combine(dir, Path.GetFileName(installedLibrary));
            }
            if (!File.Exists(library)) return;
            try
            {
                if (!SetDllDirectory(dir) || AddDllDirectory(dir) == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll"));
                OrtEnv.Instance().RegisterExecutionProviderLibrary("VitisAIExecutionProvider", library);
                Registered = true;
            }
            catch (Exception ex) { HardwareInference.DiagnosticTrace?.Invoke("AMD registration failed: " + ex); }
        }
    }
    public static InferenceSession Create(IntentModel model, OrtEpDevice device)
    {
        byte[] bytes = AmdNpuOnnx.Export(model);
        string key = Convert.ToHexString(SHA256.HashData(bytes))[..16];
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestureSign V2", "IntentDlc", "amd-cache", "hybrid-v1", _providerKey, key);
        Directory.CreateDirectory(cache); Directory.CreateDirectory(Path.Combine(cache, "compiled"));
        var shortPath = new System.Text.StringBuilder(1024); if (GetShortPathName(cache, shortPath, 1024) > 0) cache = shortPath.ToString(); string path = Path.Combine(cache, "linear.onnx");
        File.WriteAllBytes(path, bytes);
        using var options = new SessionOptions {
            EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = 1, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL
        };
        options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
        if (Environment.GetEnvironmentVariable("GESTURESIGN_NPU_PROFILE") == "1")
        {
            options.ProfileOutputPathPrefix = Path.Combine(cache, "profile");
            options.EnableProfiling = true;
        }
        options.AppendExecutionProvider(OrtEnv.Instance(), [device], new Dictionary<string,string> {
            ["cache_dir"] = cache.Replace((char)92, (char)47), ["cache_key"] = "compiled", ["enable_cache_file_io_in_mem"] = "0"
        });
        return new InferenceSession(path, options);
    }
    public static float Run(InferenceSession session, IntentModel model, float[] features)
    {
        var normalized = features.Select((v, i) => (v - model.Mean[i]) / model.Scale[i]).ToArray();
        var input = NamedOnnxValue.CreateFromTensor("features", new DenseTensor<float>(normalized, [1,16,1,1]));
        using var output = session.Run([input]);
        float logit = output.First().AsTensor<float>().First();
        if (!float.IsFinite(logit)) throw new InvalidDataException("AMD NPU returned a non-finite logit.");
        float raw = (float)(1 / (1 + Math.Exp(-logit)));
        float reference = model.Score(features);
        double error = Math.Abs(raw - reference);
        Runs++;
        MaxRawError = Math.Max(MaxRawError, error);
        // Always publish the CPU reference to preserve every caller threshold and FP32 precision.
        // The NPU executes the linear core; this is explicitly labelled hybrid.
        if (error > 0.002) Corrections++;
        if (error > 0.05) throw new InvalidDataException("AMD NPU raw numerical check exceeded 0.05; switching backend.");
        return reference;
    }
}
