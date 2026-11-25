#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Foundation.Steps;

/// <summary>
/// One field of a form step, written as one line: <c>name|label|kind|default|options</c>.
/// Kinds: text, multiline, number, choice, check, file, folder. Options (for choice) are separated by commas.
/// </summary>
public sealed record StepFormField(string Name, string Label, string Kind, string Default, IReadOnlyList<string> Options)
{
    public static readonly string[] Kinds = { "text", "multiline", "number", "choice", "check", "file", "folder" };

    /// <summary>Parses the field lines; lines without a valid variable name are skipped.</summary>
    public static IReadOnlyList<StepFormField> ParseAll(string spec)
    {
        var fields = new List<StepFormField>();
        foreach (var raw in (spec ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var parts = line.Split('|');
            string Part(int index) => index < parts.Length ? parts[index].Trim() : "";
            var name = Part(0);
            if (!StepRunner.IsVariableName(name) || fields.Any(f => f.Name == name))
                continue;
            var kind = Part(2).ToLowerInvariant();
            if (!Kinds.Contains(kind))
                kind = "text";
            var options = Part(4).Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
            var label = Part(1).Length > 0 ? Part(1) : name;
            fields.Add(new StepFormField(name, label, kind, Part(3), options));
        }
        return fields;
    }
}

/// <summary>Choice items written as "label|value": the label is shown, the value is the result.</summary>
public static class StepChoices
{
    public static (string Label, string Value) Split(string item)
    {
        item ??= "";
        var bar = item.IndexOf('|');
        if (bar < 0)
            return (item, item);
        var label = item.Substring(0, bar).Trim();
        var value = item.Substring(bar + 1).Trim();
        return (label.Length > 0 ? label : value, value);
    }
}
