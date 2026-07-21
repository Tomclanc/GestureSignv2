using System;
using System.Threading;
using System.Windows.Forms;
using GestureSign.Common;
using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;
using GestureSign.Daemon.Triggers;
using ManagedWinapi.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.Daemon
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (TryRunUiAccessShortcutHelper(args))
                return;

            bool createdNew;
            using (new Mutex(true, Constants.Daemon, out createdNew))
            {
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    //Application.SetCompatibleTextRenderingDefault(false);
                    try
                    {
                        Application.ThreadException += Application_ThreadException;
                        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                        Logging.LoggedExceptionOccurred += (o, e) => ShowException(e);
                        Logging.OpenLogFile();
                        Logging.LogMessage($"GestureSign daemon started. Executable={Application.ExecutablePath}, Config={GestureSign.Common.Configuration.AppConfig.ConfigPath}, Log={Logging.LogFilePath}");
                        DisableEfficiencyModeForDaemon();

                        if (!LocalizationProvider.Instance.LoadFromFile("Daemon"))
                        {
                            LocalizationProvider.Instance.LoadFromResource(Properties.Resources.en);
                        }

                        PointCapture.Instance.Load();
                        SynchronizationContext uiContext = SynchronizationContext.Current;

                        GestureManager.Instance.Load(PointCapture.Instance);
                        ApplicationManager.Instance.Load(PointCapture.Instance);
                        TriggerManager.Instance.Load();
                        // Create host control class and pass to plugins
                        HostControl hostControl = new HostControl()
                        {
                            _ApplicationManager = ApplicationManager.Instance,
                            _GestureManager = GestureManager.Instance,
                            _PointCapture = PointCapture.Instance,
                            _PluginManager = PluginManager.Instance,
                            _TrayManager = TrayManager.Instance
                        };
                        PluginManager.Instance.Load(hostControl, uiContext);
                        TrayManager.Instance.Load();

                        NamedPipe.Instance.RunNamedPipeServer(Constants.Daemon, new MessageProcessor(uiContext));

                        Application.ApplicationExit += Application_ApplicationExit;

                        Application.Run();
                    }
                    catch (Exception e)
                    {
                        Logging.LogException(e);
                        MessageBox.Show(e.ToString(), LocalizationProvider.Instance.GetTextValue("Messages.Error"),
                            MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        Application.Exit();
                    }
                }
                else
                {
                    NamedPipe.SendMessageAsync(IpcCommands.StartSettings, Constants.Daemon, wait: false).Wait();
                }
            }
        }

        private static bool TryRunUiAccessShortcutHelper(string[] args)
        {
            if (args == null || args.Length != 2 ||
                !string.Equals(args[0], "--uiaccess-alt-f4", StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(args[1], out var rawWindowHandle))
            {
                return false;
            }

            var windowHandle = new IntPtr(rawWindowHandle);
            if (windowHandle == IntPtr.Zero)
                return true;

            try
            {
                SystemWindow.ForegroundWindow = new SystemWindow(windowHandle);
                Thread.Sleep(40);
                new InputSimulator().Keyboard.ModifiedKeyStroke(VirtualKeyCode.LMENU, VirtualKeyCode.F4);
            }
            catch
            {
                // The helper is intentionally one-shot. The normal daemon remains alive
                // even when the target window disappears before the shortcut is sent.
            }

            return true;
        }

        private static void DisableEfficiencyModeForDaemon()
        {
            const uint processPowerThrottlingCurrentVersion = 1;
            const uint processPowerThrottlingExecutionSpeed = 0x1;

            try
            {
                var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
                {
                    Version = processPowerThrottlingCurrentVersion,
                    ControlMask = processPowerThrottlingExecutionSpeed,
                    StateMask = 0
                };

                var success = NativeMethods.SetProcessInformation(
                    NativeMethods.GetCurrentProcess(),
                    NativeMethods.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                    ref state,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.PROCESS_POWER_THROTTLING_STATE)));

                Logging.LogMessage(success
                    ? "Efficiency mode opt-out applied for daemon process."
                    : "Efficiency mode opt-out was not applied.");
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            Logging.LogMessage("GestureSign daemon exiting. Reason=ApplicationExit");
            NamedPipe.Instance.Dispose();
            PointCapture.Instance.Dispose();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logging.LogMessage($"GestureSign daemon unhandled exception. IsTerminating={e.IsTerminating}");
            if (e.ExceptionObject is Exception exception)
                Logging.LogException(exception);
            else
                Logging.LogMessage(e.ExceptionObject?.ToString() ?? "Unhandled exception object was null.");
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            DialogResult result = DialogResult.Abort;
            try
            {
                Logging.LogException(e.Exception);
                string errorMsg = "An application error occurred. Please contact the author with the following information:\n\n";
                errorMsg = errorMsg + e.Exception;
                result = MessageBox.Show(errorMsg, "Error", MessageBoxButtons.AbortRetryIgnore,
                   MessageBoxIcon.Stop);
            }
            catch (Exception fe)
            {
                try
                {
                    MessageBox.Show(fe.ToString(), "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
                finally
                {
                    Application.Exit();
                }
            }

            // Exits the program when the user clicks Abort.
            if (result == DialogResult.Abort)
            {
                Logging.LogMessage("GestureSign daemon exiting. Reason=ThreadExceptionAbort");
                Application.Exit();
            }
        }

        private static void ShowException(Exception exception)
        {
            while (exception.InnerException != null)
                exception = exception.InnerException;

            MessageBox.Show(exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }
    }
}
