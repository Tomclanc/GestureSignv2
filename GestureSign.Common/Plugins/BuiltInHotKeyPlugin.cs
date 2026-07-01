using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.Common.Plugins
{
    internal sealed class BuiltInHotKeyPlugin : IPlugin
    {
        private HotKeySettings _settings;

        public string Name => "Send hotkey";
        public string Category => "Built-in";
        public string Description => "Send hotkey";
        public bool IsAction => true;
        public object GUI => null;
        public bool ActivateWindowDefault => true;
        public object Icon => null;
        public IHostControl HostControl { get; set; }

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        public void Initialize()
        {
        }

        public bool Deserialize(string serializedData)
        {
            return PluginHelper.DeserializeSettings(serializedData, out _settings);
        }

        public string Serialize()
        {
            return PluginHelper.SerializeSettings(_settings ?? new HotKeySettings());
        }

        public bool Gestured(PointInfo actionPoint)
        {
            if (_settings == null)
                return false;

            if (_settings.Windows && _settings.KeyCode != null && _settings.KeyCode.Count != 0 && _settings.KeyCode[0] == Keys.L)
            {
                LockWorkStation();
                return true;
            }

            SendShortcutKeys(_settings);
            return true;
        }

        private static void SendShortcutKeys(HotKeySettings settings)
        {
            var simulator = new InputSimulator();
            var modifiers = new List<VirtualKeyCode>();
            var keys = new List<VirtualKeyCode>();

            if (settings.Windows)
                modifiers.Add(VirtualKeyCode.LWIN);
            if (settings.Control)
                modifiers.Add(VirtualKeyCode.LCONTROL);
            if (settings.Alt)
                modifiers.Add(VirtualKeyCode.LMENU);
            if (settings.Shift)
                modifiers.Add(VirtualKeyCode.LSHIFT);

            if (settings.KeyCode != null)
            {
                foreach (var keyCode in settings.KeyCode)
                {
                    if (Enum.IsDefined(typeof(VirtualKeyCode), keyCode.GetHashCode()))
                        keys.Add((VirtualKeyCode)keyCode);
                }
            }

            if (modifiers.Count == 0)
            {
                if (keys.Count != 0)
                    simulator.Keyboard.KeyPress(keys.ToArray()).Sleep(30);
                return;
            }

            if (keys.Count != 0)
                simulator.Keyboard.ModifiedKeyStroke(modifiers, keys).Sleep(30);
            else
                simulator.Keyboard.KeyPress(modifiers.ToArray()).Sleep(30);
        }

        private sealed class HotKeySettings
        {
            public bool Windows { get; set; }
            public bool Control { get; set; }
            public bool Shift { get; set; }
            public bool Alt { get; set; }
            public List<Keys> KeyCode { get; set; }
            public bool SendByKeybdEvent { get; set; }
        }
    }
}
