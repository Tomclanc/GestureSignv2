using System;
using System.IO;

namespace GestureSign.Shared;

internal static class ScriptInvocation
{
    internal static string PowerShellCommand(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute script path is required.", nameof(path));
        string Quote(string text) => "'" + text.Replace("'", "''") + "'";
        var invoke = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ps1" or ".cmd" or ".bat" => "& " + Quote(path),
            ".py" => "& 'python' " + Quote(path),
            ".ahk" => "Start-Process -FilePath " + Quote(path),
            _ => throw new ArgumentException("Unsupported script extension.", nameof(path))
        };
        return "Set-Location -LiteralPath " + Quote(Path.GetDirectoryName(path)!) + Environment.NewLine + invoke;
    }
}
