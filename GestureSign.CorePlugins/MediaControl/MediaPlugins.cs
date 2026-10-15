using GestureSign.Common.Plugins;
using GestureSign.Foundation;

namespace GestureSign.CorePlugins.MediaControl;

public sealed class MediaPlay : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Play);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Play);
}

public sealed class MediaPause : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Pause);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Pause);
}

public sealed class MediaForward : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Forward);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Forward);
}

public sealed class MediaBackward : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Backward);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Backward);
}

public sealed class MediaNext : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Next);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Next);
}

public sealed class MediaPrevious : IPlugin
{
    public string Name => MediaCommand.Name(MediaOperation.Previous);
    public string Description => Name;
    public string Category => MediaCommand.Text("Media Controls");
    public bool IsAction => true;
    public object GUI => null;
    public bool ActivateWindowDefault => false;
    public object Icon => IconSource.Volume;
    public IHostControl HostControl { get; set; }
    public void Initialize() { }
    public bool Deserialize(string settings) => true;
    public string Serialize() => "";
    public bool Gestured(PointInfo point) => MediaCommand.Execute(MediaOperation.Previous);
}
