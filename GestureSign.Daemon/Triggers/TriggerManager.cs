using GestureSign.Common.Applications;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Input;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace GestureSign.Daemon.Triggers
{
    class TriggerManager
    {
        #region Private Variables

        private List<Trigger> _triggerList = new List<Trigger>(3);

        #endregion

        #region Constructors

        static TriggerManager()
        {
            Instance = new TriggerManager();
        }

        #endregion

        #region Public Instance Properties

        public static TriggerManager Instance { get; }

        #endregion

        #region Public Methods

        public void Load()
        {
            AddTrigger(new HotKeyManager());
            AddTrigger(new MouseTrigger());
            AddTrigger(new ContinuousGestureTrigger());
            AddTrigger(new TouchPadEdgeTrigger());
            AddTrigger(new TouchPadEdgeTrigger(GestureSign.Common.Input.Devices.TouchScreen, "TouchScreenEdge", "TouchScreen", 12, 70, 60, 1.2, true, true));
            AddTrigger(new TouchPadEdgeTrigger(GestureSign.Common.Input.Devices.Mouse, "TouchScreenEdge", "TouchScreenMouse", 12, 70, 60, 1.2, true));
        }

        #endregion


        #region Private Methods

        private void AddTrigger(Trigger newTrigger)
        {
            newTrigger.TriggerFired += Trigger_TriggerFired;
            _triggerList.Add(newTrigger);
        }

        private void Trigger_TriggerFired(object sender, TriggerFiredEventArgs e)
        {
            if (PointCapture.Instance.Mode == GestureSign.Common.Input.CaptureMode.UserDisabled)
                return;

            if (e.FiredActions == null || e.FiredActions.Count == 0) return;
            var point = new List<Point>(new[] { e.FiredPoint });
            var points = e.Points != null && e.Points.Count > 0
                ? e.Points
                : new List<List<Point>>(new[] { point });
            PluginManager.Instance.ExecuteAction(e.FiredActions, PointCapture.Instance.Mode, PointCapture.Instance.SourceDevice, new List<int>(new[] { 1 }), point, points);
        }

        #endregion
    }
}
