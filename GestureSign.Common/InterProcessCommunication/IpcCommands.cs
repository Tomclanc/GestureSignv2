namespace GestureSign.Common.InterProcessCommunication
{
    public enum IpcCommands
    {
        StartSettings,
        StartTeaching,
        StopTraining,
        LoadApplications,
        LoadGestures,
        LoadConfiguration,
        GotGesture,
        ConfigReload,
        SynDeviceState,
        EnableRecognition,
        DisableRecognition,
        Exit,
        SynRecognitionState,
        Ping,
        RecoverInput,
        InputHealth,
        /// <summary>Run TestAction.json (written by the action editor) in the active window.</summary>
        RunTestAction
    }
}
