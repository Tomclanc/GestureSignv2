using System;
using System.Diagnostics;
using System.Text;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins.Common;

namespace GestureSign.CorePlugins.RunCommand
{
    public class RunCommandPlugin : IPlugin
    {
        #region Private Variables

        private RunCommand _GUI = null;
        private RunCommandSettings _Settings = null;

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.RunCommand.Name"); }
        }

        public string Description
        {
            get { return string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.RunCommand.Description"), _Settings.Command); }
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

        public RunCommand TypedGUI
        {
            get { return (RunCommand)GUI; }
        }

        public string Category
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.RunCommand.Category"); }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Command;

        #endregion

        #region Public Methods

        public void Initialize()
        {

        }

        public bool Gestured(PointInfo pointInfo)
        {
            if (_Settings == null) return false;

            string clipboardString = string.Empty;
            var parser = new EnvironmentVariablesParser(pointInfo);
            // The selected text is handed to the shell as environment variables
            // (%GS_SelectedText% in cmd, $env:GS_SelectedText in PowerShell) so
            // quotes or newlines in the selection cannot break the command.
            // Elevated processes do not inherit custom variables, so for them
            // the text is substituted into the command instead.
            var selectedText = parser.CaptureSelectedTextIfNeeded(_Settings.Command);
            // An environment block is limited to 32767 characters per value.
            if (selectedText != null && selectedText.Length > 30000)
                selectedText = selectedText.Substring(0, 30000);
            var passAsVariable = selectedText != null && !_Settings.RunAsAdministrator;
            string command = parser.ExpandEnvironmentVariables(_Settings.Command, replaceSelectedText: !passAsVariable);
            bool usePowerShell = string.Equals(_Settings.Shell, "PowerShell", StringComparison.OrdinalIgnoreCase);
            if (passAsVariable && usePowerShell)
            {
                // PowerShell does not expand %NAME%; map the placeholders to the
                // environment variables (works bare and inside double quotes).
                command = command
                    .Replace("%" + EnvironmentVariablesParser.SelectedTextUrlVariable + "%", "$($env:" + EnvironmentVariablesParser.SelectedTextUrlVariable + ")", StringComparison.OrdinalIgnoreCase)
                    .Replace("%" + EnvironmentVariablesParser.SelectedTextVariable + "%", "$($env:" + EnvironmentVariablesParser.SelectedTextVariable + ")", StringComparison.OrdinalIgnoreCase);
            }

            using (Process process = new Process())
            {
                process.StartInfo.FileName = usePowerShell ? "powershell.exe" : "cmd.exe";
                process.StartInfo.Arguments = usePowerShell
                    ? $"-NoProfile -ExecutionPolicy Bypass {(_Settings.ShowCmd ? "-NoExit " : "")}-EncodedCommand {EncodePowerShellCommand(command)}"
                    : $"{(_Settings.ShowCmd ? "/K " : "/C ")}\"{string.Join(" & ", command.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))}\"";
                process.StartInfo.WindowStyle = _Settings.ShowCmd ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden;
                process.StartInfo.CreateNoWindow = !_Settings.ShowCmd;
                process.StartInfo.UseShellExecute = _Settings.RunAsAdministrator;
                if (_Settings.RunAsAdministrator)
                    process.StartInfo.Verb = "runas";
                if (passAsVariable)
                {
                    process.StartInfo.Environment[EnvironmentVariablesParser.SelectedTextVariable] = selectedText;
                    process.StartInfo.Environment[EnvironmentVariablesParser.SelectedTextUrlVariable] = EnvironmentVariablesParser.UrlEncode(selectedText);
                }
                process.Start();
            }

            return true;
        }

        public bool Deserialize(string SerializedData)
        {
            return PluginHelper.DeserializeSettings(SerializedData, out _Settings);
        }

        public string Serialize()
        {
            if (_GUI != null)
                _Settings = _GUI.Settings;

            if (_Settings == null)
                _Settings = new RunCommandSettings();

            return PluginHelper.SerializeSettings(_Settings);
        }

        #endregion

        #region Private Methods

        private RunCommand CreateGUI()
        {
            RunCommand newGUI = new RunCommand() { Settings = _Settings };
            newGUI.Loaded += (o, e) => { TypedGUI.Settings = _Settings; };

            return newGUI;
        }

        private static string EncodePowerShellCommand(string command)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(command ?? string.Empty));
        }

        #endregion

        #region Host Control

        public IHostControl HostControl { get; set; }

        #endregion
    }
}
