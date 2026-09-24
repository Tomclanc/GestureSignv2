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
async Task Trace(TouchPadIntentBridge bridge)
{
    bridge.Begin([new(1, new Point(10, 10)), new(2, new Point(70, 10))]);
    for (int i = 1; i < 9; i++) { await Task.Delay(12); bridge.Add([new(1, new Point(10 + i * 5, 10 + i * 10)), new(2, new Point(70 + i * 5, 10 + i * 10))]); }
    bridge.End();
}
async Task Respond(float score)
{
    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    await pipe.WaitForConnectionAsync();
    using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    var request = JsonSerializer.Deserialize<IntentRequest>(await reader.ReadLineAsync());
    Check(request.Features.Length == IntentFeatures.Count, "Wrong inference feature count.");
    await writer.WriteLineAsync(JsonSerializer.Serialize(new IntentPrediction(score, "test")));
}
IntentSample Stroke(bool fullL = false) => new() { Frames = Enumerable.Range(0, 10).Select(i => new IntentFrame(i * 20, [new IntentPoint(1, fullL && i > 4 ? (i - 4) * 20 : 0, Math.Min(i, fullL ? 4 : 9) * 20), new IntentPoint(2, 60 + (fullL && i > 4 ? (i - 4) * 20 : 0), Math.Min(i, fullL ? 4 : 9) * 20)])).ToArray() };
var context = new IntentScrollContext();
void Burst() { context.Reset(); for (int i = 0; i < 3; i++) Check(!context.Add(Stroke(), 1, i * 300, i * 300 + 180), "Scroll context triggered too early."); }
Burst(); Check(context.Add(Stroke(), 1, 900, 1080), "Rapid continuation was missed.");
Burst(); Check(!context.Add(Stroke(true), 1, 900, 1080), "Deliberate L was treated as a scroll continuation.");
Burst(); Check(!context.Add(Stroke(), 2, 900, 1080), "Context leaked to another window.");
Burst(); Check(!context.Add(Stroke(), 1, 1500, 1680), "Context survived a pause.");
context.Reset(); Check(!context.Add(Stroke(), 1, 1800, 1980), "Reset retained scroll history.");
using (var bridge = new TouchPadIntentBridge(root, pipeName))
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
    bridge.Publish(); await Task.Delay(200);
    Check(Directory.GetFiles(Path.Combine(root,"samples"),"*.json").Select(IntentFiles.Read<IntentSample>).Any(s => s.AiVeto && s.Blocked && s.Label == IntentLabel.Unknown), "Veto was not persisted as unlabeled evidence.");
    await Control(IntentMode.Observe); Check(!bridge.ShouldSuppress("L", true, 2), "Observe mode intercepted an action.");
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
