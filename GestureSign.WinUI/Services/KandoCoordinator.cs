using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Shared;
using GestureSign.WinUI;

namespace GestureSign.WinUI.Services;

/// <summary>UI-free Kando process and executable coordination.</summary>
internal sealed class KandoCoordinator
{
    public string? FindExecutable(string configuredPath)
        => KandoComponentPaths.FindExecutable(configuredPath, AppContext.BaseDirectory);

    public Process? Start(LegacyOptions options, string arguments, Action<Exception>? onCompatibilityError = null,
        string? hostConfiguration = null)
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
            var native = KandoComponentPaths.IsNativePieMenu(executablePath);
            if (native)
                arguments = KandoComponentPaths.NativeArguments(string.IsNullOrWhiteSpace(arguments) ? "--background" : arguments,
                    Environment.ProcessPath, hostConfiguration);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            });
            if (!native)
                KandoTaskbarIdentity.ApplyWhenWindowAvailable(executablePath);
            return process;
        }
        catch { return null; }
    }

    public void Stop(LegacyOptions options)
    {
        var executablePath = FindExecutable(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);
        if (executablePath is not null && KandoComponentPaths.IsNativePieMenu(executablePath))
        {
            using var request = Start(options, "--quit");
            return;
        }
        foreach (var process in Process.GetProcessesByName(executablePath is null ? "kando" : Path.GetFileNameWithoutExtension(executablePath)))
        {
            try
            {
                if (expectedPath is null || !string.Equals(Path.GetFullPath(KandoProcessPath.Read(process) ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                process.Kill(entireProcessTree: false);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    public async Task StopNativeAndWaitAsync(LegacyOptions options)
    {
        var path = options.KandoExecutablePath;
        if (!File.Exists(path)) return;

        // Different component versions share the activation key for the integrated menu.
        // Let the old instance release it before the replacement starts.
        using var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var running = processes.Where(process => !process.HasExited && process.SessionId == current.SessionId &&
                string.Equals(KandoProcessPath.Read(process), path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (running.Length == 0) return;
            using var request = Start(options, "--quit");
            await Task.WhenAll(running.Select(process => process.WaitForExitAsync(timeout.Token)));
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public bool IsRunning(LegacyOptions options)
    {
        var executablePath = FindExecutable(options.KandoExecutablePath);
        var expectedPath = executablePath is null ? null : Path.GetFullPath(executablePath);
        foreach (var process in Process.GetProcessesByName(executablePath is null ? "kando" : Path.GetFileNameWithoutExtension(executablePath)))
        {
            try
            {
                if (expectedPath is not null && string.Equals(Path.GetFullPath(KandoProcessPath.Read(process) ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }

    public async Task StopAndWaitAsync(LegacyOptions options, Action<string>? log = null)
    {
        var executable = FindExecutable(options.KandoExecutablePath);
        if (executable is null) return;
        var expected = Path.GetFullPath(executable);
        await Task.Run(() =>
        {
            foreach (var process in Process.GetProcessesByName("kando").GroupBy(p => p.Id).Select(g => g.First()))
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    if (!string.Equals(Path.GetFullPath(KandoProcessPath.Read(process, log) ?? ""), expected, StringComparison.OrdinalIgnoreCase)) continue;
                    log?.Invoke($"Stopping managed Kando PID={process.Id}, path={expected}");
                    process.Kill(entireProcessTree: false);
                    if (!process.WaitForExit(5000)) throw new IOException("Kando is still running. Please close it and try again.");
                }
            }
        });
    }
}
