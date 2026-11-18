using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GestureSign.WinUI;

internal static class StartupTaskState
{
    // A registered task is not necessarily enabled for this user's next logon.
    internal static bool IsEnabledForLogon(string? taskXml, string userSid)
    {
        if (string.IsNullOrWhiteSpace(taskXml) || string.IsNullOrWhiteSpace(userSid))
            return false;

        try
        {
            var task = XDocument.Parse(taskXml).Root;
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            if (task?.Name != ns + "Task" || !IsEnabled(task.Element(ns + "Settings"), ns))
                return false;

            return task.Element(ns + "Triggers")?.Elements(ns + "LogonTrigger").Any(trigger =>
                IsEnabled(trigger, ns) &&
                (string.IsNullOrWhiteSpace((string?)trigger.Element(ns + "UserId")) ||
                 string.Equals(((string?)trigger.Element(ns + "UserId"))?.Trim(), userSid, StringComparison.OrdinalIgnoreCase))) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEnabled(XElement? element, XNamespace ns)
    {
        var value = (string?)element?.Element(ns + "Enabled");
        // Both task settings and trigger Enabled default to true in the schema.
        return value is null || XmlConvert.ToBoolean(value.Trim());
    }
}
