namespace GestureSign.Common.Applications
{
    /// <summary>A radial menu slot that opens a submenu instead of running commands.</summary>
    public sealed class RadialMenuFolderAction : Action
    {
        public string SubmenuId { get; set; }
    }
}
