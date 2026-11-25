#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Security;

namespace GestureSign.Foundation.Steps;

/// <summary>Encrypts step parameters of kind <see cref="StepParameterKind.Secret"/> (AI API keys) that are still plain text.</summary>
public static class StepSecrets
{
    /// <summary>Protects plain secrets in a program; returns whether anything changed.</summary>
    public static bool ProtectPlain(StepProgram program, Func<string, string> protect = null)
    {
        protect ??= SecretStore.Protect;
        var changed = false;
        foreach (var step in Walk(program?.Steps))
        {
            var info = StepCatalog.Find(step.Type);
            if (info == null || step.Args == null)
                continue;
            foreach (var parameter in info.Parameters.Where(p => p.Kind == StepParameterKind.Secret))
            {
                var value = step.Arg(parameter.Key);
                if (string.IsNullOrEmpty(value) || SecretStore.IsProtected(value))
                    continue;
                var protectedValue = protect(value);
                if (!string.Equals(protectedValue, value, StringComparison.Ordinal))
                {
                    step.Args[parameter.Key] = protectedValue;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>Protects plain secrets in a serialized step program; returns the new JSON, or null when nothing changed.</summary>
    public static string ProtectPlainJson(string programJson, Func<string, string> protect = null)
    {
        if (string.IsNullOrWhiteSpace(programJson))
            return null;
        StepProgram program;
        try { program = StepProgram.FromJson(programJson); }
        catch (System.Text.Json.JsonException) { return null; }
        return ProtectPlain(program, protect) ? program.ToJson() : null;
    }

    /// <summary>Protects plain secrets in every step action of a radial menu configuration.</summary>
    public static bool ProtectPlain(RadialMenuConfig config, Func<string, string> protect = null)
    {
        var changed = false;
        foreach (var command in config?.Actions.SelectMany(a => a.Commands ?? new List<RadialMenuCommand>()) ?? Enumerable.Empty<RadialMenuCommand>())
        {
            if (!string.Equals(command.PluginClass, ActionPackage.StepsPluginClass, StringComparison.Ordinal))
                continue;
            var updated = ProtectPlainJson(command.CommandSettings, protect);
            if (updated == null)
                continue;
            command.CommandSettings = updated;
            changed = true;
        }
        return changed;
    }

    private static IEnumerable<Step> Walk(IEnumerable<Step> steps)
    {
        foreach (var step in steps ?? Enumerable.Empty<Step>())
        {
            yield return step;
            foreach (var inner in Walk(step.Body)) yield return inner;
            foreach (var inner in Walk(step.Else)) yield return inner;
        }
    }
}
