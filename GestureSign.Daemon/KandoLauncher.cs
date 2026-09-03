using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

            var menuName = AppConfig.KandoMenuName;
            if (!string.IsNullOrWhiteSpace(menuName) && IsRunning(executablePath))
            {
                // Kando is an Electron application. Starting kando.exe for every
                // hotkey invocation forces a full Electron process startup before
                // the single-instance hand-off can reach the existing process.
                // Its local WebSocket IPC is already available once the process is
                // running, so use it to show the menu without the extra startup.
                _ = TryShowMenuViaIpcAsync(menuName, executablePath);
                return true;
            }

            var arguments = BuildShowMenuArguments();
            return StartKando(executablePath, arguments);
        }

        private static async Task TryShowMenuViaIpcAsync(string menuName, string executablePath)
        {
            try
            {
                var infoPath = Path.Combine(KandoComponentPaths.UserDataDirectory, "ipc-info.json");
                var menusPath = Path.Combine(KandoComponentPaths.UserDataDirectory, "menus.json");
                if (!File.Exists(infoPath) || !File.Exists(menusPath))
                {
                    StartKandoFallback(executablePath, menuName);
                    return;
                }

                using var infoDocument = JsonDocument.Parse(await File.ReadAllTextAsync(infoPath).ConfigureAwait(false));
                if (!infoDocument.RootElement.TryGetProperty("port", out var portElement) ||
                    !portElement.TryGetInt32(out var port) || port <= 0 || port > 65535)
                {
                    StartKandoFallback(executablePath, menuName);
                    return;
                }

                using var menusDocument = JsonDocument.Parse(await File.ReadAllTextAsync(menusPath).ConfigureAwait(false));
                if (!menusDocument.RootElement.TryGetProperty("menus", out var menus) || menus.ValueKind != JsonValueKind.Array)
                {
                    StartKandoFallback(executablePath, menuName);
                    return;
                }

                JsonElement? selectedMenu = null;
                foreach (var menu in menus.EnumerateArray())
                {
                    if (!menu.TryGetProperty("root", out var root) ||
                        !root.TryGetProperty("name", out var name) ||
                        !string.Equals(name.GetString(), menuName, StringComparison.Ordinal))
                        continue;

                    selectedMenu = menu;
                    break;
                }

                if (!selectedMenu.HasValue)
                {
                    StartKandoFallback(executablePath, menuName);
                    return;
                }

                using var socket = new ClientWebSocket();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token).ConfigureAwait(false);
                var payload = JsonSerializer.Serialize(new
                {
                    type = "show-menu",
                    menu = selectedMenu.Value
                });
                var bytes = Encoding.UTF8.GetBytes(payload);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
                socket.Abort();
                Logging.LogMessage($"Kando menu shown through IPC. Menu={menuName}");
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Kando IPC menu request failed; falling back to process hand-off. {ex.Message}");
                StartKandoFallback(executablePath, menuName);
            }
        }

        private static bool StartKandoFallback(string executablePath, string menuName)
        {
            var arguments = "--menu " + QuoteArgument(menuName);
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

                    // Kando can take several seconds to tear down its tray/
                    // renderer processes. Waiting synchronously here blocks
                    // the daemon's exit path and makes clicking Exit feel
                    // sluggish. Terminate it and let the OS reap the process
                    // asynchronously while GestureSign continues shutting
                    // down immediately.
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (MissingMethodException)
                    {
                        process.Kill();
                    }
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
