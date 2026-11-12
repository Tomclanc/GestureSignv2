using GestureSign.Shared;
using System.Text.Json;

var folder = Path.Combine(Path.GetTempPath(), "脚本 O'Brien & $HOME `test");
var commands = new List<string>();
foreach (var extension in new[] { ".ps1", ".cmd", ".bat", ".py", ".ahk" })
{
    var path = Path.Combine(folder, "demo script" + extension);
    var command = ScriptInvocation.PowerShellCommand(path);
    if (!command.StartsWith("Set-Location -LiteralPath '") || !command.Contains("O''Brien") || !command.Contains("$HOME `test"))
        throw new Exception("Literal path quoting lost");
    if (extension == ".py" && !command.Contains("& 'python' ")) throw new Exception("Missing Python interpreter");
    if (extension == ".ahk" && !command.Contains("Start-Process -FilePath ")) throw new Exception("AHK association lost");
    if (extension is ".ps1" or ".cmd" or ".bat" && !command.Contains(Environment.NewLine + "& '"))
        throw new Exception("Script is not invoked literally");
    commands.Add(command);
}
foreach (var path in new[] { "relative.ps1", Path.Combine(folder, "bad.exe") })
{
    try { ScriptInvocation.PowerShellCommand(path); throw new Exception("Invalid script input accepted"); }
    catch (ArgumentException) { }
}
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(commands));
Console.WriteLine("PASS: five script types, literal paths with spaces/quotes/Unicode/metacharacters, working directory and invalid input. No scripts executed.");
