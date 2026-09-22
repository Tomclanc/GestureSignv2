using GestureSign.Foundation.Intent;
using System.Security.Cryptography;
using System.Text;

namespace GestureSign.IntentLearning;

public sealed class IntentModel
{
    public int FeatureVersion { get; set; } = IntentFeatures.Version;
    public float[] Weights { get; set; } = [];
    public float Bias { get; set; }
    public float[] Mean { get; set; } = [];
    public float[] Scale { get; set; } = [];
    public DateTimeOffset TrainedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string[] TrainingSessions { get; set; } = [];
    public string[] ValidationSessions { get; set; } = [];
    public int ValidationScrolls { get; set; }
    public int ValidationGestures { get; set; }
    public int FalseActivations { get; set; }
    public int MissedGestures { get; set; }
    public bool EligibleForProtection => ValidationScrolls >= 10 && ValidationGestures >= 10 && FalseActivations == 0 && (double)MissedGestures / ValidationGestures <= 0.2;
    public string Report => $"留出验证：滚动 {ValidationScrolls} 条，误放行 {FalseActivations}；手势 {ValidationGestures} 条，误拦截 {MissedGestures}。\n" +
        (EligibleForProtection ? "达到实验性拦截门槛；这不是日常准确率保证。" : "暂未达到拦截门槛，请补充多次独立采样。仍可观察评分。") +
        "\n模型评分未做概率校准，0.85 是放行阈值，不代表 85% 的准确率。";

    public void Validate()
    {
        if (FeatureVersion != IntentFeatures.Version || Weights.Length != IntentFeatures.Count || Mean.Length != Weights.Length || Scale.Length != Weights.Length ||
            Weights.Concat(Mean).Concat(Scale).Any(v => !float.IsFinite(v)) || Scale.Any(v => v <= 0) || !float.IsFinite(Bias))
            throw new InvalidDataException("Incompatible or corrupt intent model.");
    }
    public float Score(float[] values)
    {
        Validate();
        if (values.Length != Weights.Length || values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid feature vector.");
        double z = Bias;
        for (int i = 0; i < values.Length; i++) z += Weights[i] * ((values[i] - Mean[i]) / Scale[i]);
        return (float)(1 / (1 + Math.Exp(-Math.Clamp(z, -80, 80))));
    }

    // Stable session split. No trace from a held-out recording session can leak into fitting.
    public static IntentModel Train(IEnumerable<IntentSample> source)
    {
        var samples = source.Where(s => s.Label is IntentLabel.Scroll or IntentLabel.Gesture)
            .GroupBy(s => s.Id).Select(g => g.Last()).ToArray();
        if (samples.Any(s => string.IsNullOrWhiteSpace(s.Session))) throw new InvalidDataException("Samples must belong to recording sessions.");
        foreach (var label in new[] { IntentLabel.Scroll, IntentLabel.Gesture })
            if (samples.Count(s => s.Label == label) < 40 || samples.Where(s => s.Label == label).Select(s => s.Session).Distinct().Count() < 4)
                throw new InvalidOperationException("每类至少 40 条样本、4 次独立采样；每次建议录制 10–20 条。请先收集正常滚动和有意绘制的双指 L。");
        var sessions = samples.Select(s => s.Session).Distinct().OrderBy(s => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))).ToArray();
        var heldOut = new HashSet<string>();
        foreach (var label in new[] { IntentLabel.Scroll, IntentLabel.Gesture })
        {
            foreach (var session in sessions.Where(id => samples.Any(s => s.Session == id && s.Label == label)))
            {
                if (samples.Count(s => heldOut.Contains(s.Session) && s.Label == label) >= 10) break;
                heldOut.Add(session);
            }
        }
        var train = samples.Where(s => !heldOut.Contains(s.Session)).ToArray();
        var validation = samples.Where(s => heldOut.Contains(s.Session)).ToArray();
        if (train.Count(s => s.Label == IntentLabel.Scroll) < 20 || train.Count(s => s.Label == IntentLabel.Gesture) < 20)
            throw new InvalidOperationException("留出完整会话后，训练样本不足。请增加独立采样次数，避免全部样本集中在一次录制。");
        var x = train.Select(IntentFeatures.Extract).ToArray();
        var model = new IntentModel
        {
            Weights = new float[IntentFeatures.Count], Mean = new float[IntentFeatures.Count], Scale = new float[IntentFeatures.Count],
            TrainingSessions = train.Select(s => s.Session).Distinct().ToArray(), ValidationSessions = heldOut.ToArray()
        };
        for (int j = 0; j < IntentFeatures.Count; j++)
        {
            model.Mean[j] = x.Average(v => v[j]);
            model.Scale[j] = (float)Math.Max(0.03, Math.Sqrt(x.Average(v => Math.Pow(v[j] - model.Mean[j], 2))));
        }
        var normalized = x.Select(v => v.Select((a, j) => (a - model.Mean[j]) / model.Scale[j]).ToArray()).ToArray();
        int positives = train.Count(s => s.Label == IntentLabel.Gesture);
        // Balanced L2-regularized logistic regression; all fitting is local and small enough for CPU.
        for (int epoch = 0; epoch < 1000; epoch++)
        {
            double[] gradient = new double[IntentFeatures.Count]; double biasGradient = 0;
            for (int i = 0; i < train.Length; i++)
            {
                double z = model.Bias;
                for (int j = 0; j < gradient.Length; j++) z += model.Weights[j] * normalized[i][j];
                bool positive = train[i].Label == IntentLabel.Gesture;
                double weight = train.Length / (2d * (positive ? positives : train.Length - positives));
                double error = (1 / (1 + Math.Exp(-Math.Clamp(z, -80, 80))) - (positive ? 1 : 0)) * weight;
                biasGradient += error;
                for (int j = 0; j < gradient.Length; j++) gradient[j] += error * normalized[i][j];
            }
            for (int j = 0; j < gradient.Length; j++) model.Weights[j] -= (float)(0.08 * (gradient[j] / train.Length + 0.005 * model.Weights[j]));
            model.Bias -= (float)(0.08 * biasGradient / train.Length);
        }
        foreach (var sample in validation)
        {
            bool allow = model.Score(IntentFeatures.Extract(sample)) >= 0.85;
            if (sample.Label == IntentLabel.Scroll) { model.ValidationScrolls++; if (allow) model.FalseActivations++; }
            else { model.ValidationGestures++; if (!allow) model.MissedGestures++; }
        }
        return model;
    }
}
