using WindowsInput;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using ManagedWinapi;
using System.Windows.Forms;
using System.Threading;

namespace GestureSign.CorePlugins.Volume
{
    public class VolumePlugin : IPlugin
    {
        #region Private Variables

        private Volume _GUI = null;
        private VolumeSettings _settings = null;

        private enum Method
        {
            VolumeUp = 0,
            VolumeDown = 1,
            Mute = 2
        }

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Name"); }
        }

        public string Description
        {
            get { return GetDescription(_settings); }
        }

        public object GUI
        {
            get
            {
                if (_GUI == null)
                    _GUI = CreateGUI();

                return _GUI;
            }
        }

        public bool ActivateWindowDefault
        {
            get { return false; }
        }

        public Volume TypedGUI
        {
            get { return (Volume)GUI; }
        }

        public string Category
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Category"); }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Volume;

        #endregion

        #region Public Methods

        public void Initialize()
        {

        }

        public bool Gestured(PointInfo ActionPoint)
        {
            return AdjustVolume(_settings);
        }

        public bool Deserialize(string SerializedData)
        {
            return PluginHelper.DeserializeSettings(SerializedData, out _settings);
        }

        public string Serialize()
        {
            if (_GUI != null)
                _settings = _GUI.Settings;

            if (_settings == null)
                _settings = new VolumeSettings();

            return PluginHelper.SerializeSettings(_settings);
        }

        #endregion

        #region Private Methods

        private Volume CreateGUI()
        {
            Volume newGUI = new Volume();

            newGUI.Loaded += (o, e) =>
            {
                TypedGUI.Settings = _settings;
                TypedGUI.HostControl = HostControl;
            };

            return newGUI;
        }

        private string GetDescription(VolumeSettings Settings)
        {
            if (Settings == null)
                return LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Name");

            // Create string to store final output description
            string strOutput = "";

            // Build output string
            switch (Settings.Method)
            {
                case 0:
                    strOutput = LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Increase") + Settings.Percent + " %";
                    break;
                case 1:
                    strOutput = LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Decrease") + Settings.Percent + " %";
                    break;
                case 2:
                    strOutput = LocalizationProvider.Instance.GetTextValue("CorePlugins.Volume.Mute");
                    break;
            }

            return strOutput;
        }

        private bool AdjustVolume(VolumeSettings settings)
        {
            if (settings == null)
                return false;

            try
            {
                if ((Method)settings.Method == Method.Mute)
                    new InputSimulator().Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.VOLUME_MUTE);
                else if (settings.Method == 0 || settings.Method == 1)
                    EndpointVolume.Adjust(settings.Method == 0 ? settings.Percent : -settings.Percent);
                else return false;
                return true;
            }
            catch (System.Exception ex)
            {
                GestureSign.Common.Log.Logging.LogException(ex);
                return false;
            }
        }

        #endregion

        #region Host Control

        public IHostControl HostControl { get; set; }

        #endregion
    }
}
