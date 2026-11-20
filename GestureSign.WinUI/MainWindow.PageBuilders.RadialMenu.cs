using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private string RadialMenuSlotLabel(LegacyAction action)
    {
        if (!IsGenericRadialMenuLabel(action.Name))
            return action.Name;
        var command = action.Commands.FirstOrDefault();
        return command is null ? action.Name : RadialMenuAutoLabel(command.PluginClass, command.Settings);
    }

    // Names the command editor used to prefill; they say nothing about what a
    // slot does, so they are treated as "no name" for wheel labels.
    private static bool IsGenericRadialMenuLabel(string? name)
        => string.IsNullOrWhiteSpace(name) ||
           name.Trim() is "发送快捷键" or "Send Hotkey" or "發送快速鍵" or "단축키 보내기";

    private string RadialMenuAutoLabel(string pluginClass, string settings)
    {
        var hotKey = HotKeyDisplayText(settings);
        if (!string.IsNullOrWhiteSpace(hotKey))
            return hotKey;
        var file = RadialMenuFileLabel(settings);
        return !string.IsNullOrWhiteSpace(file) ? file : PluginName(pluginClass);
    }

    private static string? RadialMenuFileLabel(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return null;
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(settings, @"[A-Za-z]:(?:\\\\|\\|/)[^""<>|*?\r\n]+"))
        {
            var path = match.Value.Replace(@"\\", @"\").Trim();
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return null;
    }

    private ToggleSwitch NewRadialMenuToggle(bool isOn) => new()
    {
        IsOn = isOn,
        OnContent = L("开", "On", "開", "켬"),
        OffContent = L("关", "Off", "關", "끔"),
        VerticalAlignment = VerticalAlignment.Center
    };
}
