using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestureSign.Foundation.Intent;

public enum IntentMode { Off, RecordScroll, RecordGesture, Observe, ProtectSmartClose, BackgroundLearn }
public sealed class IntentPreferences
{
    public bool BackgroundLearning { get; set; }
}
public enum IntentLabel { Unknown, Scroll, Gesture }
public sealed record IntentPoint(int Contact, double X, double Y);
public sealed record IntentFrame(double Milliseconds, IntentPoint[] Points);
public sealed class IntentControl
{
    public int Version { get; set; } = 1;
    public IntentMode Mode { get; set; }
    public string Session { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartsUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool Active => Version == 1 && DateTimeOffset.UtcNow >= StartsUtc && DateTimeOffset.UtcNow < ExpiresUtc && Enum.IsDefined(Mode) && Mode != IntentMode.Off;
    public bool Recording => Mode is IntentMode.RecordScroll or IntentMode.RecordGesture;
}
public sealed class IntentSample
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Session { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public IntentLabel Label { get; set; }
    public IntentFrame[] Frames { get; set; } = [];
    public double PreviousGapMs { get; set; } = 5000;
    public string? Candidate { get; set; }
    public IntentPrediction? Prediction { get; set; }
    public bool Blocked { get; set; }
}
public sealed record IntentRequest(float[] Features);
public sealed record IntentPrediction(float GestureScore, string Backend, string? Error = null)
{
    // Scores are uncalibrated; never describe them as measured accuracy.
    public bool Allows => Error == null && float.IsFinite(GestureScore) && GestureScore >= 0.85f;
}

public static class IntentFiles
{
    // Intentionally separate from roaming/OneDrive configuration.
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign V2", "IntentDlc");
    public static string PipeName => "GestureSign.Intent." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root)))[..20];
    public static string ControlPath => Path.Combine(Root, "control.json");
    public static string SamplesPath => Path.Combine(Root, "samples");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    public static T? Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string SamplePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid sample ID.");
        return Path.Combine(SamplesPath, id + ".json");
    }
}

/// <summary>Time-preserving, bounded capture before the recognizer's distance filtering.</summary>
public sealed class IntentTrace
{
    private readonly List<IntentFrame> _frames = [];
    private bool _overflow;
    public void Add(double milliseconds, IntentPoint[] points)
    {
        if (_overflow || !double.IsFinite(milliseconds) || milliseconds < 0 || points.Length == 0) return;
        if (milliseconds > 5000 || points.Length > 2) { _overflow = true; return; }
        if (_frames.Count > 0 && milliseconds - _frames[^1].Milliseconds < 4) return;
        if (_frames.Count >= 1251) { _overflow = true; return; }
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) { _overflow = true; return; }
        _frames.Add(new IntentFrame(milliseconds, points));
    }
    public IntentFrame[]? Finish()
    {
        if (_overflow) return null;
        var frames = _frames.Where(f => f.Points.Length == 2).ToArray();
        if (frames.Length < 6 || frames[^1].Milliseconds - frames[0].Milliseconds < 30) return null;
        // Changes of contact identity corrupt velocity and must not become training data.
        var ids = frames[0].Points.Select(p => p.Contact).Order().ToArray();
        if (ids.Distinct().Count() != 2 || frames.Any(f => !f.Points.Select(p => p.Contact).Order().SequenceEqual(ids))) return null;
        return frames;
    }
}

public static class IntentFeatures
{
    public const int Version = 1;
    public const int Count = 16;
    public static float[] Extract(IntentSample sample)
    {
        if (sample.Version != 1 || sample.Frames.Length is < 6 or > 1251) throw new InvalidDataException("Unsupported or incomplete trace.");
        var f = sample.Frames;
        if (f.Any(x => x.Points.Length != 2 || !double.IsFinite(x.Milliseconds) || x.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))))
            throw new InvalidDataException("Invalid trace.");
        var x = f.Select(v => v.Points.Average(p => p.X)).ToArray();
        var y = f.Select(v => v.Points.Average(p => p.Y)).ToArray();
        var w = x.Max() - x.Min(); var h = y.Max() - y.Min();
        var scale = Math.Max(1, Math.Sqrt(w * w + h * h));
        var duration = f[^1].Milliseconds - f[0].Milliseconds;
        if (duration < 30 || duration > 5000) throw new InvalidDataException("Invalid duration.");
        var speeds = new List<double>(); var turns = new List<double>();
        double path = 0, absX = 0, absY = 0, early = 0, late = 0, tailPath = 0;
        double previousDx = 0, previousDy = 0, previousLength = 0;
        for (int i = 1; i < f.Length; i++)
        {
            double dt = f[i].Milliseconds - f[i - 1].Milliseconds;
            if (dt <= 0) throw new InvalidDataException("Non-monotonic sample time.");
            double dx = x[i] - x[i - 1], dy = y[i] - y[i - 1], length = Math.Sqrt(dx * dx + dy * dy);
            path += length; absX += Math.Abs(dx); absY += Math.Abs(dy);
            speeds.Add(length / scale / (dt / 1000));
            double t = (f[i].Milliseconds - f[0].Milliseconds) / duration;
            if (t < 0.3) early += length;
            if (t > 0.7) late += length;
            if (t > 0.8) tailPath += length;
            if (length > scale * 0.005 && previousLength > scale * 0.005)
                turns.Add(Math.Acos(Math.Clamp((dx * previousDx + dy * previousDy) / (length * previousLength), -1, 1)) / Math.PI);
            if (length > scale * 0.005) { previousDx = dx; previousDy = dy; previousLength = length; }
        }
        if (path < 1) throw new InvalidDataException("Stationary trace.");
        var mean = speeds.Average();
        var distances = f.Select(v => Math.Sqrt(Math.Pow(v.Points[0].X - v.Points[1].X, 2) + Math.Pow(v.Points[0].Y - v.Points[1].Y, 2))).ToArray();
        double displacement = Math.Sqrt(Math.Pow(x[^1] - x[0], 2) + Math.Pow(y[^1] - y[0], 2));
        double[] values = [
            Math.Log(1 + duration / 100), Math.Min(w, h) / Math.Max(1, Math.Max(w, h)), displacement / path,
            path / scale, absX / path, absY / path,
            (absX - Math.Abs(x[^1] - x[0])) / path, (absY - Math.Abs(y[^1] - y[0])) / path,
            turns.Count == 0 ? 0 : turns.Max(), turns.Count == 0 ? 0 : turns.Sum(),
            Math.Log(1 + mean), Math.Sqrt(speeds.Average(s => Math.Pow(s - mean, 2))) / Math.Max(0.01, mean),
            (late - early) / path, tailPath / path,
            (distances.Max() - distances.Min()) / scale, Math.Log(1 + Math.Clamp(sample.PreviousGapMs, 0, 5000) / 100)
        ];
        if (values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Non-finite features.");
        return values.Select(v => (float)Math.Clamp(v, -50, 50)).ToArray();
    }
}
