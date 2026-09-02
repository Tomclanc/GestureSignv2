using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GestureSign.Shared;
using GestureSign.WinUI;

namespace GestureSign.WinUI.Services;

/// <summary>UI-free Kando process and executable coordination.</summary>
internal sealed class KandoCoordinator
{
    public string? FindExecutable(string configuredPath)
        => KandoComponentPaths.FindExecutable(configuredPath, AppContext.BaseDirectory);

    public Process? Start(LegacyOptions options, string arguments, Action<Exception>? onCompatibilityError = null)
    {
        try
        {
            var executablePath = FindExecutable(options.KandoExecutablePath);
            if (executablePath is null)
                return null;
            if (!KandoExecutableCompatibility.IsSupportedOnCurrentOperatingSystem(executablePath, out var reason))
            {
                onCompatibilityError?.Invoke(new BadImageFormatException(reason));
                return null;
            }
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            });
            KandoTaskbarIdentity.ApplyWhenWindowAvailable(executablePath);
            return process;
        }
        catch { return null; }
    }

    public void Stop(LegacyOptions options)
    {
        var executablePath = FindExecutable(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);
        foreach (var process in Process.GetProcessesByName("kando").Concat(Process.GetProcessesByName("Kando")).GroupBy(p => p.Id).Select(g => g.First()))
        {
            try
            {
                if (expectedPath is not null && !string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                process.Kill(entireProcessTree: true);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    public bool IsRunning(LegacyOptions options)
    {
        var executablePath = FindExecutable(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);
        foreach (var process in Process.GetProcessesByName("kando").Concat(Process.GetProcessesByName("Kando")).GroupBy(p => p.Id).Select(g => g.First()))
        {
            try
            {
                if (expectedPath is null || string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }
}
