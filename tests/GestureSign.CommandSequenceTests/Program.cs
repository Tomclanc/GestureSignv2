using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json.Nodes;
using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Plugins;
using GestureSign.WinUI;
using ManagedWinapi.Windows;
using GAction = GestureSign.Common.Applications.Action;

class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    [STAThread]
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "GestureSign-sequence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app = new JsonObject { ["Name"] = "Test", ["Actions"] = new JsonArray() };
            var ctor = typeof(LegacyDataStore).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(JsonArray), typeof(JsonArray), typeof(string) }, null);
            var store = (LegacyDataStore)ctor.Invoke(new object[] { new JsonArray(app), new JsonArray(), root });
            foreach (var pair in new[] { ("RoamingPath", root), ("LocalPath", root), ("ActionsPath", Path.Combine(root, "Actions.gsa")), ("GesturesPath", Path.Combine(root, "Gestures.gest")), ("ConfigPath", Path.Combine(root, "GestureSign.config")) })
                typeof(LegacyDataStore).GetProperty(pair.Item1).SetValue(store, pair.Item2);
            store.AddAction(new LegacyApplication { Source = app }, "Test", "Down", 0, 0);
            var action = new LegacyAction { Source = app["Actions"][0].AsObject() };
            store.AddCommand(action, "A", "Test", "A");
            store.AddCommand(action, "Delay", "GestureSign.CorePlugins.Delay.Delay", "{\"WaitType\":0,\"Timeout\":150}");
            store.AddCommand(action, "B", "Test", "B");
            var commands = action.Source["Commands"].AsArray();
            Check(commands.Count == 3, "Adding commands overwrites previous entries");
            var b = new LegacyCommand { Source = commands[2].AsObject() };
            store.MoveCommand(action, b, -1);
            Check(commands[1]["Name"].GetValue<string>() == "B", "Move up failed");
            store.MoveCommand(action, b, 1);
            store.MoveCommand(action, b, 1);
            Check(commands[2]["Name"].GetValue<string>() == "B", "Move down/boundary failed");
            var backup = store.CreateBackup();
            File.WriteAllText(store.ActionsPath, "[]"); store.RestoreArchive(backup);
            Check(JsonNode.Parse(File.ReadAllText(store.ActionsPath))[0]["Actions"][0]["Commands"].AsArray().Count == 3, "Backup lost command sequence");
            store.DeleteCommand(action, b);
            Check(commands.Count == 2 && commands[0]["Name"].GetValue<string>() == "A", "Delete removed wrong command");
        }
        finally { Directory.Delete(root, true); }

        // Exercise the real asynchronous backend with a hidden window and real Delay plugin.
        using var window = new System.Windows.Forms.Form();
        typeof(ApplicationManager).GetProperty("CaptureWindow").SetValue(ApplicationManager.Instance, new SystemWindow(window.Handle));
        var manager = PluginManager.Instance;
        var recorder = new Recorder();
        typeof(PluginManager).GetField("_Plugins", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, new List<IPluginInfo> {
            new PluginInfo(recorder, "Test", "test.dll"),
            new PluginInfo(new GestureSign.CorePlugins.Delay.Delay(), "GestureSign.CorePlugins.Delay.Delay", "GestureSign.CorePlugins.dll") });
        Command Record(string label, bool enabled = true) => new() { Name = label, PluginClass = "Test", PluginFilename = "test.dll", CommandSettings = label, IsEnabled = enabled };
        var rule = new GAction { Name = "Sequence", GestureName = "Down", ActivateWindow = false, Commands = new ICommand[] {
            Record("A"), Record("disabled", false),
            new Command { Name = "Delay", PluginClass = "GestureSign.CorePlugins.Delay.Delay", PluginFilename = "GestureSign.CorePlugins.dll", CommandSettings = "{\"WaitType\":0,\"Timeout\":150}", IsEnabled = true }, Record("B") } };
        int events = 0; manager.GestureActionExecuted += (_, _) => events++;
        void Execute() => manager.ExecuteAction(new List<IAction> { rule }, CaptureMode.Normal, Devices.Mouse, new List<int> { 0 }, new List<Point> { new(0, 0) }, new List<List<Point>> { new() { new(0, 0), new(0, 100) } });
        Execute(); Execute();
        var task = (Task)typeof(PluginManager).GetField("_lastActionTask", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        Check(task.Wait(TimeSpan.FromSeconds(15)), "Execution timed out");
        Check(recorder.Labels.SequenceEqual(new[] { "A", "B", "A", "B" }), "Commands skipped, reordered, or sequences overlapped");
        Check((recorder.Times[1] - recorder.Times[0]).TotalMilliseconds >= 140 && (recorder.Times[3] - recorder.Times[2]).TotalMilliseconds >= 140, "Delay was not honored");
        Check(events == 2, "Action completion emitted more than once per sequence");
        Console.WriteLine($"PASS: {checks} command-sequence checks (persistence, reorder, backup, deletion, backend order, disabled skip, real delay, queued execution, completion).");
    }
}
class Recorder : IPlugin
{
    string label;
    public List<string> Labels = new(); public List<DateTime> Times = new();
    public string Name => "Recorder"; public string Category => "Test"; public string Description => "Test";
    public bool IsAction => true; public object GUI => null; public bool ActivateWindowDefault => false; public object Icon => null;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string value) { label = value; return true; }
    public string Serialize() => label;
    public bool Gestured(PointInfo info) { Labels.Add(label); Times.Add(DateTime.UtcNow); return true; }
}
