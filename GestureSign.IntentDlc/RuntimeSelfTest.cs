using System.Text.Json;
using GestureSign.Foundation.Intent;
using GestureSign.IntentLearning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GestureSign.IntentDlc;

internal static class RuntimeSelfTest
{
    public static async Task RunAsync(string[] args)
    {
        var model = new IntentModel
        {
            Weights = Enumerable.Range(0, IntentFeatures.Count).Select(i => (i - 7) * 0.1f).ToArray(),
            Mean = Enumerable.Range(0, IntentFeatures.Count).Select(i => i * 0.2f).ToArray(),
            Scale = Enumerable.Repeat(0.7f, IntentFeatures.Count).ToArray(), Bias = 0.25f
        };
        if (args.Contains("--actual-model")) model = JsonSerializer.Deserialize<IntentModel>(File.ReadAllText(Path.Combine(IntentFiles.Root, "model.json")))!; using var cpu = new InferenceSession(IntentOnnx.Export(model));
        using var runtime = new HardwareInference();
        string? vendor = args.FirstOrDefault(a => a.StartsWith("--vendor="))?[9..];
        await runtime.LoadAsync(model, false, vendor);
        var rng = new Random(7); double maxError = 0; string backend = "";
        var cases = Enumerable.Range(0,200).Select(_ => model.Mean.Select((v,i) => v + (float)(rng.NextDouble()*4-2)*model.Scale[i]).ToArray()).ToList(); if (args.Contains("--actual-model")) foreach (var file in Directory.EnumerateFiles(Path.Combine(IntentFiles.Root,"samples"), "*.json")) cases.Add(IntentFeatures.Extract(JsonSerializer.Deserialize<IntentSample>(File.ReadAllText(file))!)); for (int i = 0; i < cases.Count; i++)
        {
            var values = cases[i];
            using var output = cpu.Run([NamedOnnxValue.CreateFromTensor("features", new DenseTensor<float>(values, [1, IntentFeatures.Count]))]);
            float cpuValue = output.First().AsTensor<float>().First();
            var actual = runtime.Predict(values); backend = actual.Backend;
            double error = Math.Max(Math.Abs(cpuValue - model.Score(values)), Math.Abs(actual.GestureScore - cpuValue));
            if (actual.Error != null || error > 0.002) throw new Exception("ONNX/runtime parity failed: " + error);
            maxError = Math.Max(maxError, error);
        }
        if (args.Contains("--require-npu") && (!backend.StartsWith("NPU") || AmdNpuBackend.Runs < cases.Count)) throw new Exception("NPU was not exercised: " + runtime.Status); if (vendor != null && !backend.Contains(vendor, StringComparison.OrdinalIgnoreCase)) throw new Exception("Requested vendor was not exercised: " + runtime.Status);
        if (runtime.Predict([float.NaN]).Error == null) throw new Exception("Invalid features accepted.");
        File.WriteAllText(args.Last(), JsonSerializer.Serialize(new { Passed = true, Cases = cases.Count, NpuRuns = AmdNpuBackend.Runs, RawMaxError = AmdNpuBackend.MaxRawError, PrecisionCorrections = AmdNpuBackend.Corrections, ActualBackend = backend, MaxAbsoluteError = maxError, Diagnostics = runtime.Status }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
