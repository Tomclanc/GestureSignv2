namespace GestureSign.CorePlugins.RunCommand
{
    public class RunCommandSettings
    {
        #region Constructors

        public RunCommandSettings()
        {

        }

        #endregion

        #region Public Properties

        public string Command { get; set; }

        public bool ShowCmd { get; set; }

        public string Shell { get; set; }

        public bool RunAsAdministrator { get; set; }

        #endregion
    }
}
