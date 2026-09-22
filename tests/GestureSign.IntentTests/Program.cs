using GestureSign.Foundation.Intent;
using GestureSign.IntentLearning;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
IntentSample Sample(bool gesture, string session, int variant = 0)
{
    var frames = Enumerable.Range(0, 31).Select(i =>
    {
        double t = i / 30d;
        // Same approximate L outline, different timing and turn proportions.
        double x = gesture ? Math.Max(0, t - .55) * 220 : Math.Max(0, t - .9) * 160;
        double y = gesture ? Math.Min(t, .55) * 180 : Math.Min(t, .9) * 180;
        x += variant * .05 * t; y += variant * .03 * t;
        return new IntentFrame(i * (gesture ? 22 : 5), [new(1, x, y), new(2, x + 60, y + 4)]);
    }).ToArray();
    return new() { Session = session, Label = gesture ? IntentLabel.Gesture : IntentLabel.Scroll, Frames = frames, PreviousGapMs = gesture ? 1500 : 120 };
}
var original = Sample(true, "a");
var translated = Sample(true, "a");
translated.Frames = translated.Frames.Select(f => f with { Points = f.Points.Select(p => p with { X = p.X * 2 + 100, Y = p.Y * 2 - 200 }).ToArray() }).ToArray();
Check(IntentFeatures.Extract(original).Zip(IntentFeatures.Extract(translated)).All(p => Math.Abs(p.First - p.Second) < .0001), "Features must ignore translation and uniform coordinate scale.");
var fast = Sample(true, "b"); fast.Frames = fast.Frames.Select(f => f with { Milliseconds = f.Milliseconds / 2 }).ToArray();
Check(Math.Abs(IntentFeatures.Extract(original)[0] - IntentFeatures.Extract(fast)[0]) > .3, "Timing information lost.");
var trace = new IntentTrace(); foreach (var f in original.Frames) trace.Add(f.Milliseconds, f.Points);
Check(trace.Finish()?.Length == original.Frames.Length, "Valid trace rejected.");
trace.Add(6000, original.Frames[0].Points); Check(trace.Finish() == null, "Long trace must be rejected, never truncated and trained.");
var changedContacts = new IntentTrace(); foreach (var f in original.Frames) changedContacts.Add(f.Milliseconds, f.Points.Select(p => p with { Contact = p.Contact + (f.Milliseconds > 300 ? 10 : 0) }).ToArray());
Check(changedContacts.Finish() == null, "Contact replacement accepted.");
Check(!new IntentControl { Mode = IntentMode.Observe, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }.Active, "Expired control still active.");
Check(!new IntentControl { Mode = IntentMode.Observe, StartsUtc = DateTimeOffset.UtcNow.AddSeconds(10), ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(20) }.Active, "Countdown ignored.");
Check(!new IntentPrediction(float.NaN, "test").Allows && !new IntentPrediction(1, "test", "timeout").Allows, "Invalid inference must not allow close.");
var dataset = new List<IntentSample>();
for (int session = 0; session < 5; session++) for (int i = 0; i < 12; i++) { dataset.Add(Sample(true, "gesture" + session, i)); dataset.Add(Sample(false, "scroll" + session, i)); }
var model = IntentModel.Train(dataset);
Check(!model.TrainingSessions.Intersect(model.ValidationSessions).Any(), "Validation session leaked into training.");
Check(model.ValidationScrolls >= 10 && model.ValidationGestures >= 10 && model.EligibleForProtection, "Separable synthetic validation failed.");
Check(model.Score(IntentFeatures.Extract(Sample(true, "new", 8))) >= .85 && model.Score(IntentFeatures.Extract(Sample(false, "new", 8))) < .15, "Unseen synthetic trace prediction failed.");
bool rejected = false; try { IntentModel.Train(dataset.Take(10)); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Too-small training corpus accepted.");
var ambiguous = dataset.Select(s => new IntentSample { Session = s.Session, Label = s.Label, Frames = original.Frames, PreviousGapMs = 500 }).ToArray();
Check(!IntentModel.Train(ambiguous).EligibleForProtection, "Indistinguishable data must not authorize protection.");
Check(IntentOnnx.Export(model).Length > 100, "ONNX export empty.");
Check(BackgroundLearningPolicy.HasEnoughLabels(dataset), "Confirmed training corpus rejected.");
var fingerprint = BackgroundLearningPolicy.Fingerprint(dataset);
var unknown = Sample(true, "unknown"); unknown.Label = IntentLabel.Unknown; unknown.Prediction = new(1, "test");
Check(BackgroundLearningPolicy.Fingerprint(dataset.Append(unknown)) == fingerprint, "Prediction/unlabeled trace changed training input.");
Check(!BackgroundLearningPolicy.HasEnoughLabels(Enumerable.Range(0, 100).Select(_ => new IntentSample { Session = Guid.NewGuid().ToString(), Prediction = new(1, "test") })), "Guessed labels authorize training.");
dataset[0].Label = IntentLabel.Scroll;
Check(BackgroundLearningPolicy.Fingerprint(dataset) != fingerprint, "Correction failed to invalidate training fingerprint.");
Console.WriteLine($"PASS: {checks} intent capture/training/validation checks. Synthetic data tests mechanics, not real-world accuracy.");
