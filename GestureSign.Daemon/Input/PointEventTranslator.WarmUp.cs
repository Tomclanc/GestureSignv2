using System;
using System.Drawing;
using GestureSign.Common.Configuration;
using ManagedWinapi.Hooks;
using GestureSign.Common.Log;

namespace GestureSign.Daemon.Input
{
    public partial class PointEventTranslator
    {
        /// <summary>
        /// Runs the button-down checks once so their first real use (inside the
        /// low-level mouse hook, which Windows abandons after a few hundred ms)
        /// does not pay for JIT, window lookups and process-path caches.
        /// </summary>
        internal void WarmUp(Point point)
        {
            try
            {
                ShouldPassThroughGestureSignUi(point);
                ShouldPassThroughRemoteDesktopInput(point);
                ShouldPassThroughGamingShellInput(point);
                IsCaptionButtonRegion(point);
                ShouldPreferMouseGesturesAtPoint(point);
                IsMouseButtonDown(AppConfigDrawingButtonOrRight());
            }
            catch (Exception ex)
            {
                Logging.LogMessage("Input warm-up skipped a check: " + ex.GetType().Name);
            }
        }

        private static MouseActions AppConfigDrawingButtonOrRight()
        {
            var button = AppConfig.DrawingButton;
            return button == MouseActions.None ? MouseActions.Right : button;
        }
    }
}
