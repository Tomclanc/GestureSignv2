using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using GestureSign.Foundation.Intent;
using GestureSign.IntentDlc;

AppDomain.CurrentDomain.UnhandledException += (_, error) => File.WriteAllText("intent-host-failure.log", error.ExceptionObject.ToString());
HardwareInference.DiagnosticTrace = Console.WriteLine;
var root = Path.Combine(Path.GetTempPath(), "GestureSign-HostTest-" + Guid.NewGuid().ToString("N"));
var pipeName = "GestureSign-HostTest-" + Guid.NewGuid().ToString("N");
bool idle = false; var now = DateTimeOffset.UtcNow;
using var host = new IntentHost(root, pipeName, () => idle, () => now);
var running = host.RunAsync(Process.GetCurrentProcess());
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine($"PASS host check {checks}"); }
Check(new uint[] { 0x10de, 0x1002, 0x8086, 0x5143, 0x17cb }.All(id => HardwareInference.IsSupportedGpu(id, "")) && !HardwareInference.IsSupportedGpu(0x1414, "") && !HardwareInference.IsSupportedGpu(0, ""), "Anonymous/software GPU accepted or known vendor rejected.");
async Task<IntentHostResponse> Send(IntentHostRequest request)
{
    using var timeout = new CancellationTokenSource(5000);
    using var pipe = new NamedPipeClientStream(".", pipeName + ".control", PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(timeout.Token);
    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; using var reader = new StreamReader(pipe, leaveOpen: true);
    await writer.WriteLineAsync(JsonSerializer.Serialize(request));
    return JsonSerializer.Deserialize<IntentHostResponse>((await reader.ReadLineAsync(timeout.Token))!)!;
}
Check((await Send(new("status"))).Control.Mode == IntentMode.Off, "Host must start disabled.");
Check((await Send(new("mode", IntentMode.ProtectSmartClose))).Error != null, "Untrained model authorized protection.");
for (int i = 0; i < 600 && (await Send(new("status"))).Busy; i++) await Task.Delay(100);
var recorded = await Send(new("mode", IntentMode.RecordScroll));
Check(recorded.Control.StartsUtc > DateTimeOffset.UtcNow && recorded.Control.Recording, "Recording countdown missing.");
await Send(new("mode", IntentMode.Observe));
var lease = (await Send(new("status"))).Control.ExpiresUtc;
await Task.Delay(2200);
Check((await Send(new("status"))).Control.ExpiresUtc > lease, "Host failed to renew lease without a settings window.");
var sample = new IntentSample { Session = "test", Frames = Enumerable.Range(0, 8).Select(i => new IntentFrame(i * 15, [new(1, i * 10, i * 5), new(2, 60 + i * 10, i * 5)])).ToArray() };
IntentFiles.Write(Path.Combine(root, "samples", sample.Id + ".json"), sample);
Check((await Send(new("status"))).Unknown == 1, "Library count missing.");
Check((await Send(new("trace", SampleId: sample.Id))).Sample?.Id == sample.Id, "Trace retrieval failed.");
await Send(new("label", SampleId: sample.Id, Label: IntentLabel.Scroll));
Check((await Send(new("status"))).Scrolls == 1, "Relabel did not persist.");
Check((await Send(new("trace", SampleId: "../model"))).Error != null, "Unsafe sample ID accepted.");
await Send(new("train"));
for (int i = 0; i < 30 && (await Send(new("status"))).Busy; i++) await Task.Delay(50);
var failure = await Send(new("status"));
Check(!failure.Busy && !failure.Eligible && failure.Message.Contains("40"), "Insufficient training data not reported.");
await Send(new("delete", SampleId: sample.Id)); Check((await Send(new("status"))).Samples.Length == 0, "Deletion failed.");
await Send(new("mode", IntentMode.BackgroundLearn));
var background = await Send(new("status"));
Check(background.BackgroundLearning && background.Control.Mode == IntentMode.BackgroundLearn && !background.Control.Recording, "Background learning did not start passively.");
Check(IntentFiles.Read<IntentPreferences>(Path.Combine(root, "preferences.json"))?.BackgroundLearning == true, "Background preference was not persisted.");
await Task.Delay(2200);
Check((await Send(new("status"))).Control.ExpiresUtc > background.Control.ExpiresUtc, "Background lease was not renewed.");
for (int session = 0; session < 4; session++) for (int i = 0; i < 10; i++) foreach (var label in new[] { IntentLabel.Scroll, IntentLabel.Gesture })
{
    var confirmed = new IntentSample { Session = "session-" + session, Label = label, Frames = sample.Frames };
    IntentFiles.Write(Path.Combine(root, "samples", confirmed.Id + ".json"), confirmed);
}
now = now.AddMinutes(2); host.TryAutomaticTraining();
Check(!File.Exists(Path.Combine(root, "model.json")), "Training started while user was active.");
idle = true; now = now.AddMinutes(2); host.TryAutomaticTraining();
for (int i = 0; i < 200 && (await Send(new("status"))).Busy; i++) await Task.Delay(50);
var trained = await Send(new("status"));
Check(File.Exists(Path.Combine(root, "model.json")) && trained.Control.Mode == IntentMode.BackgroundLearn, "Idle training failed to save and resume passive collection: " + trained.Message);
await Send(new("mode", IntentMode.ExperimentalVeto));
var combined = await Send(new("status"));
Check(combined.BackgroundLearning && combined.Control.Mode == IntentMode.ExperimentalVeto, "Veto disabled background learning.");
await Send(new("background-off"));
var vetoOnly = await Send(new("status"));
Check(!vetoOnly.BackgroundLearning && vetoOnly.Control.Mode == IntentMode.ExperimentalVeto, "Disabling learning disabled veto.");
await Send(new("background-on"));
Check((await Send(new("status"))).Control.Mode == IntentMode.ExperimentalVeto, "Enabling learning disabled veto.");
await Send(new("train"));
for (int i = 0; i < 600 && (await Send(new("status"))).Busy; i++) await Task.Delay(100);
var afterCombinedTraining = await Send(new("status"));
Check(!afterCombinedTraining.Busy && afterCombinedTraining.BackgroundLearning && afterCombinedTraining.Control.Mode == IntentMode.ExperimentalVeto, "Training failed to restore both switches.");
await Send(new("veto-off"));
var learningOnly = await Send(new("status"));
Check(learningOnly.BackgroundLearning && learningOnly.Control.Mode == IntentMode.BackgroundLearn, "Disabling veto disabled learning.");
var modelTime = File.GetLastWriteTimeUtc(Path.Combine(root, "model.json"));
now = now.AddMinutes(6); host.TryAutomaticTraining();
Check(!(await Send(new("status"))).Busy && File.GetLastWriteTimeUtc(Path.Combine(root, "model.json")) == modelTime, "Unchanged labels triggered repeated training.");
Check(!trained.Eligible, "Ambiguous data automatically authorized protection.");
var modelBeforeUnlabeledDelete = File.ReadAllText(Path.Combine(root, "model.json"));
var unlabeled = new IntentSample { Session = "unlabeled", Frames = sample.Frames };
IntentFiles.Write(Path.Combine(root, "samples", unlabeled.Id + ".json"), unlabeled);
await Send(new("delete", SampleId: unlabeled.Id));
Check(File.ReadAllText(Path.Combine(root, "model.json")) == modelBeforeUnlabeledDelete, "Deleting an unlabeled trace invalidated confirmed training.");
await Send(new("mode", IntentMode.Off));
Check(IntentFiles.Read<IntentPreferences>(Path.Combine(root, "preferences.json"))?.BackgroundLearning == false, "Stop did not disable automatic resume.");
await Send(new("stop")); await running.WaitAsync(TimeSpan.FromSeconds(3));
Check(IntentFiles.Read<IntentControl>(Path.Combine(root, "control.json"))?.Mode == IntentMode.Off, "Stop left capture active.");
IntentFiles.Write(Path.Combine(root, "preferences.json"), new IntentPreferences { BackgroundLearning = true });
using (var resumedHost = new IntentHost(root, pipeName, () => false))
{
    var resumedRun = resumedHost.RunAsync(Process.GetCurrentProcess());
    for (int i = 0; i < 200 && (await Send(new("status"))).Busy; i++) await Task.Delay(50);
    var resumed = await Send(new("status"));
    Check(resumed.BackgroundLearning && resumed.Control.Mode == IntentMode.BackgroundLearn, "Explicit background preference failed to resume after restart.");
    await Send(new("stop")); await resumedRun.WaitAsync(TimeSpan.FromSeconds(3));
}
Console.WriteLine($"PASS: {checks} headless host, integrated settings protocol, lease and lifecycle checks.");
