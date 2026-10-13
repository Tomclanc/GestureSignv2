using GestureSign.Common.Plugins;
using GestureSign.Common.Localization;

namespace GestureSign.CorePlugins
{
    public class MaximizeRestore : IPlugin
    {
        #region Private Variables

        IHostControl _HostControl = null;

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MaximizeRestore.Name"); }
        }

        public string Description
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MaximizeRestore.Description"); }
        }

        public object GUI
        {
            get { return null; }
        }

        public bool ActivateWindowDefault
        {
            // This command addresses the captured HWND directly. Foreground
            // activation restrictions must not prevent the state request.
            get { return false; }
        }

        public string Category
        {
            get { return "Windows"; }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.MaximizeRestore;

        #endregion

        #region Public Methods

        public void Initialize()
        {

        }

        public void ShowGUI(bool IsNew)
        {
            // Nothing to do here
        }

        public bool Gestured(PointInfo ActionPoint)
        {
            return WindowStateCommand.Execute(ActionPoint, false);
        }

        public bool Deserialize(string SerializedData)
        {
            return true;
            // Nothing to do here
        }

        public string Serialize()
        {
            // Nothing to serialize
            return "";
        }

        #endregion

        #region Host Control

        public IHostControl HostControl
        {
            get { return _HostControl; }
            set { _HostControl = value; }
        }

        #endregion
    }
}
