using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GestureSign.Shared;

namespace GestureSign.Daemon
{
    internal static class KandoLauncher
    {
        public static void StartIfEnabled()
        {
            if (!AppConfig.KandoEnabled)
            {
                Logging.LogMessage("Kando auto-start skipped: quick actions are disabled.");
                return;
            }

            var executablePath = FindExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Logging.LogMessage("Kando auto-start skipped: Kando.exe was not found.");
                return;
            }

            if (IsRunning(executablePath))
            {
                Logging.LogMessage("Kando auto-start skipped: Kando is already running.");
                return;
            }

            Logging.LogMessage(StartKando(executablePath, string.Empty)
                ? $"Kando auto-start requested. Executable={executablePath}"
                : $"Kando auto-start failed. Executable={executablePath}");
        }

        public static bool ShowMenu()
        {
            var executablePath = FindExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Logging.LogMessage("Kando launcher skipped: Kando.exe was not found.");
                return false;
            }

            var arguments = BuildShowMenuArguments();
            return StartKando(executablePath, arguments);
        }

        public static bool OpenSettings()
        {
            var executablePath = FindExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Logging.LogMessage("Kando settings skipped: Kando.exe was not found.");
                return false;
            }

            return StartKando(executablePath, "--settings");
        }

        public static void Stop()
        {
            var executablePath = FindExecutablePath();
            var expectedPath = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Path.GetFullPath(executablePath);

            foreach (var process in Process.GetProcessesByName("kando").Concat(Process.GetProcessesByName("Kando")).GroupBy(process => process.Id).Select(group => group.First()))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(expectedPath))
                    {
                        var processPath = process.MainModule?.FileName;
                        if (!string.Equals(Path.GetFullPath(processPath ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    Logging.LogException(ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static string FindExecutablePath()
        {
            return KandoComponentPaths.FindExecutable(
                AppConfig.KandoExecutablePath,
                AppDomain.CurrentDomain.BaseDirectory);
        }

        private static string BuildShowMenuArguments()
        {
            var menuName = AppConfig.KandoMenuName;
            if (!string.IsNullOrWhiteSpace(menuName))
                return "--menu " + QuoteArgument(menuName);

            return string.Empty;
        }

        private static bool StartKando(string executablePath, string arguments)
        {
            try
            {
                if (!KandoExecutableCompatibility.IsSupportedOnCurrentOperatingSystem(executablePath, out var incompatibilityReason))
                {
                    Logging.LogMessage(incompatibilityReason);
                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                KandoTaskbarIdentity.ApplyWhenWindowAvailable(executablePath);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return false;
            }
        }

        private static bool IsRunning(string executablePath)
        {
            var expectedPath = Path.GetFullPath(executablePath);
            foreach (var process in Process.GetProcessesByName("kando")
                         .Concat(Process.GetProcessesByName("Kando"))
                         .GroupBy(process => process.Id)
                         .Select(group => group.First()))
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (string.Equals(Path.GetFullPath(processPath ?? string.Empty), expectedPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    // A process at another integrity level may not expose MainModule.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
