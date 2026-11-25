using System;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Foundation.Diagnostics;
using GestureSign.Shared;

namespace GestureSign.Daemon
{
    internal static class DaemonDiagnostics
    {
        private static readonly string Directory = LocalDiagnostics.DirectoryFor(KandoComponentPaths.ProductDataDirectory);
        private static readonly LocalDiagnostics.Writer<TrayDiagnostic> Tray = new(Directory, LocalDiagnostics.TrayFile);
        private static readonly LocalDiagnostics.Writer<GestureDiagnostic> Gestures = new(Directory, LocalDiagnostics.GestureFile);

        internal static void RecordTray(bool visible) => Tray.Publish(new(
            DateTimeOffset.UtcNow, DiagnosticProcess.Current, AppConfig.ShowTrayIcon, visible, AppConfig.KandoEnabled));

        internal static void RecordGesture(Devices device, int contacts, string candidate, string match,
            GestureOutcome outcome, string[] actions)
            => Gestures.Publish(new(DateTimeOffset.UtcNow, DiagnosticProcess.Current, device.ToString(), contacts,
                LocalDiagnostics.GestureLabel(candidate), LocalDiagnostics.GestureLabel(match), outcome,
                Array.ConvertAll(actions, LocalDiagnostics.GestureLabel)));
    }
}
