using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Foundation.RadialMenus;
using ManagedWinapi.Windows;

namespace GestureSign.Daemon
{
    /// <summary>
    /// "Test run" from the action editor: after a short delay (to switch to the
    /// target window) runs the action in TestAction.json on the active window.
    /// </summary>
    internal static class TestActionRunner
    {
        public const string FileName = "TestAction.json";
        private const int DelayMs = 3000;
        private static int _running;

        public static void Start(SynchronizationContext uiContext)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                Logging.LogMessage("Test run ignored: one is already running.");
                return;
            }
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DelayMs).ConfigureAwait(false);
                    Run(uiContext);
                }
                catch (Exception ex)
                {
                    Logging.LogException(ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            });
        }

        private static void Run(SynchronizationContext uiContext)
        {
            var path = Path.Combine(AppConfig.ApplicationDataPath, FileName);
            if (!File.Exists(path))
            {
                Logging.LogMessage($"Test run skipped: {path} not found.");
                return;
            }
            var action = JsonSerializer.Deserialize<RadialMenuAction>(File.ReadAllText(path));
            if (action?.Commands == null || action.Commands.Count == 0)
                return;

            var cursor = Cursor.Position;
            var window = SystemWindow.ForegroundWindow;
            var pointInfo = new PointInfo(new List<Point> { cursor }, new List<List<Point>> { new List<Point> { cursor } }, window, uiContext);
            Logging.LogMessage($"Test run started. Action={action.Name}, Window={SafeTitle(window)}");
            foreach (var command in action.Commands.Where(c => c.IsEnabled))
            {
                var plugin = PluginManager.Instance.FindPluginByClassAndFilename(command.PluginClass, command.PluginFilename);
                if (plugin == null)
                {
                    Logging.LogMessage($"Test run: plugin not found: {command.PluginClass}");
                    continue;
                }
                plugin.Plugin.Deserialize(command.CommandSettings ?? "");
                plugin.Plugin.Gestured(pointInfo);
            }
            Logging.LogMessage("Test run finished.");
        }

        private static string SafeTitle(SystemWindow window)
        {
            try { return window?.Title ?? ""; }
            catch { return ""; }
        }
    }
}
