using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GestureSign.Common;
using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Triggers;

namespace GestureSign.Daemon
{
    // No native work in a static constructor: bootstrap on the running STA loop.
    internal sealed class DaemonApplicationContext : ApplicationContext
    {
        private readonly Control _dispatcher = new Control();
        private static PointCapture _capture;
        private static string _selfTestReport;
        internal static bool IsSelfTest => _selfTestReport != null;
        private System.Windows.Forms.Timer _selfTestTimer;

        internal static void ConfigureSelfTest(string[] args)
        {
            if (args.Length != 2 || args[0] != "--startup-self-test") return;
            _selfTestReport = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(_selfTestReport));
            Directory.CreateDirectory(_selfTestReport + ".data");
            Environment.SetEnvironmentVariable("GESTURESIGN_STARTUP_TEST_DATA", _selfTestReport + ".data");
        }

        internal DaemonApplicationContext()
        {
            _ = _dispatcher.Handle;
            _dispatcher.BeginInvoke(new System.Action(InitializeBackend));
        }

        private void Stage(string name, System.Action action)
        {
            Logging.LogMessage("Daemon startup stage starting. Stage=" + name);
            action();
            Logging.LogMessage("Daemon startup stage completed. Stage=" + name);
        }

        private void InitializeBackend()
        {
            try
            {
                var uiContext = SynchronizationContext.Current
                    ?? throw new InvalidOperationException("WinForms synchronization context is unavailable.");
                Logging.LogMessage($"Daemon message loop ready. Architecture={RuntimeInformation.ProcessArchitecture}, OSArchitecture={RuntimeInformation.OSArchitecture}, Thread={Environment.CurrentManagedThreadId}, Apartment={Thread.CurrentThread.GetApartmentState()}");
                Stage("Input", () => { _capture = PointCapture.Instance; _capture.Load(); });
                Stage("Gestures", () => GestureManager.Instance.Load(_capture));
                Stage("Applications", () => ApplicationManager.Instance.Load(_capture));
                Stage("Triggers", () => TriggerManager.Instance.Load());
                Stage("Tray", () => TrayManager.Instance.Load());
                var host = new HostControl
                {
                    _ApplicationManager = ApplicationManager.Instance,
                    _GestureManager = GestureManager.Instance,
                    _PointCapture = _capture,
                    _PluginManager = PluginManager.Instance,
                    _TrayManager = TrayManager.Instance
                };
                Stage("Plugins", () => PluginManager.Instance.Load(host, uiContext));
                Stage("IPC", () => NamedPipe.Instance.RunNamedPipeServer(Constants.Daemon, new MessageProcessor(uiContext)));
                Logging.LogMessage("Daemon startup completed. Input, tray and IPC ready.");
                ThreadPool.QueueUserWorkItem(_ => KandoLauncher.StartIfEnabled());
                if (_selfTestReport != null)
                {
                    _selfTestTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                    _selfTestTimer.Tick += async (_, _) =>
                    {
                        _selfTestTimer.Stop();
                        try
                        {
                            bool ipc = await NamedPipe.SendMessageAsync(IpcCommands.Ping, Constants.Daemon);
                            var previousMode = _capture.Mode;
                            _capture.Mode = GestureSign.Common.Input.CaptureMode.UserDisabled;
                            bool penPreemption = _capture.TestPenPreemption();
                            bool missingTouchRelease = _capture.TestMissingTouchRelease();
                            bool recoveryCommand = await NamedPipe.SendMessageAsync(IpcCommands.RecoverInput, Constants.Daemon);
                            await System.Threading.Tasks.Task.Delay(150);
                            bool recovery = recoveryCommand && _capture.InputStateCleared &&
                                _capture.Mode == GestureSign.Common.Input.CaptureMode.UserDisabled;
                            var recoveryCount = _capture.InputRecoveryCount;
                            await System.Threading.Tasks.Task.Run(() => _capture.RequestSystemRecoveryForTest());
                            await System.Threading.Tasks.Task.Delay(900);
                            bool systemRecovery = _capture.InputRecoveryCount > recoveryCount &&
                                _capture.InputStateCleared && _capture.Mode == GestureSign.Common.Input.CaptureMode.UserDisabled;
                            _capture.Mode = previousMode;
                            bool mouse = _capture.MouseHook.Hooked;
                            bool tray = TrayManager.Instance.TrayIconVisible;
                            bool pass = ipc && mouse && tray && recovery && penPreemption && systemRecovery && missingTouchRelease;
                            File.WriteAllText(_selfTestReport, $"Pass={pass}\nArchitecture={RuntimeInformation.ProcessArchitecture}\nMouseHook={mouse}\nTrayVisible={tray}\nIPC={ipc}\nPenPreemption={penPreemption}\nMissingTouchRelease={missingTouchRelease}\nInputRecovery={recovery}\nSystemEventRecovery={systemRecovery}\n");
                            Environment.ExitCode = pass ? 0 : 1;
                        }
                        catch (Exception ex) { ReportFailure(ex); }
                        ExitThread();
                    };
                    _selfTestTimer.Start();
                }
            }
            catch (Exception ex)
            {
                ReportFailure(ex);
                if (_selfTestReport == null)
                    MessageBox.Show(ex.ToString(), "GestureSign startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        internal static void ReportFailure(Exception ex)
        {
            Logging.LogMessage("Daemon startup failed.");
            Logging.LogException(ex);
            Environment.ExitCode = 1;
            if (_selfTestReport != null) File.WriteAllText(_selfTestReport, "Pass=False\n" + ex);
        }

        internal static void DisposeInput() => _capture?.Dispose();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _selfTestTimer?.Dispose(); _dispatcher.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
