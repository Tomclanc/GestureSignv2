using GestureSign.Foundation.Intent;
var root = Path.Combine(Path.GetTempPath(), "GestureSign-LocationTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value) { if (!value) throw new Exception("Location check failed: " + (checks + 1)); checks++; }
try {
    Check(!IntentComponentLocation.IsPackaged);
    Check(IntentComponentLocation.ApplicationRoot(root) == root);
    Check(IntentComponentLocation.ApplicationRoot(Path.Combine(root, "Backend")) == root);
    Check(IntentComponentLocation.Resolve(root) == IntentComponentLocation.UserDirectory);
    var component = IntentComponentLocation.ProgramDirectory(root);
    IntentComponentLocation.CheckWritable(component);
    Check(!Directory.EnumerateFiles(component).Any());
    File.WriteAllText(Path.Combine(component, "component.json"), "{}");
    Check(IntentComponentLocation.UsesProgramDirectory(root));
    Check(IntentComponentLocation.Resolve(root) == component);
    Check(IntentComponentLocation.Resolve(Path.Combine(root, "Backend")) == component);
    Console.WriteLine($"PASS: {checks} component location checks.");
} finally { Directory.Delete(root, true); }
