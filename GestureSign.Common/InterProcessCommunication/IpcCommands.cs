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
        Exit
    }
}
