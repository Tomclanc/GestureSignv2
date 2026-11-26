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
        StartTeachingTwoStages,
        StartTeachingThreeStages,
        /// <summary>Run TestAction.json (written by the action editor) in the active window.</summary>
        RunTestAction,
        /// <summary>Discard cached radial menus and icons so the next menu shows the edited actions.</summary>
        ReloadRadialMenus
    }
}
