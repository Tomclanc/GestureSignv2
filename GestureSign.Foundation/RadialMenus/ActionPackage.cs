#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestureSign.Foundation.Steps;

namespace GestureSign.Foundation.RadialMenus;

/// <summary>What a shared action can do that deserves a look before importing it.</summary>
public enum ActionRiskKind
{
    /// <summary>Starts a program, opens a file or runs a shell command.</summary>
    RunProgram,
    /// <summary>Runs JavaScript or Lua.</summary>
    Script,
    /// <summary>Sends data over the network (HTTP request).</summary>
    Network,
    /// <summary>Sends text to an AI service.</summary>
    Ai,
    /// <summary>Uses a plugin that is not part of GestureSign.</summary>
    CustomPlugin
}

public sealed record ActionRisk(string ActionName, ActionRiskKind Kind, string Detail);

/// <summary>
/// One or more library actions packed for sharing: as a .gsaction file or as a
/// share code (text starting with "GestureSignAction:"). Library actions that an
/// action calls are packed along. On import every action gets a new id.
/// </summary>
public sealed class ActionPackage
{
    public const string FormatName = "GestureSign.ActionPackage";
    public const string ShareCodePrefix = "GestureSignAction:";
    public const string FileExtension = ".gsaction";
    public const string StepsPluginClass = "GestureSign.CorePlugins.Steps.StepsPlugin";
    public const string LibraryActionPluginClass = "GestureSign.CorePlugins.Library.LibraryActionPlugin";
    private const string BuiltInPluginPrefix = "GestureSign.CorePlugins.";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public List<RadialMenuAction> Actions { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ----- export --------------------------------------------------------

    /// <summary>Packs actions and every library action they call. API keys typed into AI steps are removed.</summary>
    public static ActionPackage Create(RadialMenuConfig config, IEnumerable<RadialMenuAction> actions)
    {
        var package = new ActionPackage();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<RadialMenuAction>(actions.Where(a => a != null));
        while (queue.Count > 0)
        {
            var action = queue.Dequeue();
            if (!seen.Add(action.Id ?? ""))
                continue;
            var copy = Clone(action);
            copy.Icon = IsFilePath(copy.Icon) ? null : copy.Icon; // local paths mean nothing elsewhere
            foreach (var command in copy.Commands)
            {
                if (command.PluginClass == StepsPluginClass)
                {
                    var program = StepProgram.FromJson(command.CommandSettings);
                    foreach (var step in AllSteps(program.Steps).Where(s => s.Type == StepCatalog.AiChat && s.Args != null))
                        step.Args["ApiKey"] = "";
                    command.CommandSettings = program.ToJson();
                }
            }
            package.Actions.Add(copy);
            foreach (var id in ReferencedActionIds(copy))
                if (config?.FindAction(id) is { } referenced)
                    queue.Enqueue(referenced);
        }
        return package;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Compact text for chats and forums: prefix + Base64 of the gzipped JSON.</summary>
    public string ToShareCode()
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return ShareCodePrefix + Convert.ToBase64String(output.ToArray());
    }

    // ----- import --------------------------------------------------------

    /// <summary>Reads a package from file content or a share code; throws a readable error when it is not one.</summary>
    public static ActionPackage Parse(string text)
    {
        text = (text ?? "").Trim();
        if (text.StartsWith(ShareCodePrefix, StringComparison.Ordinal))
        {
            var compressed = Convert.FromBase64String(text.Substring(ShareCodePrefix.Length).Trim());
            using var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var reader = new StreamReader(input, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        ActionPackage package;
        try
        {
            package = JsonSerializer.Deserialize<ActionPackage>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This is not a GestureSign action package.", ex);
        }
        if (package == null || package.Format != FormatName || package.Actions == null || package.Actions.Count == 0)
            throw new InvalidDataException("This is not a GestureSign action package.");
        foreach (var action in package.Actions)
            action.Commands ??= new();
        return package;
    }

    public static bool LooksLikeShareCode(string text)
        => !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith(ShareCodePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Adds the package's actions to the library with new ids, rewiring calls
    /// between them. Returns the added actions in package order.
    /// </summary>
    public List<RadialMenuAction> ImportInto(RadialMenuConfig config)
    {
        var map = Actions.Where(a => !string.IsNullOrEmpty(a.Id))
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, _ => RadialMenuConfig.NewId(), StringComparer.OrdinalIgnoreCase);
        var added = new List<RadialMenuAction>();
        foreach (var source in Actions)
        {
            var action = Clone(source);
            action.Id = action.Id != null && map.TryGetValue(action.Id, out var newId) ? newId : RadialMenuConfig.NewId();
            foreach (var command in action.Commands)
                command.CommandSettings = RemapSettings(command.PluginClass, command.CommandSettings, map);
            config.Actions.Add(action);
            added.Add(action);
        }
        config.Normalize();
        return added;
    }

    // ----- review --------------------------------------------------------

    /// <summary>Everything in the actions that runs programs or scripts, uses the network or AI, or needs another plugin.</summary>
    public static List<ActionRisk> Review(IEnumerable<RadialMenuAction> actions)
    {
        var risks = new List<ActionRisk>();
        foreach (var action in actions)
        {
            var name = action.Name ?? "";
            foreach (var command in action.Commands.Where(c => c != null))
            {
                var plugin = command.PluginClass ?? "";
                if (plugin == StepsPluginClass)
                {
                    foreach (var step in AllSteps(StepProgram.FromJson(command.CommandSettings).Steps).Where(s => !s.Disabled))
                        ReviewStep(name, step, risks);
                }
                else
                {
                    ReviewCommand(name, plugin, command.CommandSettings, risks);
                }
            }
        }
        return risks;
    }

    private static void ReviewStep(string name, Step step, List<ActionRisk> risks)
    {
        switch (step.Type)
        {
            case StepCatalog.RunProgram:
            case StepCatalog.LaunchOrActivate:
                risks.Add(new ActionRisk(name, ActionRiskKind.RunProgram, (step.Arg("Path") + " " + step.Arg("Arguments")).Trim()));
                break;
            case StepCatalog.RunJavaScript:
            case StepCatalog.RunLua:
                risks.Add(new ActionRisk(name, ActionRiskKind.Script, Shorten(step.Arg("Code"))));
                break;
            case StepCatalog.Http:
                risks.Add(new ActionRisk(name, ActionRiskKind.Network, step.Arg("Method") + " " + step.Arg("Url")));
                break;
            case StepCatalog.AiChat:
                risks.Add(new ActionRisk(name, ActionRiskKind.Ai, string.IsNullOrWhiteSpace(step.Arg("Endpoint")) ? "" : step.Arg("Endpoint")));
                break;
            case StepCatalog.RunCommand:
                ReviewCommand(name, step.Arg("PluginClass"), step.Arg("Settings"), risks);
                break;
        }
    }

    private static void ReviewCommand(string name, string plugin, string settings, List<ActionRisk> risks)
    {
        if (plugin.EndsWith(".RunCommandPlugin", StringComparison.Ordinal))
            risks.Add(new ActionRisk(name, ActionRiskKind.RunProgram, Shorten(JsonString(settings, "Command"))));
        else if (plugin.EndsWith(".OpenFilePlugin", StringComparison.Ordinal))
            risks.Add(new ActionRisk(name, ActionRiskKind.RunProgram, JsonString(settings, "Path")));
        else if (plugin.EndsWith(".LaunchApp", StringComparison.Ordinal))
            risks.Add(new ActionRisk(name, ActionRiskKind.RunProgram, JsonString(settings, "Key")));
        else if (!string.IsNullOrEmpty(plugin) && !plugin.StartsWith(BuiltInPluginPrefix, StringComparison.Ordinal))
            risks.Add(new ActionRisk(name, ActionRiskKind.CustomPlugin, plugin));
    }

    // ----- helpers -------------------------------------------------------

    private static RadialMenuAction Clone(RadialMenuAction action)
        => JsonSerializer.Deserialize<RadialMenuAction>(JsonSerializer.Serialize(action, JsonOptions), JsonOptions);

    public static IEnumerable<Step> AllSteps(IEnumerable<Step> steps)
    {
        foreach (var step in steps ?? Enumerable.Empty<Step>())
        {
            if (step == null) continue;
            yield return step;
            foreach (var child in AllSteps(step.Body)) yield return child;
            foreach (var child in AllSteps(step.Else)) yield return child;
        }
    }

    /// <summary>Ids of library actions an action calls, directly or from its steps.</summary>
    public static IEnumerable<string> ReferencedActionIds(RadialMenuAction action)
    {
        foreach (var command in action.Commands.Where(c => c != null))
        {
            if (command.PluginClass == LibraryActionPluginClass)
                yield return JsonString(command.CommandSettings, "ActionId");
            else if (command.PluginClass == StepsPluginClass)
                foreach (var step in AllSteps(StepProgram.FromJson(command.CommandSettings).Steps))
                    if (step.Type == StepCatalog.RunCommand && step.Arg("PluginClass").Trim() == LibraryActionPluginClass)
                        yield return JsonString(step.Arg("Settings"), "ActionId");
        }
    }

    private static string RemapSettings(string plugin, string settings, IReadOnlyDictionary<string, string> map)
    {
        if (plugin == LibraryActionPluginClass)
            return RemapActionId(settings, map);
        if (plugin != StepsPluginClass)
            return settings;
        var program = StepProgram.FromJson(settings);
        foreach (var step in AllSteps(program.Steps))
            if (step.Type == StepCatalog.RunCommand && step.Args != null && step.Arg("PluginClass").Trim() == LibraryActionPluginClass)
                step.Args["Settings"] = RemapActionId(step.Arg("Settings"), map);
        return program.ToJson();
    }

    private static string RemapActionId(string settings, IReadOnlyDictionary<string, string> map)
    {
        var id = JsonString(settings, "ActionId");
        return map.TryGetValue(id, out var newId) ? new JsonObject { ["ActionId"] = newId }.ToJsonString() : settings;
    }

    private static string JsonString(string json, string key)
    {
        try
        {
            return JsonNode.Parse(json ?? "") is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string Shorten(string text)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
    }

    private static bool IsFilePath(string icon)
        => !string.IsNullOrEmpty(icon) && icon.Length > 1 && (icon.Contains('\\') || icon.Contains('/'));
}
