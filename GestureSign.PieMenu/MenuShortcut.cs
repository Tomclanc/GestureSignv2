using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using WindowsInput.Native;

namespace GestureSign.PieMenu;

internal readonly record struct MenuShortcut(uint Modifiers, uint Key)
{
    private static readonly (string Name, uint Flag)[] ModifierNames =
        [("Control", 2), ("Shift", 4), ("Alt", 1), ("Meta", 8)];

    private static readonly Dictionary<string, uint> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = 2, ["Ctrl"] = 2, ["CommandOrControl"] = 2, ["CmdOrCtrl"] = 2,
        ["Shift"] = 4, ["Alt"] = 1, ["Option"] = 1, ["AltGr"] = 3,
        ["Command"] = 8, ["Cmd"] = 8, ["Super"] = 8, ["Meta"] = 8, ["Win"] = 8
    };

    private static readonly (string Name, VirtualKeyCode Code)[] KeyNames =
    [
        ("Space", VirtualKeyCode.SPACE), ("Tab", VirtualKeyCode.TAB), ("Capslock", VirtualKeyCode.CAPITAL),
        ("Numlock", VirtualKeyCode.NUMLOCK), ("Scrolllock", VirtualKeyCode.SCROLL), ("Backspace", VirtualKeyCode.BACK),
        ("Delete", VirtualKeyCode.DELETE), ("Insert", VirtualKeyCode.INSERT), ("Return", VirtualKeyCode.RETURN),
        ("Up", VirtualKeyCode.UP), ("Down", VirtualKeyCode.DOWN), ("Left", VirtualKeyCode.LEFT), ("Right", VirtualKeyCode.RIGHT),
        ("Home", VirtualKeyCode.HOME), ("End", VirtualKeyCode.END), ("PageUp", VirtualKeyCode.PRIOR),
        ("PageDown", VirtualKeyCode.NEXT), ("Escape", VirtualKeyCode.ESCAPE), ("PrintScreen", VirtualKeyCode.SNAPSHOT),
        ("VolumeUp", VirtualKeyCode.VOLUME_UP), ("VolumeDown", VirtualKeyCode.VOLUME_DOWN), ("VolumeMute", VirtualKeyCode.VOLUME_MUTE),
        ("MediaNextTrack", VirtualKeyCode.MEDIA_NEXT_TRACK), ("MediaPreviousTrack", VirtualKeyCode.MEDIA_PREV_TRACK),
        ("MediaStop", VirtualKeyCode.MEDIA_STOP), ("MediaPlayPause", VirtualKeyCode.MEDIA_PLAY_PAUSE),
        ("numdec", VirtualKeyCode.DECIMAL), ("numadd", VirtualKeyCode.ADD), ("numsub", VirtualKeyCode.SUBTRACT),
        ("nummult", VirtualKeyCode.MULTIPLY), ("numdiv", VirtualKeyCode.DIVIDE)
    ];

    internal static bool TryParse(string text, out MenuShortcut shortcut, out string canonical, bool allowModifiers = true)
    {
        shortcut = default;
        canonical = "";
        text = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        if (text.Length == 0) return true;
        if (text == "+") text = "Plus";
        uint modifiers = 0;
        uint required = 0;
        uint key = 0;
        string? name = null;
        foreach (var part in text.Split('+'))
        {
            if (allowModifiers && ModifierAliases.TryGetValue(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }
            if (key != 0 || !TryKey(part, out key, out required, out name)) return false;
        }
        if (key == 0) return false;
        shortcut = new(modifiers | required, key);
        canonical = Format(modifiers, name!);
        return true;
    }

    internal static string Format(uint modifiers, string key) =>
        string.Join("+", ModifierNames.Where(entry => (modifiers & entry.Flag) != 0)
            .Select(entry => entry.Name).Append(key));

    internal static string? FromVirtualKey(uint key)
    {
        if (key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)key).ToString();
        if (key is >= 0x70 and <= 0x87) return "F" + (key - 0x6F);
        if (key is >= 0x60 and <= 0x69) return "num" + (key - 0x60);
        foreach (var entry in KeyNames)
            if (key == (uint)entry.Code) return entry.Name;
        var character = MapVirtualKey(key, 2) & 0x7FFF;
        return character is >= 33 and <= 126 ? character == '+' ? "Plus" : ((char)character).ToString() : null;
    }

    internal static uint CurrentModifiers() =>
        (Down(0x11) ? 2u : 0u) | (Down(0x10) ? 4u : 0u) |
        (Down(0x12) ? 1u : 0u) | (Down(0x5B) || Down(0x5C) ? 8u : 0u);

    private static bool Down(int key) => GetKeyState(key) < 0;

    private static bool TryKey(string part, out uint key, out uint modifiers, out string? name)
    {
        key = modifiers = 0;
        name = null;
        if (part.Equals("Enter", StringComparison.OrdinalIgnoreCase)) part = "Return";
        if (part.Equals("Esc", StringComparison.OrdinalIgnoreCase)) part = "Escape";
        foreach (var entry in KeyNames)
        {
            if (!entry.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) continue;
            key = (uint)entry.Code;
            name = entry.Name;
            return true;
        }
        if (part.StartsWith("num", StringComparison.OrdinalIgnoreCase) && part.Length == 4 && char.IsAsciiDigit(part[3]))
        {
            key = (uint)(0x60 + part[3] - '0');
            name = "num" + part[3];
            return true;
        }
        if (part.Length is 2 or 3 && char.ToUpperInvariant(part[0]) == 'F' &&
            uint.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn) && fn is >= 1 and <= 24)
        {
            key = 0x6F + fn;
            name = "F" + fn;
            return true;
        }
        var character = part.Equals("Plus", StringComparison.OrdinalIgnoreCase) ? '+' : part.Length == 1 ? part[0] : '\0';
        if (character is < '!' or > '~') return false;
        if (char.IsAsciiLetterOrDigit(character))
        {
            key = char.ToUpperInvariant(character);
            name = ((char)key).ToString();
            return true;
        }
        var mapped = VkKeyScan(character);
        if (mapped == -1 || (mapped >> 8) > 7) return false;
        key = (uint)(mapped & 0xFF);
        modifiers = (uint)(((mapped >> 8) & 1) << 2 | ((mapped >> 8) & 2) | (((mapped >> 8) & 4) >> 2));
        name = character == '+' ? "Plus" : character.ToString();
        return true;
    }

    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")] private static extern uint MapVirtualKey(uint key, uint type);
    [DllImport("user32.dll", EntryPoint = "VkKeyScanW", CharSet = CharSet.Unicode)] private static extern short VkKeyScan(char key);
}
