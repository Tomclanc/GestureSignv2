using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;
using WindowsInput;

namespace GestureSign.Common.Plugins
{
    public class PluginManager : IPluginManager
    {
        #region Private Variables

        // Create variable to hold the only allowed instance of this class
        static readonly PluginManager _Instance = new PluginManager();
        List<IPluginInfo> _Plugins = new List<IPluginInfo>();
        private Task _lastActionTask;
        private SynchronizationContext _mainContext;

        #endregion

        public event EventHandler<GestureActionExecutedEventArgs> GestureActionExecuted;

        #region Public Properties

        public IPluginInfo[] Plugins { get { return _Plugins.ToArray(); } }

        public static PluginManager Instance
        {
            get { return _Instance; }
        }

        #endregion

        #region Constructors

        protected PluginManager()
        {

        }

        #endregion

        #region Events

        protected void PointCapture_GestureRecognized(object sender, RecognitionEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            // Get action to be executed
            var executableActions = ApplicationManager.Instance.GetRecognizedDefinedAction(e.GestureName)?.ToList();
            if (executableActions == null)
            {
                Logging.LogMessage($"Gesture action lookup returned null. Gesture={e.GestureName}");
                return;
            }
            Logging.LogMessage($"Gesture action lookup completed. Gesture={e.GestureName}, Actions={executableActions.Count}, Device={pointCapture.SourceDevice}, Mode={pointCapture.Mode}");
            ExecuteAction(executableActions, pointCapture.Mode, pointCapture.SourceDevice, e.ContactIdentifiers, e.FirstCapturedPoints, e.Points, e.ContactOrder, e.LastCapturedPoints);
        }

        #endregion

        #region Public Methods

        public void ExecuteAction(List<IAction> executableActions, CaptureMode mode, Devices devices, List<int> contactIdentifiers, List<Point> firstCapturedPoints, List<List<Point>> points, List<int> contactOrder = null, List<Point> lastCapturedPoints = null)
        {
            // Exit if we're teaching
            if (mode == CaptureMode.Training)
            {
                Logging.LogMessage("Gesture action skipped. Reason=TrainingMode");
                return;
            }
            if (mode == CaptureMode.UserDisabled)
            {
                Logging.LogMessage("Gesture action skipped. Reason=UserDisabled");
                return;
            }
            var target = ApplicationManager.Instance.CaptureWindow;
            var actionPoints = OrderByContactOrder(points, contactIdentifiers, contactOrder);
            var actionFirstCapturedPoints = OrderByContactOrder(firstCapturedPoints, contactIdentifiers, contactOrder);
            var actionLastCapturedPoints = OrderByContactOrder(lastCapturedPoints, contactIdentifiers, contactOrder);
            var pointInfo = new PointInfo(actionFirstCapturedPoints, actionLastCapturedPoints, actionPoints, target, _mainContext);
            var action = new Action<object>(o =>
            {
                var executed = false;
                foreach (IAction executableAction in executableActions)
                {
                    // Exit if there is no action configured
                    if (executableAction == null)
                    {
                        Logging.LogMessage("Gesture action skipped. Reason=NullAction");
                        continue;
                    }

                    if ((executableAction.IgnoredDevices & devices) != 0)
                    {
                        Logging.LogMessage($"Gesture action skipped. Action={executableAction.Name}, Reason=IgnoredDevice, Device={devices}");
                        continue;
                    }

                    if (executableAction.Commands == null)
                    {
                        Logging.LogMessage($"Gesture action skipped. Action={executableAction.Name}, Reason=NoCommands");
                        continue;
                    }

                    if (!Compute(executableAction.Condition, points, contactIdentifiers))
                    {
                        Logging.LogMessage($"Gesture action skipped. Action={executableAction.Name}, Reason=ConditionNotMatched");
                        continue;
                    }

                    var currentCommand = executableAction.Commands
                        .Where(item => item != null && item.IsEnabled)
                        .FirstOrDefault();
                    if (currentCommand == null)
                    {
                        Logging.LogMessage($"Gesture action skipped. Action={executableAction.Name}, Reason=NoEnabledCommand");
                        continue;
                    }

                    if (mode == CaptureMode.UserDisabled && !"GestureSign.CorePlugins.ToggleDisableGestures".Equals(currentCommand.PluginClass))
                    {
                        Logging.LogMessage($"Gesture action skipped. Action={executableAction.Name}, Command={currentCommand.Name}, Reason=UserDisabled");
                        continue;
                    }

                    NormalizeLegacyHotKeyCommand(executableAction, currentCommand);

                    target.WaitForIdle(200);

                    // Locate the plugin associated with this action
                    IPluginInfo pluginInfo = FindPluginByClassAndFilename(currentCommand.PluginClass, currentCommand.PluginFilename);

                    // Exit if there is no plugin available for action
                    if (pluginInfo == null)
                    {
                        Logging.LogMessage($"Gesture command skipped. Action={executableAction.Name}, Command={currentCommand.Name}, Plugin={currentCommand.PluginClass}, Reason=PluginNotFound");
                        continue;
                    }

                    var requiresActivation = executableAction.ActivateWindow == null && pluginInfo.Plugin.ActivateWindowDefault ||
                                             executableAction.ActivateWindow.GetValueOrDefault();
                    if (requiresActivation)
                    {
                        // Use the simplest activation mechanism: a real left
                        // click at the current pointer location. This works
                        // for protected/UWP windows without relying on
                        // foreground-lock heuristics or window whitelists.
                        ActivateByMouseClick(target);
                    }

                    // Load action settings into plugin
                    pluginInfo.Plugin.Deserialize(currentCommand.CommandSettings);
                    Logging.LogMessage($"Gesture command executing. Action={executableAction.Name}, Command={currentCommand.Name}, Plugin={currentCommand.PluginClass}, TargetHwnd={target.HWnd}");
                    // Execute plugin process
                    pluginInfo.Plugin.Gestured(pointInfo);
                    OnGestureActionExecuted(new GestureActionExecutedEventArgs(executableAction.Name, executableAction.GestureName, devices));
                    executed = true;
                }

                if (!executed)
                    Logging.LogMessage("Gesture action completed without executing any command.");
            });

            var observeExceptions = new Action<Task>(t =>
            {
                Logging.LogException(t.Exception.InnerException);
            });

            if (_lastActionTask == null)
            {
                _lastActionTask = Task.Factory.StartNew(action, null);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                _lastActionTask = _lastActionTask.ContinueWith(action);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private static bool ActivateTargetWindow(SystemWindow target)
        {
            if (target == null || target.HWnd == IntPtr.Zero)
                return false;

            // Hit-testing packaged apps can return their CoreWindow child. It
            // is not a valid foreground activation target; always activate
            // the GA_ROOT frame while leaving the original target available to
            // the plugin for app-specific classification.
            var activationTarget = target.TopLevelWindow ?? target;

            var activated = SystemWindow.ActivateWindow(activationTarget);
            var deadline = DateTime.UtcNow.AddMilliseconds(300);
            while (DateTime.UtcNow < deadline)
            {
                var foreground = SystemWindow.ForegroundWindow;
                if (foreground != null && foreground.HWnd == activationTarget.HWnd)
                {
                    Logging.LogMessage($"Gesture target activated. TargetHwnd={target.HWnd}, ActivationHwnd={activationTarget.HWnd}, ForegroundHwnd={foreground.HWnd}, Activated={activated}");
                    return true;
                }

                Thread.Sleep(10);
            }

            var finalForeground = SystemWindow.ForegroundWindow;
            var success = finalForeground != null && finalForeground.HWnd == activationTarget.HWnd;
            Logging.LogMessage($"Gesture target activation {(success ? "completed" : "failed")}. TargetHwnd={target.HWnd}, ActivationHwnd={activationTarget.HWnd}, ForegroundHwnd={finalForeground?.HWnd}, Activated={activated}");
            return success;
        }

        private static void ActivateByMouseClick(SystemWindow target)
        {
            try
            {
                new InputSimulator().Mouse.LeftButtonClick();
                Logging.LogMessage($"Gesture target activation click sent. TargetHwnd={target?.HWnd}");
                // Give the target input queue a brief opportunity to process
                // the click before the plugin injects its keyboard/message
                // action. Do not verify the foreground HWND or skip the action.
                Thread.Sleep(60);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        private static List<T> OrderByContactOrder<T>(List<T> values, List<int> contactIdentifiers, List<int> contactOrder)
        {
            if (values == null || contactIdentifiers == null || contactOrder == null ||
                values.Count != contactIdentifiers.Count || contactOrder.Count == 0)
                return values;

            var ordered = new List<T>(values.Count);
            var used = new bool[values.Count];
            foreach (var identifier in contactOrder)
            {
                for (var index = 0; index < contactIdentifiers.Count; index++)
                {
                    if (!used[index] && contactIdentifiers[index] == identifier)
                    {
                        ordered.Add(values[index]);
                        used[index] = true;
                        break;
                    }
                }
            }

            for (var index = 0; index < values.Count; index++)
            {
                if (!used[index])
                    ordered.Add(values[index]);
            }
            return ordered;
        }

        public bool LoadPlugins(IHostControl host)
        {
            // Default return value to failure
            bool bFailed = true;

            // Clear any existing plugins
            _Plugins = new List<IPluginInfo>();
            //_Plugins.Clear();
            string directoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (directoryPath == null) return true;

            // Load core plugins.
            string corePluginsPath = Path.Combine(directoryPath, "GestureSign.CorePlugins.dll");
            if (File.Exists(corePluginsPath))
            {
                _Plugins.AddRange(LoadPluginsFromAssembly(corePluginsPath, host));
                bFailed = false;
            }

            var extraPluginsPath = Path.Combine(directoryPath, "Plugins");
            if (Directory.Exists(extraPluginsPath))
            {
                // Load extra plugins.
                foreach (string sFilePath in Directory.GetFiles(extraPluginsPath, "*.dll"))
                {
                    _Plugins.AddRange(LoadPluginsFromAssembly(sFilePath, host));
                    bFailed = false;
                }
            }

            EnsureBuiltInFallbackPlugins(host);

            return bFailed;
        }

        private void EnsureBuiltInFallbackPlugins(IHostControl host)
        {
            if (FindPluginByClassAndFilename("GestureSign.CorePlugins.HotKey.HotKeyPlugin", "GestureSign.CorePlugins.dll") != null)
                return;

            var hotKey = new BuiltInHotKeyPlugin { HostControl = host };
            hotKey.Initialize();
            _Plugins.Add(new PluginInfo(hotKey, "GestureSign.CorePlugins.HotKey.HotKeyPlugin", "GestureSign.CorePlugins.dll"));
            Logging.LogMessage("Built-in fallback plugin loaded. Plugin=GestureSign.CorePlugins.HotKey.HotKeyPlugin");
        }

        private void OnGestureActionExecuted(GestureActionExecutedEventArgs e)
        {
            GestureActionExecuted?.Invoke(this, e);
        }

        public IPluginInfo FindPluginByClassAndFilename(string PluginClass, string PluginFilename)
        {
            // Get reference to plugin using PluginClass and PluginFilename
            return _Plugins.FirstOrDefault(p => p.Class == PluginClass && p.Filename == PluginFilename);
        }

        public bool PluginExists(string PluginClass, string PluginFilename)
        {
            return _Plugins.Exists(p => p.Class == PluginClass && p.Filename == PluginFilename);
        }

        private static void NormalizeLegacyHotKeyCommand(IAction action, ICommand command)
        {
            if (action == null || command == null)
                return;

            if (!IsEdgeGesture(action.GestureName))
                return;

            if (string.IsNullOrWhiteSpace(command.PluginClass) ||
                command.PluginClass.IndexOf("RunCommand", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            var settings = command.CommandSettings ?? string.Empty;
            var commandName = command.Name ?? string.Empty;
            var looksLikeHotKey =
                settings.IndexOf("\"KeyCode\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                settings.IndexOf("\"Control\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandName.IndexOf("快捷键", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandName.IndexOf("HotKey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandName.IndexOf("Hot Key", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeHotKey)
                return;

            command.PluginClass = "GestureSign.CorePlugins.HotKey.HotKeyPlugin";
            command.PluginFilename = "GestureSign.CorePlugins.dll";
            if (string.IsNullOrWhiteSpace(command.CommandSettings) ||
                command.CommandSettings.IndexOf("\"Command\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                command.CommandSettings = "{\"Windows\":false,\"Control\":true,\"Shift\":false,\"Alt\":false,\"KeyCode\":[67],\"SendByKeybdEvent\":false}";
            }
            Logging.LogMessage($"Gesture command normalized. Action={action.Name}, Gesture={action.GestureName}, Command={command.Name}, Plugin=GestureSign.CorePlugins.HotKey.HotKeyPlugin");
        }

        private static bool IsEdgeGesture(string gestureName)
        {
            return !string.IsNullOrWhiteSpace(gestureName) &&
                   (gestureName.StartsWith("TouchScreenEdge.", StringComparison.OrdinalIgnoreCase) ||
                    gestureName.StartsWith("TouchPadEdge.", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private Methods

        private List<IPluginInfo> LoadPluginsFromAssembly(string assemblyLocation, IHostControl hostControl)
        {
            List<IPluginInfo> retPlugins = new List<IPluginInfo>();

            //To avoid exception System.NotSupportedException
            byte[] file = File.ReadAllBytes(assemblyLocation);
            Assembly aPlugin = Assembly.Load(file);

            Localization.LocalizationProvider.Instance.AddAssembly(aPlugin.FullName);

            Type[] tPluginTypes = aPlugin.GetTypes();

            foreach (Type tPluginType in tPluginTypes)
                if (tPluginType.GetInterface("IPlugin") != null)
                {
                    IPlugin plugin = Activator.CreateInstance(tPluginType) as IPlugin;

                    // If we have a new instance of a plugin, initialize it and add it to return list
                    if (plugin != null)
                    {
                        plugin.HostControl = hostControl;
                        plugin.Initialize();
                        retPlugins.Add(new PluginInfo(plugin, tPluginType.FullName, Path.GetFileName(assemblyLocation)));
                    }
                }

            return retPlugins;
        }

        private bool Compute(string condition, List<List<Point>> pointList, List<int> contactIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;

            string expression = GetExpression(condition, pointList, contactIdentifiers);
            try
            {
                DataTable dataTable = new DataTable();
                var result = dataTable.Compute(expression, null);
                return result is DBNull || Convert.ToBoolean(result);
            }
            catch (EvaluateException)
            {
                return false;
            }
        }

        private string GetExpression(string condition, List<List<Point>> pointList, List<int> contactIdentifiers)
        {
            for (int i = 1; i <= pointList.Count; i++)
            {
                int startX = pointList[i - 1].FirstOrDefault().X;
                int startY = pointList[i - 1].FirstOrDefault().Y;
                int endX = pointList[i - 1].LastOrDefault().X;
                int endY = pointList[i - 1].LastOrDefault().Y;

                if (condition.Contains('%'))
                {
                    int width = (int)System.Windows.SystemParameters.VirtualScreenWidth;
                    int height = (int)System.Windows.SystemParameters.VirtualScreenHeight;
                    condition = ReplaceVariables(condition, i, "start_X%", startX * 100 / width);
                    condition = ReplaceVariables(condition, i, "start_Y%", startY * 100 / height);
                    condition = ReplaceVariables(condition, i, "end_X%", endX * 100 / width);
                    condition = ReplaceVariables(condition, i, "end_Y%", endY * 100 / height);
                }

                condition = ReplaceVariables(condition, i, "start_X", startX);
                condition = ReplaceVariables(condition, i, "start_Y", startY);
                condition = ReplaceVariables(condition, i, "end_X", endX);
                condition = ReplaceVariables(condition, i, "end_Y", endY);

                condition = ReplaceVariables(condition, i, "ID", contactIdentifiers[i - 1]);
            }
            return condition;
        }

        private string ReplaceVariables(string str, int id, string key, int value)
        {
            string variable = $"finger_{id}_{key}";
            return str.Replace(variable, value.ToString());
        }

        #endregion

        #region ILoadable Methods

        public void Load(IHostControl host, SynchronizationContext syncContext = null)
        {
            _mainContext = syncContext;
            // Create empty list of plugins, then load as many as possible from plugin directory
            LoadPlugins(host);

            if (host == null) return;
            host.PointCapture.GestureRecognized += PointCapture_GestureRecognized;
        }

        #endregion
    }
}
