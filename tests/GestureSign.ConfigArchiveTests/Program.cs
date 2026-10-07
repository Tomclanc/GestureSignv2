using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using GestureSign.WinUI;

var root = Path.Combine(Path.GetTempPath(), "GestureSign-archive-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var factory = typeof(LegacyDataStore).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
    [typeof(JsonArray), typeof(JsonArray), typeof(string)], null)!;
var store = (LegacyDataStore)factory.Invoke([new JsonArray(), new JsonArray(), root]);
void Set(string name, string value) => typeof(LegacyDataStore).GetProperty(name)!.SetValue(store, value);
Set("RoamingPath", Path.Combine(root, "config"));
Set("LocalPath", root);
Directory.CreateDirectory(store.RoamingPath);
var actions = Path.Combine(store.RoamingPath, "Actions.gsa");
var gestures = Path.Combine(store.RoamingPath, "Gestures.gest");
var config = Path.Combine(store.RoamingPath, "GestureSign.config");
var expected = new Dictionary<string,string> {
    [actions] = "[{\"Name\":\"快捷键\",\"Actions\":[{\"Name\":\"Copy\",\"GestureName\":\"Right\"}]}]",
    [gestures] = "[{\"Name\":\"Right\",\"PointPatterns\":[{\"Points\":[[{\"X\":10,\"Y\":20},{\"X\":30,\"Y\":20}]]}]}]",
    [config] = "<configuration><appSettings><add key=\"CultureName\" value=\"en-US\" /></appSettings></configuration>"
};
foreach (var pair in expected) File.WriteAllText(pair.Key, pair.Value);
Set("ActionsPath", actions); Set("GesturesPath", gestures); Set("ConfigPath", config);
var backup = store.CreateBackup();
foreach (var path in expected.Keys) File.WriteAllText(path, "changed");
store.RestoreArchive(backup);
foreach (var pair in expected) if (File.ReadAllText(pair.Key) != pair.Value) throw new Exception("Roundtrip changed " + pair.Key);
Console.WriteLine("PASS: real V2 backup/restore roundtrip preserves actions, gesture paths and English configuration.");
if (args.Length != 0) {
    // The published user sample has 59 rules and 30 gesture definitions.
    store.RestoreArchive(args[0]);
    var apps = JsonNode.Parse(File.ReadAllText(actions))!.AsArray();
    var count = apps.Sum(app => (app?["Actions"] as JsonArray)?.Count ?? 0);
    var gestureCount = JsonNode.Parse(File.ReadAllText(gestures))!.AsArray().Count;
    if (count != 59 || gestureCount != 30) throw new Exception($"Sample changed: {count} rules, {gestureCount} gestures.");
    if (File.ReadAllText(config) != expected[config]) throw new Exception("Sample import unexpectedly replaced global options.");
    Console.WriteLine("PASS: RestoreArchive imports all 59 sample rules and 30 gesture definitions, retaining global options.");
}