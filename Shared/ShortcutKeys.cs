#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Shortcuts
{
    // Shared by the recorder, sender and global hotkey matcher. Legacy masks remain unchanged.
    public static class ShortcutKeys
    {
        public static bool IsModifier(int key) => key == 16 || key == 17 || key == 18 || key == 91 || key == 92 || (key >= 160 && key <= 165);
        public static int Normalize(int key, int scan, int flags) => key == 17 ? ((flags & 1) != 0 ? 163 : 162) : key == 18 ? ((flags & 1) != 0 ? 165 : 164) : key == 16 ? (scan == 54 ? 161 : 160) : key;
        public static int Flag(int key) => key == 17 || key == 162 || key == 163 ? 2 : key == 18 || key == 164 || key == 165 ? 1 : key == 16 || key == 160 || key == 161 ? 4 : key == 91 || key == 92 ? 8 : 0;
        public static int[] Resolve(int mask, IEnumerable<int> exact)
        {
            var result = (exact ?? Array.Empty<int>()).Where(IsModifier).Distinct().ToList();
            foreach (var key in new[] {164, 162, 160, 91})
                if ((mask & Flag(key)) != 0 && !result.Any(k => Flag(k) == Flag(key))) result.Add(key);
            return result.ToArray();
        }
        public static bool IsLockShortcut(int mask, IEnumerable<int> exact, IEnumerable<int> keys)
        {
            var codes = (keys ?? Array.Empty<int>()).ToArray();
            var modifiers = Resolve(mask, exact).Concat(codes.Where(IsModifier)).Distinct().ToArray();
            return modifiers.Length > 0 && modifiers.All(k => Flag(k) == 8)
                && codes.Where(k => !IsModifier(k)).SequenceEqual(new[] { 76 });
        }
        public static string Name(int key) => key == 162 ? "LCtrl" : key == 163 ? "RCtrl" : key == 160 ? "LShift" : key == 161 ? "RShift" : key == 164 ? "LAlt" : key == 165 ? "RAlt" : key == 91 ? "LWin" : key == 92 ? "RWin" : key == 17 ? "Ctrl" : key == 16 ? "Shift" : key == 18 ? "Alt" : null;
        public static bool Matches(int mask, IEnumerable<int> exact, Func<int, bool> down)
        {
            var required = (exact ?? Array.Empty<int>()).Where(IsModifier).ToArray();
            if (!required.All(k => k == 17 ? down(162) || down(163) : k == 16 ? down(160) || down(161) : k == 18 ? down(164) || down(165) : down(k))) return false;
            foreach (var pair in new[] {new[]{1,164,165},new[]{2,162,163},new[]{4,160,161},new[]{8,91,92}})
            {
                bool expected = (mask & pair[0]) != 0 || required.Any(k => Flag(k) == pair[0]);
                if (expected != (down(pair[1]) || down(pair[2]))) return false;
            }
            return true;
        }
    }
    public sealed class ModifierTapState
    {
        private readonly HashSet<int> held = new HashSet<int>();
        private int candidate;
        private bool used;
        public bool Process(int key, bool down, bool injected, int target)
        {
            if (injected) return false;
            if (down)
            {
                if (!held.Add(key)) return false;
                if (candidate != 0) used = true;
                if (key == target || (target == 17 && ShortcutKeys.Flag(key) == 2) || (target == 16 && ShortcutKeys.Flag(key) == 4) || (target == 18 && ShortcutKeys.Flag(key) == 1))
                { candidate = key; used = held.Count != 1; }
                return false;
            }
            held.Remove(key);
            bool fire = candidate == key && !used;
            if (candidate == key) candidate = 0;
            return fire;
        }
        public void Cancel() { used = true; }
        public void Reset() { held.Clear(); candidate = 0; used = false; }
    }
}

