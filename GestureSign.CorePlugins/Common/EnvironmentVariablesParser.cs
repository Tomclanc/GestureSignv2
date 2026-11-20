using GestureSign.Common.Plugins;
using System;
using System.Linq;
using System.Windows.Forms;

namespace GestureSign.CorePlugins.Common
{
    public class EnvironmentVariablesParser
    {
        public const string SelectedTextVariable = "GS_SelectedText";
        public const string SelectedTextUrlVariable = "GS_SelectedTextUrl";

        PointInfo _pointInfo;
        private string _selectedText;

        public EnvironmentVariablesParser(PointInfo pointInfo)
        {
            _pointInfo = pointInfo;
        }

        /// <summary>
        /// The text selected in the target window, captured at most once per
        /// command execution and only when one of the given texts asks for it
        /// (%GS_SelectedText%, %GS_SelectedTextUrl% or $env:GS_SelectedText).
        /// Returns null when no text references it.
        /// </summary>
        public string CaptureSelectedTextIfNeeded(params string[] texts)
        {
            if (_selectedText != null)
                return _selectedText;
            foreach (var text in texts)
            {
                if (!string.IsNullOrEmpty(text) && text.IndexOf(SelectedTextVariable, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _selectedText = SelectedTextCapture.Capture(_pointInfo);
                    return _selectedText;
                }
            }
            return null;
        }

        public static string UrlEncode(string text) => Uri.EscapeDataString(text ?? string.Empty);

        public string ExpandEnvironmentVariables(string command) => ExpandEnvironmentVariables(command, true);

        public string ExpandEnvironmentVariables(string command, bool replaceSelectedText)
        {
            if (string.IsNullOrWhiteSpace(command)) return command;

            if (replaceSelectedText && command.IndexOf(SelectedTextVariable, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var selected = CaptureSelectedTextIfNeeded(command) ?? string.Empty;
                // Replace the URL form first: it shares the plain name as a prefix.
                command = ReplaceIgnoreCase(command, "%" + SelectedTextUrlVariable + "%", UrlEncode(selected));
                command = ReplaceIgnoreCase(command, "%" + SelectedTextVariable + "%", selected);
            }

            command = Environment.ExpandEnvironmentVariables(command);

            if (command.Contains("%GS_Clipboard%"))
            {
                string clipboardString = string.Empty;
                _pointInfo.Invoke(() =>
                {
                    IDataObject iData = Clipboard.GetDataObject();
                    if (iData != null && iData.GetDataPresent(DataFormats.Text))
                    {
                        clipboardString = (string)iData.GetData(DataFormats.Text);
                    }
                });
                if (!string.IsNullOrEmpty(clipboardString))
                    command = command.Replace("%GS_Clipboard%", clipboardString);
            }

            if (command.Contains("%GS_ClassName%") && !string.IsNullOrEmpty(_pointInfo.Window.ClassName))
            {
                command = command.Replace("%GS_ClassName%", _pointInfo.Window.ClassName);
            }
            if (command.Contains("%GS_Title%") && !string.IsNullOrEmpty(_pointInfo.Window.Title))
            {
                command = command.Replace("%GS_Title%", _pointInfo.Window.Title);
            }
            if (command.Contains("%GS_PID%"))
            {
                command = command.Replace("%GS_PID%", _pointInfo.Window.ProcessId.ToString());
            }

            return command.Replace("%GS_StartPoint_X%", _pointInfo.PointLocation.First().X.ToString()).
              Replace("%GS_StartPoint_Y%", _pointInfo.PointLocation.First().Y.ToString()).
              Replace("%GS_EndPoint_X%", _pointInfo.Points[0].Last().X.ToString()).
              Replace("%GS_EndPoint_Y%", _pointInfo.Points[0].Last().Y.ToString()).
              Replace("%GS_WindowHandle%", _pointInfo.WindowHandle.ToString());
        }

        private static string ReplaceIgnoreCase(string text, string token, string value)
            => text.Replace(token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
