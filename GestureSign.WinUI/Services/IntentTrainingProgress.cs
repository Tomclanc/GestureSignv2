using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GestureSign.Foundation.Intent;
namespace GestureSign.WinUI.Services;
internal sealed record IntentLabelProgress(int Samples, int Sessions)
{
    public int MissingSamples => Math.Max(0, 40 - Samples);
    public int MissingSessions => Math.Max(0, 4 - Sessions);
    public bool Ready => MissingSamples == 0 && MissingSessions == 0;
}
internal sealed record IntentTrainingProgress(IntentLabelProgress Scroll, IntentLabelProgress Gesture)
{
    public bool Ready => Scroll.Ready && Gesture.Ready;
    // Match host validation and trainer deduplication; never use the last-100 review list.
    public static IntentTrainingProgress Read(string directory)
    {
        var samples = new List<IntentSample>();
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Take(2000))
            {
                try
                {
                    if (new FileInfo(file).Length > 500_000) continue;
                    var sample = IntentFiles.Read<IntentSample>(file);
                    if (sample == null) continue;
                    IntentFeatures.Extract(sample);
                    samples.Add(sample);
                }
                catch { /* Invalid or incompletely written samples do not count. */ }
            }
        return FromSamples(samples);
    }
    internal static IntentTrainingProgress FromSamples(IEnumerable<IntentSample> source)
    {
        var samples = source.Where(s => s.Label is IntentLabel.Scroll or IntentLabel.Gesture)
            .GroupBy(s => s.Id).Select(g => g.Last()).ToArray();
        IntentLabelProgress Count(IntentLabel label)
        {
            var group = samples.Where(s => s.Label == label && !string.IsNullOrWhiteSpace(s.Session)).ToArray();
            return new(group.Length, group.Select(s => s.Session).Distinct().Count());
        }
        return new(Count(IntentLabel.Scroll), Count(IntentLabel.Gesture));
    }
}
