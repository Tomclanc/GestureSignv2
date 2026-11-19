using GestureSign.WinUI;
using System.Xml.Linq;

const string sid = "S-1-5-21-100-200-300-1001";
XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
XElement Task(string? enabled = null, params XElement[] triggers) => new(ns + "Task",
    new XElement(ns + "Settings", enabled is null ? null : new XElement(ns + "Enabled", enabled)),
    new XElement(ns + "Triggers", triggers));
XElement Logon(string? enabled = null, string? user = sid) => new(ns + "LogonTrigger",
    enabled is null ? null : new XElement(ns + "Enabled", enabled),
    user is null ? null : new XElement(ns + "UserId", user));
var cases = new (string Name, string? Xml, bool Expected)[] {
    ("current user", Task("true", Logon("true")).ToString(), true),
    ("disabled task", Task("false", Logon("true")).ToString(), false),
    ("disabled logon trigger", Task("true", Logon("false")).ToString(), false),
    ("other user's logon", Task("true", Logon("true", sid + "9")).ToString(), false),
    ("any user trigger", Task("true", Logon("true", null)).ToString(), true),
    ("schema defaults", Task(null, Logon()).ToString(), true),
    ("numeric disabled", Task("0", Logon("1")).ToString(), false),
    ("one enabled trigger", Task("1", Logon("0"), Logon("1")).ToString(), true),
    ("manual only", Task("true").ToString(), false),
    ("time trigger only", Task("true", new XElement(ns + "TimeTrigger")).ToString(), false),
    ("invalid boolean", Task("invalid", Logon()).ToString(), false),
    ("malformed", "<Task", false),
    ("missing", null, false)
};
foreach (var test in cases)
    if (StartupTaskState.IsEnabledForLogon(test.Xml, sid) != test.Expected)
        throw new Exception(test.Name);
if (StartupTaskState.IsEnabledForLogon(Task("true", Logon()).ToString(), ""))
    throw new Exception("Missing user identity must not report enabled.");
Console.WriteLine($"PASS: {cases.Length + 1} startup task state cases.");
var accountTask = Task("true", Logon("true", @"PC\Alice")).ToString();
if (!StartupTaskState.IsEnabledForLogon(accountTask, sid, account => account == @"PC\Alice" ? sid : null))
    throw new Exception("Account-name trigger must resolve to current SID.");
if (StartupTaskState.IsEnabledForLogon(accountTask, sid, _ => sid + "9"))
    throw new Exception("Another account must not be accepted.");
if (StartupTaskState.IsEnabledForLogon(accountTask, sid, _ => throw new Exception("Account unavailable")))
    throw new Exception("Unresolvable account must not be accepted.");
Console.WriteLine("PASS: 3 account-name trigger cases.");
