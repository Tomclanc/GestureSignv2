using GestureSign.Foundation.Intent;
using System.Security.Cryptography;
using System.Text;

namespace GestureSign.IntentLearning;

public static class BackgroundLearningPolicy
{
    public static bool HasEnoughLabels(IEnumerable<IntentSample> source)
    {
        var samples = source.GroupBy(s => s.Id).Select(g => g.Last()).ToArray();
        return new[] { IntentLabel.Scroll, IntentLabel.Gesture }.All(label =>
            samples.Count(s => s.Label == label) >= 40 && samples.Where(s => s.Label == label).Select(s => s.Session).Distinct().Count() >= 4);
    }
    // Predictions and unlabeled traces cannot trigger or supply automatic training.
    public static string Fingerprint(IEnumerable<IntentSample> source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", source.Where(s => s.Label != IntentLabel.Unknown).OrderBy(s => s.Id).Select(s => $"{s.Id}:{s.Session}:{s.Label}")))));
}
