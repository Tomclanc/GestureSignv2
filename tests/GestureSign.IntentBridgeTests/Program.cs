using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Text.Json;
using GestureSign.Daemon.Input;
using GestureSign.Foundation.Intent;

var root = Path.Combine(Path.GetTempPath(), "GestureSign-IntentTest-" + Guid.NewGuid().ToString("N"));
var pipeName = "GestureSign-IntentTest-" + Guid.NewGuid().ToString("N");
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Control(IntentMode mode)
{
    IntentFiles.Write(Path.Combine(root, "control.json"), new IntentControl { Mode = mode, StartsUtc = DateTimeOffset.UtcNow.AddSeconds(-1), ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(10) });
    await Task.Delay(650);
}
async Task Trace(TouchPadIntentBridge bridge, int contacts = 2)
{
    bridge.Begin(Enumerable.Range(1, contacts).Select(n => new InputPoint(n, new Point(n * 60, 10))).ToList());
    for (int i = 1; i < 9; i++) { await Task.Delay(12); bridge.Add(Enumerable.Range(1, contacts).Select(n => new InputPoint(n, new Point(n * 60 + i * 5, 10 + i * 10))).ToList()); }
    bridge.End();
}
async Task Respond(float score, int contacts = 2, int protocol = 0)
{
    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    await pipe.WaitForConnectionAsync();
    using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    var request = JsonSerializer.Deserialize<IntentRequest>(await reader.ReadLineAsync());
    Check(request.Features.Length == IntentFeatures.Count, "Wrong inference feature count.");
    Check(request.Contacts == contacts, "Contact count was lost in inference request.");
    await writer.WriteLineAsync(JsonSerializer.Serialize(new IntentPrediction(score, "test", ReviewProtocol: protocol)));
}
IntentSample Stroke(bool fullL = false) => new() { Frames = Enumerable.Range(0, 10).Select(i => new IntentFrame(i * 20, [new IntentPoint(1, fullL && i > 4 ? (i - 4) * 20 : 0, Math.Min(i, fullL ? 4 : 9) * 20), new IntentPoint(2, 60 + (fullL && i > 4 ? (i - 4) * 20 : 0), Math.Min(i, fullL ? 4 : 9) * 20)])).ToArray() };
var context = new IntentScrollContext();
void Burst() { context.Reset(); for (int i = 0; i < 3; i++) Check(!context.Add(Stroke(), 1, i * 300, i * 300 + 180), "Scroll context triggered too early."); }
Burst(); Check(context.Add(Stroke(), 1, 900, 1080), "Rapid continuation was missed.");
Burst(); Check(!context.Add(Stroke(true), 1, 900, 1080), "Deliberate L was treated as a scroll continuation.");
Burst(); Check(!context.Add(Stroke(), 2, 900, 1080), "Context leaked to another window.");
Burst(); Check(!context.Add(Stroke(), 1, 1500, 1680), "Context survived a pause.");
context.Reset(); Check(!context.Add(Stroke(), 1, 1800, 1980), "Reset retained scroll history.");
using (var bridge = new TouchPadIntentBridge(root, pipeName, () => 1))
{
    Check(!bridge.ShouldSuppress("L", true, 2), "Disabled bridge changed behavior.");
    await Control(IntentMode.RecordScroll); await Trace(bridge);
    Check(bridge.ShouldSuppress("L", true, 2), "Recording executed Smart Close."); bridge.Publish();
    for (int i = 0; i < 30 && (!Directory.Exists(Path.Combine(root, "samples")) || Directory.GetFiles(Path.Combine(root, "samples"), "*.json").Length == 0); i++) await Task.Delay(20);
    var saved = IntentFiles.Read<IntentSample>(Directory.GetFiles(Path.Combine(root, "samples"), "*.json").Single());
    Check(saved.Label == IntentLabel.Scroll && saved.Blocked && saved.Candidate == "L", "Capture/label/candidate not persisted.");
    bridge.Begin([new(1, new Point(10, 10)), new(2, new Point(70, 10))]);
    await Control(IntentMode.Off);
    bridge.Add([new(1, new Point(15, 20)), new(2, new Point(75, 20))]); bridge.End();
    Check(bridge.ShouldSuppress("L", true, 2), "Recording expiry mid-gesture allowed an accidental close."); bridge.Publish();
    Check(!bridge.ShouldSuppress("L", true, 2), "Recording latch survived release.");
    await Control(IntentMode.ProtectSmartClose); await Trace(bridge);
    Check(!bridge.ShouldSuppress("Other", false, 2) && !bridge.ShouldSuppress("L", true, 3), "Protection escaped its intended scope.");
    var response = Respond(.95f); Check(!bridge.ShouldSuppress("L", true, 2), "Positive inference blocked."); await response;
    response = Respond(.1f); Check(bridge.ShouldSuppress("L", true, 2), "Scroll inference allowed close."); await response;
    var watch = Stopwatch.StartNew(); Check(bridge.ShouldSuppress("L", true, 2), "Missing DLC did not fail closed while protection enabled.");
    Check(watch.ElapsedMilliseconds < 400, "Inference timeout did not bound input delay.");
    var uiThread = new Thread(() => { SynchronizationContext.SetSynchronizationContext(new NonPumpingContext()); bridge.ShouldSuppress("L", true, 2); }) { IsBackground = true };
    uiThread.Start();
    Check(uiThread.Join(1000), "Inference timeout deadlocked the input/UI synchronization context.");
    bridge.Cancel(); Check(bridge.ShouldSuppress("L", true, 2), "Invalid trace allowed protected close.");
    await Control(IntentMode.ExperimentalVeto); await Trace(bridge);
    Check(!bridge.ShouldSuppress("Other", false, 2) && !bridge.ShouldSuppress("L", true, 3), "Experimental veto escaped Smart Close scope.");
    response = Respond(.96f); Check(!bridge.ShouldSuppress("L", true, 2), "Experimental veto rejected allowed score."); await response;
    response = Respond(.1f); Check(bridge.ShouldSuppress("L", true, 2) && bridge.LastAiVetoReason != null, "Experimental veto did not report rejection."); await response;
    response = Respond(.1f); Check(bridge.ShouldSuppress("Custom", false, 2, freeDraw: true), "Custom drawing bypassed AI review."); await response;
    response = Respond(.1f);
    Check(!bridge.ShouldSuppress("Custom", false, 2, freeDraw: true, strongTemplateMatch: true), "A low model score vetoed a deliberate curved template match.");
    await response;
    response = Respond(.1f);
    Check(bridge.ShouldSuppress("L", true, 2, freeDraw: true, strongTemplateMatch: true), "Strong template evidence bypassed Smart Close protection.");
    await response;
    Check(!bridge.ShouldSuppress("Custom", false, 2, freeDraw: true), "Unavailable model blocked an ordinary drawing.");
    Check(bridge.ShouldSuppress("Custom", false, 2, missingTemplateTurn: true, freeDraw: true, strongTemplateMatch: true), "Strong-match flag bypassed missing sustained template turn.");
    new IntentReviewSettings { TwoFingers = false, ThreeFourFingers = false, SingleFingerCustom = false, ExcludedGestures = ["Custom"] }.Save(root); await Task.Delay(650);
    Check(bridge.ShouldSuppress("Custom", false, 2, missingTemplateTurn: true, freeDraw: true), "Legacy exclusions bypassed mandatory drawing review.");
    new IntentReviewSettings().Save(root); await Task.Delay(650);
    bridge.Publish(); await Task.Delay(200);
    foreach (int contacts in new[] { 1, 3, 4 })
    {
        await Trace(bridge, contacts);
        response = Respond(.1f, contacts, 3);
        Check(bridge.ShouldSuppress("Custom", false, contacts, freeDraw: true, strongTemplateMatch: true), "Two-finger template exemption escaped its contact-count scope."); await response;
        response = Respond(.95f, contacts, 3);
        Check(!bridge.ShouldSuppress("Custom", false, contacts, freeDraw: true), "Multi-contact review rejected positive score."); await response;
        response = Respond(.1f, contacts, 0);
        Check(!bridge.ShouldSuppress("Custom", false, contacts, freeDraw: true), "Legacy DLC pretended to support another contact count."); await response;
    }
    await Trace(bridge);
    Check(!bridge.ShouldSuppress("Unbound", false, 2, missingTemplateTurn: true, freeDraw: true, hasExecutableAction: false), "Unbound candidate was vetoed.");
    Check(bridge.LastAiVetoReason == null, "Unbound candidate generated a notification reason.");
    response = Respond(.1f); bridge.Publish(); await response;
    await Task.Delay(250);
    var unbound = Directory.GetFiles(Path.Combine(root, "samples"), "*.json").Select(p => IntentFiles.Read<IntentSample>(p)).Single(s => s.Candidate == "Unbound");
    Check(!unbound.AiVeto && !unbound.Blocked && unbound.Prediction != null, "Unbound sample lost its score or retained a veto flag.");
    Check(!bridge.ShouldSuppress("TouchPadTipTap.Left", false, 2, missingTemplateTurn: true, freeDraw: true), "TipTap entered drawing review.");
    Check(!bridge.ShouldSuppress("TouchPadEdge.Left", false, 1, missingTemplateTurn: true, freeDraw: true), "Edge entered drawing review.");
    bridge.Publish(); await Task.Delay(200);
    Check(Directory.GetFiles(Path.Combine(root,"samples"),"*.json").Select(IntentFiles.Read<IntentSample>).Any(s => s.AiVeto && s.Blocked && s.Label == IntentLabel.Unknown), "Veto was not persisted as unlabeled evidence.");
    await Control(IntentMode.Observe); Check(!bridge.ShouldSuppress("L", true, 2), "Observe mode intercepted an action.");
    foreach (int contacts in new[] { 1, 2, 3, 4 })
    {
        await Trace(bridge, contacts);
        Check(!bridge.ShouldSuppress("Custom", false, contacts, missingTemplateTurn: true, freeDraw: true), "AI scoring blocked a custom action.");
        response = Respond(.1f, contacts, 3);
        bridge.Publish();
        await response.WaitAsync(TimeSpan.FromSeconds(3));
        IntentSample scored = null;
        for (int i = 0; i < 50 && scored == null; i++)
        {
            await Task.Delay(20);
            scored = Directory.GetFiles(Path.Combine(root, "samples"), "*.json").Select(IntentFiles.Read<IntentSample>)
                .FirstOrDefault(s => s.Candidate == "Custom" && s.Frames[0].Points.Length == contacts && s.Prediction?.GestureScore == .1f && !s.Blocked && !s.AiVeto);
        }
        Check(scored != null, "AI scoring did not persist the ordinary drawing score without a veto.");
    }
    await Control(IntentMode.Off); Check(!bridge.ShouldSuppress("L", true, 2), "Off mode still intercepted.");
    await Control(IntentMode.BackgroundLearn); await Trace(bridge);
    Check(!bridge.ShouldSuppress("L", true, 2), "Background sampling intercepted a gesture."); bridge.Publish();
    await Task.Delay(350);
    var passive = Directory.GetFiles(Path.Combine(root, "samples"), "*.json").Select(IntentFiles.Read<IntentSample>).FirstOrDefault(s => s.Session.StartsWith("background-"));
    Check(passive != null && passive.Label == IntentLabel.Unknown && !passive.Blocked, "Background trace was labeled without confirmation.");
}
Console.WriteLine($"PASS: {checks} daemon bridge / named pipe / recording / timeout checks. Test data: {root}");

namespace GestureSign.Common.Log { internal static class Logging { public static void LogMessage(string text) => Console.WriteLine(text); } }

internal sealed class NonPumpingContext : SynchronizationContext { public override void Post(SendOrPostCallback callback, object state) { } }
