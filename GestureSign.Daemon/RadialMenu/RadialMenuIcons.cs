using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text.Json;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;

namespace GestureSign.Daemon.RadialMenu
{
    /// <summary>
    /// Picks an icon for a radial menu slot: the associated icon of a file or
    /// program referenced by the action's commands when there is one, otherwise
    /// a Segoe icon-font glyph chosen by the command's plugin type.
    /// </summary>
    internal static class RadialMenuIcons
    {
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
                foreach (var path in CandidatePaths(command.CommandSettings))
                {
                    if (FileIcons.TryGetValue(path, out var cached))
                    {
                        if (cached != null) return cached;
                        continue;
                    }
                    Bitmap bitmap = null;
                    try
                    {
                        if (File.Exists(path))
                        {
                            using var icon = Icon.ExtractAssociatedIcon(path);
                            bitmap = icon?.ToBitmap();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.LogMessage($"Radial menu icon extraction failed. Path={path}, Error={ex.GetType().Name}");
                    }
                    FileIcons[path] = bitmap;
                    if (bitmap != null) return bitmap;
                }
            }
            return null;
        }

        public static string Glyph(IAction action)
        {
            var command = Commands(action).FirstOrDefault();
            var plugin = command?.PluginClass ?? string.Empty;
            var name = plugin.Substring(plugin.LastIndexOf('.') + 1);
            return name switch
            {
                "HotKeyPlugin" or "SendKeystrokes" or "KeyDownKeyUpPlugin" or "TouchKeyboard" => "",
                "RunCommandPlugin" => "",
                "LaunchApp" => "",
                "OpenFilePlugin" => "",
                "DefaultBrowser" => "",
                "MouseActionsPlugin" => "",
                "VolumePlugin" => "",
                "MediaPlay" or "MediaPlayPause" => "",
                "MediaPause" => "",
                "MediaNext" or "MediaForward" => "",
                "MediaPrevious" or "MediaBackward" => "",
                "ScreenBrightnessPlugin" => "",
                "SmartClose" => "",
                "Minimize" => "",
                "MaximizeRestore" => "",
                "ToggleWindowTopmost" => "",
                "SmartNewTab" => "",
                "ActivateWindowPlugin" or "NextApplication" or "PreviousApplication" or "SendMessagePlugin" or "WindowStateCommand" => "",
                "Delay" => "",
                "TemporarilyDisable" or "ToggleDisableGestures" => "",
                "Notifier" => "",
                "ClipboardMatch" => "",
                "TextCopyer" => "",
                _ => ""
            };
        }

        private static IEnumerable<ICommand> Commands(IAction action)
            => action?.Commands?.Where(c => c != null && c.IsEnabled) ?? Enumerable.Empty<ICommand>();

        /// <summary>Existing local files mentioned anywhere in a command's settings.</summary>
        private static IEnumerable<string> CandidatePaths(string settings)
        {
            if (string.IsNullOrWhiteSpace(settings))
                yield break;

            var values = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(settings);
                Collect(document.RootElement, values);
            }
            catch (JsonException)
            {
                values.Add(settings);
            }

            foreach (var value in values)
            {
                var path = FirstPath(value);
                if (path != null)
                    yield return path;
            }
        }

        private static void Collect(JsonElement element, List<string> values)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    values.Add(element.GetString());
                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Collect(property.Value, values);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, values);
                    break;
            }
        }

        private static string FirstPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var text = Environment.ExpandEnvironmentVariables(value.Trim());
            string candidate;
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                candidate = end > 1 ? text.Substring(1, end - 1) : null;
            }
            else
            {
                // A command line may carry arguments after the program path.
                candidate = text;
                if (!LooksLikeLocalPath(candidate) || !File.Exists(candidate))
                {
                    var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    candidate = exe > 0 ? text.Substring(0, exe + 4) : null;
                }
            }
            return candidate != null && LooksLikeLocalPath(candidate) ? candidate : null;
        }

        // Skip UNC paths so a slow network share can never stall the mouse hook.
        private static bool LooksLikeLocalPath(string path)
            => path.Length > 3 && path.Length < 260 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }
}
