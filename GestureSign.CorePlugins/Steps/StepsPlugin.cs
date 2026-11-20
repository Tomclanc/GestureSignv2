using System;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Foundation.Steps;

namespace GestureSign.CorePlugins.Steps
{
    /// <summary>
    /// Runs a step program (variables, branches, loops, JavaScript and Lua).
    /// The command settings are the program's JSON (see GestureSign.Foundation.Steps).
    /// </summary>
    public class StepsPlugin : IPlugin
    {
        public const string ClassName = "GestureSign.CorePlugins.Steps.StepsPlugin";

        private StepProgram _program = new StepProgram();

        public string Name => "Steps";
        public string Category => "Common";
        public string Description => $"{_program.Steps.Count} steps";
        public bool IsAction => true;
        public object GUI => null;
        // Steps usually send keys or paste into the target, so bring it forward first.
        public bool ActivateWindowDefault => true;
        public object Icon => IconSource.Command;
        public IHostControl HostControl { get; set; }

        public void Initialize()
        {
        }

        public bool Gestured(PointInfo actionPoint)
        {
            var runner = new StepRunner(new WindowsStepHost(actionPoint));
            var result = runner.Run(_program.Clone());
            Logging.LogMessage($"Step program finished. Steps={result.StepsExecuted}, Stopped={result.Stopped}, Error={result.Error ?? "None"}");
            if (result.Error != null && result.Error != "Canceled")
            {
                try { actionPoint.Invoke(() => StepToast.ShowMessage("GestureSign: " + result.Error)); }
                catch (Exception ex) { Logging.LogException(ex); }
            }
            return result.Error == null;
        }

        public bool Deserialize(string serializedData)
        {
            try
            {
                _program = StepProgram.FromJson(serializedData);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Step program could not be read: {ex.Message}");
                _program = new StepProgram();
                return false;
            }
        }

        public string Serialize() => _program.ToJson();
    }
}
