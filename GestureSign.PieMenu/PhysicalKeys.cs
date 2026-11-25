using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GestureSign.PieMenu;

internal static class PhysicalKeys
{
    private static Dictionary<string, int>? _codes;
    private static string? _path;
    private static Dictionary<string, int> Codes
    {
        get
        {
            var path = PieMenuAssets.Web("windows-key-codes.json");
            if (_codes is not null && _path == path) return _codes;
            _codes = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path))!;
            _path = path;
            return _codes;
        }
    }

    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ControlLeft", "ControlRight", "ShiftLeft", "ShiftRight",
        "AltLeft", "AltRight", "MetaLeft", "MetaRight"
    };

    internal static bool IsModifier(string name) => Modifiers.Contains(name);

    internal static string? GetName(uint scanCode, bool extended, uint virtualKey)
    {
        var value = (int)(scanCode & 0xFF) | (extended ? 0xE000 : 0);
        var name = FindByCode(value);
        if (name is not null) return name;
        var mapped = (int)MapVirtualKey(virtualKey, 4);
        return mapped == 0 ? null : FindByCode(mapped);
    }

    private static string? FindByCode(int value) => Codes.FirstOrDefault(pair => pair.Value == value).Key;

    internal static bool TryNormalize(string value, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(value)) return true;
        var inputs = value.Split('+', StringSplitOptions.TrimEntries);
        var names = inputs.Select(FindName).ToArray();
        if (names.All(name => name is not null) && names.Count(name => !IsModifier(name!)) == 1)
        {
            canonical = string.Join("+", names.Distinct());
            return true;
        }
        // Keep the virtual-key names accepted by earlier preview builds.
        return MenuShortcut.TryParse(value, out _, out canonical);
    }

    private static string? FindName(string key) => Codes.Keys.FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));

    internal static bool TryResolve(IEnumerable<string> names, out int[] scanCodes)
    {
        var keys = names.Select(FindName).ToArray();
        scanCodes = keys.All(key => key is not null) ? keys.Select(key => Codes[key!]).ToArray() : [];
        return scanCodes.Length > 0;
    }

    internal static async Task Press(int[] codes, Action verifyFocus)
    {
        var held = new List<int>();
        try
        {
            foreach (var code in codes)
            {
                verifyFocus();
                Send(code, up: false);
                held.Add(code);
                await Task.Delay(10);
            }
            foreach (var code in codes)
            {
                Send(code, up: true);
                held.Remove(code);
                await Task.Delay(10);
            }
        }
        finally
        {
            foreach (var code in held) Send(code, up: true);
        }
    }

    private static void Send(int code, bool up)
    {
        const uint scanCode = 8, keyUp = 2, extended = 1;
        var input = new Input
        {
            Type = 1,
            Data = new InputData
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = (ushort)(code & 0xFF),
                    Flags = scanCode | (up ? keyUp : 0) | (code > 0xFF ? extended : 0)
                }
            }
        };
        if (SendInput(1, ref input, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Windows did not accept the simulated key.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputData Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey, ScanCode;
        internal uint Flags, Time;
        internal nuint ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, ref Input input, int size);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")] private static extern uint MapVirtualKey(uint key, uint type);
}
