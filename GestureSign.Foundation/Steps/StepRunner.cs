#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    string RunProgram(string path, string arguments, bool wait, string workingDirectory, bool runAsAdmin);
    /// <summary>
    /// Switches to a window of the program, or launches it when none is open.
    /// whenActive is "minimize", "cycle" or "none". Returns launched, activated, minimized or none.
    /// </summary>
    string LaunchOrActivate(string path, string arguments, string processName, string titleFilter, string whenActive, bool hideLaunchWindow);
    string Http(string method, string url, string body, string contentType);
    /// <summary>Shows a short message; kind is "info", "warn" or "error".</summary>
    void Notify(string text, string kind = "info");
    /// <summary>Shows a message box; buttons are "OK", "OKCancel" or "YesNo". Returns true for OK/Yes.</summary>
    bool ShowMessageBox(string title, string text, string buttons);
    /// <summary>Asks for text; returns null when cancelled.</summary>
    string AskText(string prompt, string defaultValue, bool multiline);
    /// <summary>Asks to pick one item, or several when multiple is set; returns the picked indices, or null when cancelled.</summary>
    IReadOnlyList<int> AskChoice(string prompt, IReadOnlyList<string> labels, bool multiple);
    /// <summary>Shows a form; returns each field's value (text, double or bool) by name, or null when cancelled.</summary>
    IReadOnlyDictionary<string, object> AskForm(string title, IReadOnlyList<StepFormField> fields);
    /// <summary>Shows an open, save or folder dialog. mode: open, openMultiple, save, folder. Returns null when cancelled.</summary>
    IReadOnlyList<string> PickFiles(string mode, string title, string filter, string initialPath);
    IReadOnlyList<string> GetClipboardFiles();
    /// <summary>Full paths of the files and folders selected in File Explorer, on the desktop or (via Ctrl+C) in other file managers.</summary>
    IReadOnlyList<string> GetSelectedFiles();
    /// <summary>The folder open in File Explorer (the desktop folder on the desktop); empty when unknown.</summary>
    string GetExplorerFolder();
    /// <summary>Converts between Simplified and Traditional Chinese.</summary>
    string ConvertChinese(string text, bool toTraditional);
    /// <summary>Calls an OpenAI-compatible chat API; empty settings use the global AI settings.</summary>
    string AiChat(string endpoint, string apiKey, string model, string systemPrompt, string prompt, double? temperature);
    /// <summary>Shows text in a window the user can copy from.</summary>
    void ShowText(string title, string text);
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
        // The action's own variables are set up first, so that one named windowTitle or processName
        // takes the place of the built-in value instead of being silently ignored.
        foreach (var definition in program?.Variables ?? new List<StepVariableDef>())
        {
            var name = definition?.Name?.Trim();
            if (!IsVariableName(name) || variables.Contains(name))
                continue;
            var text = definition.Default ?? "";
            variables[name] = (definition.Kind ?? "text").ToLowerInvariant() switch
            {
                "list" => StepVariables.ToList(text).ToList(),
                "number" => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0d,
                "bool" => StepVariables.IsTruthy(text.Trim()),
                _ => text
            };
        }
        // The window the gesture came from, for actions that do not define these themselves.
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
                // Optional format: {name:url} URL-encodes, {name:json} escapes for a JSON string,
                // {name:quote} wraps in double quotes (each item of a list, separated by spaces).
                var format = "";
                var colon = name?.IndexOf(':') ?? -1;
                if (colon > 0)
                {
                    format = name.Substring(colon + 1).Trim().ToLowerInvariant();
                    name = name.Substring(0, colon).Trim();
                }
                if (end > i && IsVariableName(name) && format is "" or "url" or "json" or "quote")
                {
                    var text = variables.Text(name);
                    builder.Append(format switch
                    {
                        "quote" => variables[name] is System.Collections.IList items
                            ? string.Join(" ", StepVariables.ToList(items).Select(item => Quote(StepVariables.ToText(item))))
                            : Quote(text),
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

    private static string Quote(string text)
    {
        text = (text ?? "").Trim().Trim('"');
        // A trailing backslash would escape the closing quote (C:\ becomes "C:\\").
        if (text.EndsWith("\\", StringComparison.Ordinal))
            text += "\\";
        return "\"" + text + "\"";
    }

    private static readonly System.Text.Json.JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool IsVariableName(string name)
        => !string.IsNullOrEmpty(name) && (char.IsLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Unescapes \r, \n, \t and \\ in a separator typed by the user.</summary>
    private static string Unescape(string text)
        => (text ?? "").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

    /// <summary>"{name}" alone yields the variable itself (a list stays a list); anything else is interpolated text.</summary>
    private static object ResolveValue(string template, StepVariables v)
    {
        var arg = (template ?? "").Trim();
        return arg.Length > 2 && arg[0] == '{' && arg[^1] == '}' && IsVariableName(arg[1..^1].Trim())
            ? v[arg[1..^1].Trim()]
            : Interpolate(template, v);
    }

    private static string Convert(string text, string mode, IStepHost host)
    {
        text ??= "";
        switch ((mode ?? "").Trim())
        {
            case "upper": return text.ToUpperInvariant();
            case "lower": return text.ToLowerInvariant();
            case "title": return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower(CultureInfo.CurrentCulture));
            case "trim": return text.Trim();
            case "toSimplified": return host.ConvertChinese(text, toTraditional: false);
            case "toTraditional": return host.ConvertChinese(text, toTraditional: true);
            case "urlEncode": return Uri.EscapeDataString(text);
            case "urlDecode": return Uri.UnescapeDataString(text.Replace('+', ' '));
            case "base64Encode": return System.Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            case "base64Decode": return Encoding.UTF8.GetString(System.Convert.FromBase64String(text.Trim()));
            default: throw new InvalidOperationException($"Unknown text conversion: {mode}");
        }
    }

    private void RunStep(Step step, StepVariables v)
    {
        string Arg(string key) => Interpolate(step.Arg(key), v);
        bool Flag(string key) => StepVariables.IsTruthy(Arg(key).Trim());
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
                var listValue = ResolveValue(step.Arg("List"), v);
                var itemName = step.Arg("Item").Trim();
                foreach (var item in StepVariables.ToList(listValue).Take(MaxLoopCount))
                {
                    if (IsVariableName(itemName)) v[itemName] = item;
                    RunList(step.Body, v);
                }
                break;

            case StepCatalog.Group:
                RunList(step.Body, v);
                break;

            case StepCatalog.Comment:
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

            case StepCatalog.Compute:
                Output(_host.RunJavaScript(step.Arg("Expression"), v, expression: true));
                break;

            case StepCatalog.Notify:
                _host.Notify(Arg("Text"), step.Arg("Kind").Trim());
                break;

            case StepCatalog.MessageBox:
                Output(_host.ShowMessageBox(Arg("Title"), Arg("Text"), step.Arg("Buttons").Trim()));
                break;

            case StepCatalog.UserInput:
                var answer = _host.AskText(Arg("Prompt"), Arg("Default"), Flag("Multiline"));
                if (answer == null) throw new StopSignal();
                Output(answer);
                break;

            case StepCatalog.UserSelect:
                var choices = StepVariables.ToList(ResolveValue(step.Arg("Items"), v))
                    .Select(item => StepChoices.Split(StepVariables.ToText(item)))
                    .Where(item => item.Label.Length > 0 || item.Value.Length > 0)
                    .ToList();
                var multiple = Flag("Multiple");
                if (choices.Count == 0) throw new StopSignal();
                var picked = _host.AskChoice(Arg("Prompt"), choices.Select(c => c.Label).ToList(), multiple);
                if (picked == null) throw new StopSignal();
                var values = picked.Where(i => i >= 0 && i < choices.Count).Select(i => (object)choices[i].Value).ToList();
                if (multiple) Output(values);
                else if (values.Count > 0) Output(values[0]);
                else throw new StopSignal();
                break;

            case StepCatalog.UserForm:
                var fields = StepFormField.ParseAll(Arg("Fields"));
                if (fields.Count == 0) break;
                var answers = _host.AskForm(Arg("Title"), fields);
                if (answers == null) throw new StopSignal();
                foreach (var field in fields)
                    if (answers.TryGetValue(field.Name, out var fieldValue))
                        v[field.Name] = fieldValue;
                break;

            case StepCatalog.ChooseFile:
                var mode = step.Arg("Mode").Trim();
                var paths = _host.PickFiles(mode, Arg("Title"), Arg("Filter").Trim(), Arg("InitialPath").Trim());
                if (paths == null || paths.Count == 0) throw new StopSignal();
                if (string.Equals(mode, "openMultiple", StringComparison.OrdinalIgnoreCase)) Output(paths.Cast<object>().ToList());
                else Output(paths[0]);
                break;

            case StepCatalog.ReplaceText:
                var source = Arg("Input");
                var find = Arg("Find");
                if (find.Length == 0)
                    Output(source);
                else if (Flag("Regex"))
                    Output(Regex.Replace(source, find, Arg("Replace"), Flag("IgnoreCase") ? RegexOptions.IgnoreCase : RegexOptions.None, RegexTimeout));
                else
                    Output(source.Replace(find, Arg("Replace"), Flag("IgnoreCase") ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                break;

            case StepCatalog.RegexExtract:
                var regex = new Regex(Arg("Pattern"), RegexOptions.None, RegexTimeout);
                static string Value(Match m) => m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
                if (Flag("All"))
                    Output(regex.Matches(Arg("Input")).Cast<Match>().Select(m => (object)Value(m)).ToList());
                else
                {
                    var match = regex.Match(Arg("Input"));
                    Output(match.Success ? Value(match) : "");
                }
                break;

            case StepCatalog.ConvertText:
                Output(Convert(Arg("Input"), step.Arg("Mode"), _host));
                break;

            case StepCatalog.SplitText:
                var separator = Unescape(Arg("Separator"));
                Output(separator.Length == 0
                    ? StepVariables.ToList(Arg("Input")).ToList()
                    : Arg("Input").Split(separator).Cast<object>().ToList());
                break;

            case StepCatalog.JoinList:
                var joinSeparator = Unescape(Arg("Separator"));
                Output(string.Join(joinSeparator.Length == 0 ? "\n" : joinSeparator,
                    StepVariables.ToList(ResolveValue(step.Arg("List"), v)).Select(StepVariables.ToText)));
                break;

            case StepCatalog.GetDateTime:
                var format = Arg("Format");
                Output(DateTime.Now.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd HH:mm:ss" : format, CultureInfo.CurrentCulture));
                break;

            case StepCatalog.AiChat:
                double? temperature = double.TryParse(Arg("Temperature").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null;
                Output(_host.AiChat(Arg("Endpoint").Trim(), Arg("ApiKey").Trim(), Arg("Model").Trim(), Arg("SystemPrompt"), Arg("Prompt"), temperature) ?? "");
                break;

            case StepCatalog.ShowText:
                _host.ShowText(Arg("Title"), Arg("Text"));
                break;

            case StepCatalog.OpenUrl:
                _host.RunProgram(Arg("Url").Trim(), "", false, null, false);
                break;

            case StepCatalog.GetSelectedText:
                Output(_host.GetSelectedText() ?? "");
                break;

            case StepCatalog.GetClipboard:
                Output(_host.GetClipboardText() ?? "");
                break;

            case StepCatalog.GetSelectedFiles:
                Output((_host.GetSelectedFiles() ?? Array.Empty<string>()).Cast<object>().ToList());
                break;

            case StepCatalog.GetExplorerFolder:
                Output(_host.GetExplorerFolder() ?? "");
                break;

            case StepCatalog.GetClipboardFiles:
                Output((_host.GetClipboardFiles() ?? Array.Empty<string>()).Cast<object>().ToList());
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
                var output = _host.RunProgram(Arg("Path").Trim(), Arg("Arguments"), wait,
                    Arg("WorkingDirectory").Trim(), StepVariables.IsTruthy(Arg("RunAsAdmin").Trim()));
                if (wait) Output(output ?? "");
                break;

            case StepCatalog.LaunchOrActivate:
                Output(_host.LaunchOrActivate(Arg("Path").Trim(), Arg("Arguments"), Arg("ProcessName").Trim(), Arg("TitleFilter").Trim(),
                    Arg("WhenActive").Trim(), StepVariables.IsTruthy(Arg("HideLaunchWindow").Trim())) ?? "");
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
