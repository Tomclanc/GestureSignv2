using GestureSign.Foundation.Components;
using GestureSign.Foundation.Configuration;
using GestureSign.Foundation.Input;
using GestureSign.Foundation.Surface;

var tests = new (string Name, Action Run)[]
{
    ("application/global fallback", TestFallbackLifecycle),
    ("complete-trajectory live hint", TestLivePreview),
    ("release/cancel/single-contact residue", TestCancelAndResidue),
    ("visual generation race", TestVisualGenerationRace),
    ("continuous volume/scroll direction", TestContinuousMotionContract),
    ("capture recognize execute pipeline", TestCaptureExecutionPipeline),
    ("stale visual generation isolation", TestStaleVisualGenerationIsolation),
    ("configuration migration", TestConfigurationMigration),
    ("Kando migration", TestKandoMigration)
};

foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"PASS {tests.Length}/{tests.Length} regression groups");

static void TestFallbackLifecycle()
{
    var session = StartSession();
    Assert(session.UpdatePreview("Right", "App action", 3) == LivePreviewTransition.Show);
    Assert(session.FallbackActionName == "App action");
    Assert(session.UpdatePreview("Right", "Global action", 3) == LivePreviewTransition.Show);
}

static void TestLivePreview()
{
    var session = StartSession();
    Assert(session.UpdatePreview("L", "Back", 2) == LivePreviewTransition.Show);
    Assert(session.UpdatePreview("L", "Back", 3) == LivePreviewTransition.None);
    Assert(session.InvalidatePreview() == LivePreviewTransition.Clear);
    Assert(session.VisibleActionName is null);
}

static void TestCancelAndResidue()
{
    var session = StartSession();
    session.UpdatePreview("R", "Forward", 2);
    var generation = session.Cancel();
    Assert(session.State == CaptureSessionState.Canceled);
    Assert(session.FallbackActionName is null && session.FallbackPointCount == 0);
    Assert(generation == 0);
    Assert(!session.Accept(1));
}

static void TestVisualGenerationRace()
{
    var session = StartSession();
    var lifecycle = new VisualCaptureLifecycle();
    var generation = lifecycle.Start();
    Assert(session.AttachVisualGeneration(generation));
    Assert(lifecycle.TryMarkDisplayed(generation));
    Assert(session.BeginRecognition() == generation);
    lifecycle.Invalidate(generation);
    Assert(!session.IsVisualFrameCurrent(generation));
    Assert(lifecycle.ShouldHideForEndedGeneration(generation));
}

static void TestContinuousMotionContract()
{
    var session = StartSession();
    Assert(session.RequiredContactCount == 2);
    Assert(session.UpdatePreview("Up", "Volume up", 4) == LivePreviewTransition.Show);
    Assert(session.FallbackGestureName == "Up");
    Assert(session.UpdatePreview("Left", "Scroll left", 5) == LivePreviewTransition.Show);
    Assert(session.FallbackActionName == "Scroll left");
}

static void TestCaptureExecutionPipeline()
{
    // Device-independent end-to-end orchestration: input acceptance, live
    // preview, recognition, execution and completion must be monotonic.
    var session = StartSession(2);
    var visuals = new VisualCaptureLifecycle();
    var generation = visuals.Start();
    Assert(session.AttachVisualGeneration(generation));
    Assert(visuals.TryMarkDisplayed(generation));
    Assert(session.UpdatePreview("Right", "Next tab", 6) == LivePreviewTransition.Show);
    Assert(session.State == CaptureSessionState.Previewing);
    var recognitionGeneration = session.BeginRecognition();
    Assert(recognitionGeneration == generation);
    Assert(session.State == CaptureSessionState.Recognizing);
    Assert(session.BeginExecution());
    Assert(session.State == CaptureSessionState.Executing);
    session.Complete();
    Assert(session.State == CaptureSessionState.Completed);
    Assert(session.VisibleActionName is null && session.VisualGeneration == 0);
    Assert(!session.BeginExecution());
}

static void TestStaleVisualGenerationIsolation()
{
    var visuals = new VisualCaptureLifecycle();
    var first = StartSession();
    var firstGeneration = visuals.Start();
    Assert(first.AttachVisualGeneration(firstGeneration));
    Assert(visuals.TryMarkDisplayed(firstGeneration));
    Assert(first.Cancel() == firstGeneration);
    visuals.Invalidate(firstGeneration);

    // A new capture must not accept delayed frames from the previous one.
    var second = StartSession();
    var secondGeneration = visuals.Start();
    Assert(secondGeneration != firstGeneration);
    Assert(second.AttachVisualGeneration(secondGeneration));
    Assert(!second.IsVisualFrameCurrent(firstGeneration));
    Assert(second.IsVisualFrameCurrent(secondGeneration));
    Assert(!visuals.TryMarkDisplayed(firstGeneration));
    Assert(visuals.TryMarkDisplayed(secondGeneration));
}

static void TestConfigurationMigration()
{
    var root = Path.Combine(Path.GetTempPath(), "GestureSignRegression", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "legacy");
    var target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "settings.json"), "legacy");
    Assert(ConfigurationMigrationService.CopyMissingTree(source, target) == 1);
    Assert(File.Exists(Path.Combine(target, "settings.json")));
    Directory.Delete(root, true);
}

static void TestKandoMigration()
{
    var root = Path.Combine(Path.GetTempPath(), "GestureSignRegression", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "legacy");
    var target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "menus.json"), "menus");
    var selected = KandoMigrationService.FindLegacyInstallation(new[] { source }, target, false, false, _ => true);
    Assert(selected == source);
    KandoMigrationService.MergePersistentUserData(source, target);
    Assert(File.Exists(Path.Combine(target, "menus.json")));
    Directory.Delete(root, true);
}

static CaptureSession StartSession(int contacts = 2)
{
    var session = new CaptureSession();
    Assert(session.Accept(contacts));
    return session;
}

static void Assert(bool condition)
{
    if (!condition) throw new InvalidOperationException("Regression assertion failed.");
}
