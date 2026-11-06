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
string MakeArchive(string filename, params (string Name, string Text)[] entries)
{
    var path = Path.Combine(root, filename);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
    return path;
}
void RejectWithoutChanges(string archive)
{
    var before = File.ReadAllBytes(actions);
    try { store.RestoreArchive(archive); throw new Exception("Invalid archive accepted"); }
    catch (InvalidDataException) { }
    catch (System.Text.Json.JsonException) { }
    if (!before.SequenceEqual(File.ReadAllBytes(actions))) throw new Exception("Validation modified live config");
}
RejectWithoutChanges(MakeArchive("invalid.ges", ("Actions.gsa", "not JSON")));
RejectWithoutChanges(MakeArchive("duplicate.ges", ("Actions.gsa", "[]"), ("nested/Actions.gsa", "[]")));
RejectWithoutChanges(MakeArchive("empty.ges", ("other.txt", "x")));
var beforeRollback = File.ReadAllBytes(actions);
var originalGestures = File.ReadAllBytes(gestures);
File.Delete(gestures); Directory.CreateDirectory(gestures);
try
{
    try { store.RestoreArchive(MakeArchive("rollback.ges", ("Actions.gsa", "[]"), ("Gestures.gest", "[]"))); throw new Exception("Expected write failure"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    if (!beforeRollback.SequenceEqual(File.ReadAllBytes(actions))) throw new Exception("Rollback failed");
}
finally { Directory.Delete(gestures); File.WriteAllBytes(gestures, originalGestures); }
var legacy = MakeArchive("legacy.ges", ("Actions.act", "[{\"$type\":\"GestureSign.Applications.GlobalApplication, GestureSign\",\"Actions\":[{\"Name\":\"Back\",\"GestureName\":\"Left\",\"PluginClass\":\"HotKey\",\"ActionSettings\":\"{}\"}]}]"));
var preview = store.PreviewArchive(legacy);
if (preview.Actions != 1) throw new Exception("Legacy preview lost action");
store.RestoreArchive(legacy);
if (JsonNode.Parse(File.ReadAllText(actions))![0]!["Actions"]![0]!["Commands"]![0]!["CommandSettings"]!.GetValue<string>() != "{}") throw new Exception("Legacy conversion lost command");
if (!File.Exists(store.LastImportBackupPath)) throw new Exception("Missing backup");
Console.WriteLine("PASS: invalid, duplicate and empty archives rejected; write failure rolled back; legacy commands converted with preview and backup.");
