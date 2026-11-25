using System;
using System.IO;
namespace GestureSign.PieMenu;

internal static class PieMenuAssets
{
    internal static string Root { get; set; } = AppContext.BaseDirectory;
    internal static string Editor(string name) => Path.Combine(Root, "EditorAssets", name);
    internal static string Web(string name) => Path.Combine(Root, "KandoWeb", name);
}
