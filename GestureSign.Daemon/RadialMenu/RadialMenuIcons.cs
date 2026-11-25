using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using GestureSign.Foundation.RadialMenus;

namespace GestureSign.Daemon.RadialMenu
{
    /// <summary>
    /// Picks an icon for a radial menu slot: the associated icon of a file or
    /// program referenced by the action's commands when there is one, otherwise
    /// a Segoe icon-font glyph chosen by the command's plugin type.
    /// </summary>
    internal static class RadialMenuIcons
    {
        /// <summary>Icons are read at 48 px and drawn at ~22 px, which keeps them sharp on every DPI.</summary>
        private const int IconSize = 48;

        private static readonly Dictionary<string, Bitmap> FileIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static string _glyphFontName;
        private static bool _glyphFontResolved;

        public static string GlyphFontName
        {
            get
            {
                if (_glyphFontResolved)
                    return _glyphFontName;
                _glyphFontResolved = true;
                try
                {
                    using var fonts = new InstalledFontCollection();
                    var installed = new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                    _glyphFontName = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }.FirstOrDefault(installed.Contains);
                }
                catch (Exception ex)
                {
                    Logging.LogException(ex);
                }
                return _glyphFontName;
            }
        }

        public static Bitmap FileIcon(IAction action)
        {
            foreach (var command in Commands(action))
            {
                foreach (var path in ActionIcons.ReferencedPaths(command.CommandSettings))
                {
                    var bitmap = FileIcon(path);
                    if (bitmap != null) return bitmap;
                }
            }
            return null;
        }

        /// <summary>
        /// Icon of a program, shortcut or document (the picture itself for an image
        /// file); cached. Shortcuts are read through their target, so the icon has no
        /// shortcut arrow on it.
        /// </summary>
        public static Bitmap FileIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (FileIcons.TryGetValue(path, out var cached))
                return cached;
            Bitmap bitmap = null;
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    if (ActionIcons.IsImageFile(path))
                    {
                        using var image = Image.FromFile(path);
                        bitmap = new Bitmap(image, 64, 64);
                    }
                    else if (ActionIcons.TryGetIconPixels(path, 0, IconSize, out var pixels))
                    {
                        bitmap = ToBitmap(pixels);
                    }
                    else
                    {
                        using var icon = Icon.ExtractAssociatedIcon(path);
                        bitmap = icon?.ToBitmap();
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Radial menu icon extraction failed. Path={path}, Error={ex.GetType().Name}");
            }
            FileIcons[path] = bitmap;
            return bitmap;
        }

        private static Bitmap ToBitmap(IconPixels pixels)
        {
            var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < pixels.Height; y++)
                    Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, data.Scan0 + y * data.Stride, pixels.Width * 4);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        public static string Glyph(IAction action)
        {
            var command = Commands(action).FirstOrDefault();
            return ActionIcons.PluginGlyph(command?.PluginClass);
        }

        /// <summary>Names the editor used to prefill; they do not describe a slot.</summary>
        public static bool IsGenericLabel(string name)
            => string.IsNullOrWhiteSpace(name) ||
               name.Trim() is "发送快捷键" or "Send Hotkey" or "發送快速鍵" or "단축키 보내기";

        /// <summary>A label derived from the first command: hotkey text or referenced file name.</summary>
        public static string AutoLabel(IAction action)
        {
            var command = Commands(action).FirstOrDefault();
            if (command == null)
                return null;
            var hotKey = HotKeyText(command.CommandSettings);
            if (!string.IsNullOrEmpty(hotKey))
                return hotKey;
            var path = ActionIcons.ReferencedPaths(command.CommandSettings).FirstOrDefault();
            if (path != null)
                return Path.GetFileNameWithoutExtension(path);
            var plugin = command.PluginClass ?? string.Empty;
            return plugin.Substring(plugin.LastIndexOf('.') + 1);
        }

        private static string HotKeyText(string settings)
        {
            if (string.IsNullOrWhiteSpace(settings))
                return null;
            try
            {
                using var document = JsonDocument.Parse(settings);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("KeyCode", out var codes) || codes.ValueKind != JsonValueKind.Array)
                    return null;
                bool Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
                var parts = new List<string>();
                if (Flag("Control")) parts.Add("Ctrl");
                if (Flag("Shift")) parts.Add("Shift");
                if (Flag("Alt")) parts.Add("Alt");
                if (Flag("Windows")) parts.Add("Win");
                foreach (var code in codes.EnumerateArray())
                {
                    if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var value))
                        continue;
                    parts.Add(KeyName((System.Windows.Forms.Keys)value));
                }
                return parts.Count == 0 ? null : string.Join("+", parts.Distinct());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string KeyName(System.Windows.Forms.Keys key)
        {
            if (key >= System.Windows.Forms.Keys.D0 && key <= System.Windows.Forms.Keys.D9)
                return ((int)(key - System.Windows.Forms.Keys.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return key switch
            {
                System.Windows.Forms.Keys.Left => "←",
                System.Windows.Forms.Keys.Right => "→",
                System.Windows.Forms.Keys.Up => "↑",
                System.Windows.Forms.Keys.Down => "↓",
                System.Windows.Forms.Keys.Return => "Enter",
                System.Windows.Forms.Keys.Escape => "Esc",
                System.Windows.Forms.Keys.Back => "Backspace",
                System.Windows.Forms.Keys.Delete => "Del",
                System.Windows.Forms.Keys.Next => "PageDown",
                System.Windows.Forms.Keys.Prior => "PageUp",
                System.Windows.Forms.Keys.LControlKey or System.Windows.Forms.Keys.RControlKey or System.Windows.Forms.Keys.ControlKey => "Ctrl",
                System.Windows.Forms.Keys.LShiftKey or System.Windows.Forms.Keys.RShiftKey or System.Windows.Forms.Keys.ShiftKey => "Shift",
                System.Windows.Forms.Keys.LMenu or System.Windows.Forms.Keys.RMenu or System.Windows.Forms.Keys.Menu => "Alt",
                System.Windows.Forms.Keys.LWin or System.Windows.Forms.Keys.RWin => "Win",
                _ => key.ToString()
            };
        }

        private static IEnumerable<ICommand> Commands(IAction action)
            => action?.Commands?.Where(c => c != null && c.IsEnabled) ?? Enumerable.Empty<ICommand>();
    }
}
