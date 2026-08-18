using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;

namespace GestureSign.CorePlugins.MouseActions
{
    public class MouseActionsPlugin : IPlugin
    {
        #region Private Variables

        private MouseActionsUI _gui = null;
        private MouseActionsSettings _settings = null;

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Name"); }
        }

        public string Description
        {
            get { return GetDescription(); }
        }

        public object GUI
        {
            get { return _gui ?? (_gui = CreateGUI()); }
        }

        public bool ActivateWindowDefault
        {
            get { return false; }
        }

        public MouseActionsUI TypedGUI
        {
            get { return (MouseActionsUI)GUI; }
        }

        public string Category
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Category"); }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Mouse;

        #endregion

        #region Public Methods

        public void Initialize()
        {

        }

        public bool Gestured(PointInfo actionPoint)
        {
            if (_settings == null)
                return false;

            InputSimulator simulator = new InputSimulator();
            try
            {
                var waitMilliseconds = Math.Clamp(_settings.WaitMilliseconds, 0, 3_600_000);
                if (waitMilliseconds > 0)
                    Thread.Sleep(waitMilliseconds);

                int buttonId = (_settings.MouseAction & MouseActions.XButton1) != 0 ? 1 : 2;
                var referencePoint = GetReferencePoint(_settings.ActionLocation, actionPoint);

                if (_settings.MouseAction.GetButtons() != 0)
                {
                    if (_settings.ActionLocation != ClickPositions.Current)
                        MoveCursor(referencePoint, _settings.MoveDurationMilliseconds);
                }

                switch (_settings.MouseAction)
                {
                    case MouseActions.HorizontalScroll:
                        simulator.Mouse.HorizontalScroll(_settings.ScrollAmount).Sleep(30);
                        return true;
                    case MouseActions.VerticalScroll:
                        simulator.Mouse.VerticalScroll(_settings.ScrollAmount).Sleep(30);
                        return true;
                    case MouseActions.MoveMouseTo:
                        MoveCursor(_settings.MovePoint, _settings.MoveDurationMilliseconds);
                        return true;
                    case MouseActions.MoveMouseBy:
                        referencePoint.Offset(_settings.MovePoint);
                        MoveCursor(referencePoint, _settings.MoveDurationMilliseconds);
                        break;
                    case MouseActions.XButton1Click:
                    case MouseActions.XButton2Click:
                        simulator.Mouse.XButtonClick(buttonId).Sleep(30);
                        break;
                    case MouseActions.XButton1DoubleClick:
                    case MouseActions.XButton2DoubleClick:
                        simulator.Mouse.XButtonDoubleClick(buttonId).Sleep(30);
                        break;
                    case MouseActions.XButton1Down:
                    case MouseActions.XButton2Down:
                        simulator.Mouse.XButtonDown(buttonId).Sleep(30);
                        break;
                    case MouseActions.XButton1Up:
                    case MouseActions.XButton2Up:
                        simulator.Mouse.XButtonUp(buttonId).Sleep(30);
                        break;
                    default:
                        {
                            MethodInfo clickMethod = typeof(IMouseSimulator).GetMethod(_settings.MouseAction.ToString());
                            clickMethod.Invoke(simulator.Mouse, null);
                            Thread.Sleep(30);
                            break;
                        }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static void MoveCursor(Point target, int durationMilliseconds)
        {
            var duration = Math.Clamp(durationMilliseconds, 0, 10_000);
            if (duration == 0)
            {
                Cursor.Position = target;
                return;
            }

            var start = Cursor.Position;
            var steps = Math.Clamp(duration / 10, 1, 240);
            for (var step = 1; step <= steps; step++)
            {
                var progress = (double)step / steps;
                var eased = progress * progress * (3 - 2 * progress);
                Cursor.Position = new Point(
                    (int)Math.Round(start.X + (target.X - start.X) * eased),
                    (int)Math.Round(start.Y + (target.Y - start.Y) * eased));
                if (step < steps)
                    Thread.Sleep(Math.Max(1, duration / steps));
            }
        }

        public bool Deserialize(string serializedData)
        {
            if (serializedData.Contains("ClickPosition"))
            {
                LegacyMouseActionsSettings legacySettings;
                bool flag = PluginHelper.DeserializeSettings(serializedData, out legacySettings);
                _settings = new MouseActionsSettings()
                {
                    MouseAction = legacySettings.MouseAction.ToNewMouseActions(),
                    ActionLocation = legacySettings.ClickPosition.ToClickPositions(),
                    MovePoint = legacySettings.MovePoint,
                    ScrollAmount = legacySettings.ScrollAmount
                };
                return flag;
            }
            return PluginHelper.DeserializeSettings(serializedData, out _settings);
        }

        public string Serialize()
        {
            if (_gui != null)
                _settings = _gui.Settings;

            if (_settings == null)
                _settings = new MouseActionsSettings();

            return PluginHelper.SerializeSettings(_settings);
        }

        #endregion

        #region Private Methods

        private Point GetReferencePoint(ClickPositions position, PointInfo actionPoint)
        {
            Point referencePoint;
            switch (position)
            {
                case ClickPositions.LastUp:
                    referencePoint = actionPoint.LastCapturedPoints?.LastOrDefault()
                        ?? actionPoint.Points.Last().Last();
                    break;
                case ClickPositions.LastDown:
                    referencePoint = actionPoint.Points.Last().First();
                    break;
                case ClickPositions.FirstUp:
                    referencePoint = actionPoint.LastCapturedPoints?.FirstOrDefault()
                        ?? actionPoint.Points.First().Last();
                    break;
                case ClickPositions.FirstDown:
                    referencePoint = actionPoint.Points.First().First();
                    break;
                case ClickPositions.Custom:
                    return _settings.MovePoint;
                default:
                    referencePoint = Cursor.Position;
                    break;
            }
            return referencePoint;

        }

        private MouseActionsUI CreateGUI()
        {
            MouseActionsUI newGUI = new MouseActionsUI();

            newGUI.Loaded += (o, e) =>
            {
                TypedGUI.Settings = _settings;
            };

            return newGUI;
        }

        private string GetDescription()
        {
            switch (_settings.MouseAction)
            {
                case MouseActions.HorizontalScroll:
                    return
                        String.Format(
                            LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.HorizontalScroll"),
                            (_settings.ScrollAmount >= 0
                                ? LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.Right")
                                : LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.Left")),
                            Math.Abs(_settings.ScrollAmount));
                case MouseActions.VerticalScroll:
                    return
                        String.Format(
                            LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.VerticalScroll"),
                            (_settings.ScrollAmount >= 0
                                ? LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.Up")
                                : LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.Down")),
                            Math.Abs(_settings.ScrollAmount));
                case MouseActions.MoveMouseBy:
                    return LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.MoveMouseBy") + _settings.MovePoint;
                case MouseActions.MoveMouseTo:
                    return LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Description.MoveMouseTo") + _settings.MovePoint;
            }

            string button, action, location;
            MouseActionDescription.ButtonDescription.TryGetValue(_settings.MouseAction.GetButtons(), out button);
            MouseActionDescription.DescriptionDict.TryGetValue(_settings.MouseAction.GetActions(), out action);
            ClickPositionDescription.DescriptionDict.TryGetValue(_settings.ActionLocation, out location);
            return string.Format("{0} {1} {2}", location, action, button);
        }

        #endregion

        #region Host Control

        public IHostControl HostControl { get; set; }

        #endregion
    }
}
