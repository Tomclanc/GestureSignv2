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
            var configuredPath = AppConfig.KandoExecutablePath;
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "Kando", "kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "Kando.exe"),
                Path.Combine(baseDirectory, "kando", "kando.exe"),
                Path.Combine(baseDirectory, "kando", "Kando.exe"),
                Path.Combine(baseDirectory, "kando", "Kando-win32-x64", "kando.exe"),
                Path.Combine(baseDirectory, "kando", "Kando-win32-x64", "Kando.exe"),
                Path.Combine(baseDirectory, "kando.exe"),
                Path.Combine(baseDirectory, "Kando.exe"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "Kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "Kando-win32-x64", "kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Kando", "Kando-win32-x64", "Kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "kando", "kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "kando", "Kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "kando", "Kando-win32-x64", "kando.exe")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "kando", "Kando-win32-x64", "Kando.exe"))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
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

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
