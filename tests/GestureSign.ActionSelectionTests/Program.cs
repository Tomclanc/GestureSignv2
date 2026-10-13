using System.Reflection;
using System.Text.Json.Nodes;
using GestureSign.WinUI;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
LegacyAction Make(string name, string gesture) => new() {
    Name = name, GestureName = gesture,
    Source = new JsonObject { ["Name"] = name, ["GestureName"] = gesture, ["Commands"] = new JsonArray() }
};
var old = Make("Same name", "Down");
var owner = new LegacyApplication { Name = "App", MatchUsing = 2, MatchString = "app.exe", Actions = [old] };
var current = old with { Source = (JsonObject)old.Source.DeepClone() };
var currentOwner = owner with { Actions = [current] };
Check(ReferenceEquals(ActionSelection.Resolve([owner], owner, old), old), "Live selection remains valid");
Check(ReferenceEquals(ActionSelection.Resolve([currentOwner], owner, old), current), "First click after full reload resolves automatically");
var other = Make("Same name", "Up");
Check(ReferenceEquals(ActionSelection.Resolve([currentOwner with { Actions = [other, current] }], owner, old), current), "Inserted action does not redirect to its list index");
Check(ActionSelection.Resolve([currentOwner with { Actions = [other] }], owner, old) is null, "Deleted action must not target same-name action");
Check(ActionSelection.Resolve([currentOwner with { MatchString = "other.exe" }], owner, old) is null, "Do not cross application scopes");
Check(ActionSelection.Resolve([currentOwner with { Actions = [current, current with { Source = (JsonObject)current.Source.DeepClone() }] }], owner, old) is null, "Ambiguous duplicate must not choose arbitrarily");
Check(ActionSelection.Resolve([currentOwner, currentOwner], owner, old) is null, "Ambiguous application must not choose arbitrarily");

var root = Path.Combine(Path.GetTempPath(), "GestureSign-selection-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    var factory = typeof(LegacyDataStore).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
        [typeof(JsonArray), typeof(JsonArray), typeof(string)], null)!;
    var store = (LegacyDataStore)factory.Invoke([new JsonArray(owner.Source), new JsonArray(), root]);
    typeof(LegacyDataStore).GetProperty("RoamingPath")!.SetValue(store, root);
    typeof(LegacyDataStore).GetProperty("Applications")!.SetValue(store, new[] { owner });
    var applications = store.Applications;
    store.UpdateOption("LastUpdateCheckUtc", "2026-10-13T00:00:00Z");
    store.ReloadOptions();
    Check(ReferenceEquals(store.Applications, applications) && ReferenceEquals(store.Applications[0].Actions[0].Source, old.Source),
        "Startup update check preserves the original action bindings");
    store.UpdateOption("CheckForUpdates", "False");
    store.ReloadOptions();
    Check(!store.Options.CheckForUpdates, "Settings still refresh from disk");
    Check(ReferenceEquals(ActionSelection.Resolve(store.Applications, owner, old), old), "Option save preserves first-click command selection");
} finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS: {checks} action selection checks.");
