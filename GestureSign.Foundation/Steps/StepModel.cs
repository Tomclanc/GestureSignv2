#nullable disable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestureSign.Foundation.Steps;

/// <summary>
/// A Quicker-style step program: a tree of steps with variables, branches and
/// loops. Stored as JSON in the settings of a "Steps" command.
/// </summary>
public sealed class StepProgram
{
    public int Version { get; set; } = 1;
    public List<Step> Steps { get; set; } = new();

    /// <summary>Declared variables with default values; they are set before the first step runs.</summary>
    public List<StepVariableDef> Variables { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static StepProgram FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new StepProgram();
        var program = JsonSerializer.Deserialize<StepProgram>(json, JsonOptions) ?? new StepProgram();
        program.Steps ??= new();
        program.Variables ??= new();
        return program;
    }

    public StepProgram Clone() => FromJson(ToJson());
}

/// <summary>A declared variable, shown in the editor's variable list.</summary>
public sealed class StepVariableDef
{
    public string Name { get; set; }

    /// <summary>"text", "list", "number" or "bool".</summary>
    public string Kind { get; set; } = "text";

    /// <summary>Default value; for a list, one item per line.</summary>
    public string Default { get; set; }

    public string Description { get; set; }
}

public sealed class Step
{
    /// <summary>Step type id, see <see cref="StepCatalog"/>.</summary>
    public string Type { get; set; }

    /// <summary>Parameter values by key. Text values may contain {variable} placeholders.</summary>
    public Dictionary<string, string> Args { get; set; }

    /// <summary>Child steps of If (then branch), Repeat and ForEach.</summary>
    public List<Step> Body { get; set; }

    /// <summary>Else branch of If.</summary>
    public List<Step> Else { get; set; }

    public bool Disabled { get; set; }

    /// <summary>Free text shown in the editor.</summary>
    public string Note { get; set; }

    public string Arg(string key) => Args != null && Args.TryGetValue(key, out var value) ? value ?? "" : "";

    public static Step Create(string type)
    {
        var step = new Step { Type = type, Args = new Dictionary<string, string>() };
        var info = StepCatalog.Find(type);
        if (info != null)
        {
            foreach (var parameter in info.Parameters)
                if (parameter.Default != null) step.Args[parameter.Key] = parameter.Default;
            if (info.HasBody) step.Body = new List<Step>();
            if (info.HasElse) step.Else = new List<Step>();
        }
        return step;
    }
}
