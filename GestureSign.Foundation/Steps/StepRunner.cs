#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace GestureSign.Foundation.Steps;

/// <summary>Everything a step needs from the outside world. Implemented by the Steps plugin.</summary>
public interface IStepHost
{
    string WindowTitle { get; }
    string ProcessName { get; }
    string GetSelectedText();
    string GetClipboardText();
    void SetClipboardText(string text);
    void PasteText(string text);
    void TypeText(string text);
    void SendKeys(string keys);
    /// <summary>Starts a program, file or URL. When wait is set, returns its standard output.</summary>
    string RunProgram(string path, string arguments, bool wait);
    string Http(string method, string url, string body, string contentType);
    void Notify(string text);
    void RunCommand(string pluginClass, string settings);
    /// <summary>Runs JavaScript. As an expression the code is evaluated; otherwise it is a function body.</summary>
    object RunJavaScript(string code, StepVariables variables, bool expression);
    object RunLua(string code, StepVariables variables);
    void Log(string message);
}

/// <summary>Variables of one run: case-sensitive names, values are text, numbers, booleans or lists.</summary>
public sealed class StepVariables : IEnumerable<KeyValuePair<string, object>>
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public object this[string name]
    {
        get => name != null && _values.TryGetValue(name, out var value) ? value : null;
        set { if (!string.IsNullOrWhiteSpace(name)) _values[name.Trim()] = value; }
    }

    public bool Contains(string name) => name != null && _values.ContainsKey(name);
    public IReadOnlyCollection<string> Names => _values.Keys;
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public string Text(string name) => ToText(this[name]);

    public static string ToText(object value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable list => string.Join("\n", list.Cast<object>().Select(ToText)),
        _ => value.ToString()
    };

    /// <summary>Items of a list value, or the lines of a text value.</summary>
    public static IReadOnlyList<object> ToList(object value) => value switch
    {
        null => Array.Empty<object>(),
        string s => s.Replace("\r\n", "\n").Split('\n').Where(line => line.Length > 0).Cast<object>().ToList(),
        IEnumerable list => list.Cast<object>().ToList(),
        _ => new[] { value }
    };

    public static bool IsTruthy(object value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0 && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0",
        double d => d != 0 && !double.IsNaN(d),
        int i => i != 0,
        long l => l != 0,
        _ => true
    };
}

public sealed class StepRunResult
{
    public bool Stopped { get; set; }
    public string Error { get; set; }
    public int StepsExecuted { get; set; }
    public StepVariables Variables { get; set; }
}

/// <summary>Runs a <see cref="StepProgram"/> against an <see cref="IStepHost"/>.</summary>
public sealed class StepRunner
{
    public const int MaxStepsPerRun = 100_000;
    public const int MaxLoopCount = 10_000;

    private readonly IStepHost _host;
    private readonly CancellationToken _cancellation;
    private int _executed;

    private sealed class StopSignal : Exception { }

    public StepRunner(IStepHost host, CancellationToken cancellation = default)
    {
        _host = host;
        _cancellation = cancellation;
    }

    public StepRunResult Run(StepProgram program, StepVariables variables = null)
    {
        variables ??= new StepVariables();
        if (!variables.Contains("windowTitle")) variables["windowTitle"] = _host.WindowTitle ?? "";
        if (!variables.Contains("processName")) variables["processName"] = _host.ProcessName ?? "";
        var result = new StepRunResult { Variables = variables };
        try
        {
            RunList(program?.Steps, variables);
        }
        catch (StopSignal)
        {
            result.Stopped = true;
        }
        catch (OperationCanceledException)
        {
            result.Stopped = true;
            result.Error = "Canceled";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _host.Log($"Step program failed: {ex.GetType().Name}: {ex.Message}");
        }
        result.StepsExecuted = _executed;
        return result;
    }

    private void RunList(List<Step> steps, StepVariables variables)
    {
        if (steps == null)
            return;
        foreach (var step in steps)
        {
            if (step == null || step.Disabled)
                continue;
            _cancellation.ThrowIfCancellationRequested();
            if (++_executed > MaxStepsPerRun)
                throw new InvalidOperationException($"More than {MaxStepsPerRun} steps were executed; the program was stopped.");
            RunStep(step, variables);
        }
    }

    /// <summary>
    /// Replaces {name} with the variable's text ({name:url} URL-encoded, {name:json}
    /// escaped for a JSON string); {{ produces a literal brace. Other braces (JSON) stay as written.
    /// </summary>
    public static string Interpolate(string template, StepVariables variables)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
            return template ?? "";
        var builder = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { builder.Append('{'); i++; continue; }
            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                var name = end > i ? template.Substring(i + 1, end - i - 1).Trim() : null;
                // Optional format: {name:url} URL-encodes, {name:json} escapes for a JSON string.
                var format = "";
                var colon = name?.IndexOf(':') ?? -1;
                if (colon > 0)
                {
                    format = name.Substring(colon + 1).Trim().ToLowerInvariant();
                    name = name.Substring(0, colon).Trim();
                }
                if (end > i && IsVariableName(name) && format is "" or "url" or "json")
                {
                    var text = variables.Text(name);
                    builder.Append(format switch
                    {
                        "url" => Uri.EscapeDataString(text),
                        "json" => System.Text.Json.JsonSerializer.Serialize(text, RelaxedJson)[1..^1],
                        _ => text
                    });
                    i = end;
                    continue;
                }
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static readonly System.Text.Json.JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool IsVariableName(string name)
        => !string.IsNullOrEmpty(name) && (char.IsLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    private void RunStep(Step step, StepVariables v)
    {
        string Arg(string key) => Interpolate(step.Arg(key), v);
        void Output(object value)
        {
            var name = step.Arg("Output").Trim();
            if (IsVariableName(name)) v[name] = value;
        }

        switch (step.Type)
        {
            case StepCatalog.If:
                var condition = step.Arg("Condition");
                var passed = string.IsNullOrWhiteSpace(condition) || StepVariables.IsTruthy(_host.RunJavaScript(condition, v, expression: true));
                RunList(passed ? step.Body : step.Else, v);
                break;

            case StepCatalog.Repeat:
                if (!int.TryParse(Arg("Count").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    throw new InvalidOperationException($"Repeat count is not a number: {Arg("Count")}");
                count = Math.Clamp(count, 0, MaxLoopCount);
                var indexName = step.Arg("Index").Trim();
                for (var i = 0; i < count; i++)
                {
                    if (IsVariableName(indexName)) v[indexName] = (double)i;
                    RunList(step.Body, v);
                }
                break;

            case StepCatalog.ForEach:
                // "{name}" alone iterates the variable itself (a list stays a list);
                // anything else is interpolated and split into lines.
                var listArg = step.Arg("List").Trim();
                object listValue = listArg.Length > 2 && listArg[0] == '{' && listArg[^1] == '}' && IsVariableName(listArg[1..^1].Trim())
                    ? v[listArg[1..^1].Trim()]
                    : Interpolate(listArg, v);
                var itemName = step.Arg("Item").Trim();
                foreach (var item in StepVariables.ToList(listValue).Take(MaxLoopCount))
                {
                    if (IsVariableName(itemName)) v[itemName] = item;
                    RunList(step.Body, v);
                }
                break;

            case StepCatalog.Stop:
                throw new StopSignal();

            case StepCatalog.Delay:
                if (int.TryParse(Arg("Milliseconds").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                {
                    if (_cancellation.WaitHandle.WaitOne(Math.Min(ms, 60_000)))
                        _cancellation.ThrowIfCancellationRequested();
                }
                break;

            case StepCatalog.SetVariable:
                var name = step.Arg("Name").Trim();
                if (IsVariableName(name)) v[name] = Arg("Value");
                break;

            case StepCatalog.Notify:
                _host.Notify(Arg("Text"));
                break;

            case StepCatalog.GetSelectedText:
                Output(_host.GetSelectedText() ?? "");
                break;

            case StepCatalog.GetClipboard:
                Output(_host.GetClipboardText() ?? "");
                break;

            case StepCatalog.SetClipboard:
                _host.SetClipboardText(Arg("Text"));
                break;

            case StepCatalog.PasteText:
                _host.PasteText(Arg("Text"));
                break;

            case StepCatalog.TypeText:
                _host.TypeText(Arg("Text"));
                break;

            case StepCatalog.SendKeys:
                _host.SendKeys(Arg("Keys"));
                break;

            case StepCatalog.RunProgram:
                var wait = StepVariables.IsTruthy(Arg("Wait").Trim());
                var output = _host.RunProgram(Arg("Path").Trim(), Arg("Arguments"), wait);
                if (wait) Output(output ?? "");
                break;

            case StepCatalog.Http:
                Output(_host.Http(Arg("Method").Trim(), Arg("Url").Trim(), Arg("Body"), Arg("ContentType").Trim()) ?? "");
                break;

            case StepCatalog.RunCommand:
                _host.RunCommand(step.Arg("PluginClass").Trim(), step.Arg("Settings"));
                break;

            case StepCatalog.RunJavaScript:
                Output(_host.RunJavaScript(step.Arg("Code"), v, expression: false));
                break;

            case StepCatalog.RunLua:
                Output(_host.RunLua(step.Arg("Code"), v));
                break;

            default:
                _host.Log($"Unknown step type skipped: {step.Type}");
                break;
        }
    }
}
