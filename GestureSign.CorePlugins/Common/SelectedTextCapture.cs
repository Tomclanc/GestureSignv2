using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.CorePlugins.Common
{
    /// <summary>
    /// Reads the text selected in the foreground window by sending Ctrl+C,
    /// then puts the previous clipboard content back.
    /// </summary>
    internal static class SelectedTextCapture
    {
        private const int CopyTimeoutMs = 600;

        // Formats restored after the copy. Delay-rendered or private formats
        // are skipped: re-rendering them can block or fail once the source
        // application no longer owns the clipboard.
        private static readonly string[] RestoredFormats =
        {
            DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html,
            DataFormats.FileDrop, DataFormats.Bitmap
        };

        public static string Capture(PointInfo pointInfo)
        {
            var text = CaptureWith(pointInfo, () => Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty), "text");
            Logging.LogMessage($"Selected text captured. Length={text?.Length ?? 0}");
            return text ?? string.Empty;
        }

        /// <summary>Copies the selection with Ctrl+C and returns the file paths it put on the clipboard.</summary>
        public static string[] CaptureFiles(PointInfo pointInfo)
        {
            var files = CaptureWith(pointInfo, () =>
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        return Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToArray() : Array.Empty<string>();
                    }
                    catch (ExternalException)
                    {
                        Thread.Sleep(30);
                    }
                }
                return Array.Empty<string>();
            }, "files");
            return files ?? Array.Empty<string>();
        }

        /// <summary>Sends Ctrl+C, reads the clipboard with read on the UI thread, then restores the clipboard.</summary>
        private static T CaptureWith<T>(PointInfo pointInfo, Func<T> read, string what) where T : class
        {
            DataObject backup = null;
            var hadContent = false;
            pointInfo.Invoke(() => backup = Snapshot(out hadContent));

            var before = GetClipboardSequenceNumber();
            new InputSimulator().Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

            var stopwatch = Stopwatch.StartNew();
            while (GetClipboardSequenceNumber() == before && stopwatch.ElapsedMilliseconds < CopyTimeoutMs)
                Thread.Sleep(15);
            if (GetClipboardSequenceNumber() == before)
            {
                Logging.LogMessage($"Selection capture ({what}): clipboard did not change; nothing selected or copy not supported.");
                return null;
            }
            // Some applications publish several formats in a row.
            Thread.Sleep(40);

            T result = null;
            pointInfo.Invoke(() =>
            {
                result = read();
                Retry(() =>
                {
                    if (backup != null)
                        Clipboard.SetDataObject(backup, true, 5, 50);
                    else if (!hadContent)
                        Clipboard.Clear();
                    return string.Empty;
                });
            });
            return result;
        }

        /// <summary>
        /// Pastes text into the foreground window with Ctrl+V and then puts the
        /// previous clipboard content back.
        /// </summary>
        public static void Paste(PointInfo pointInfo, string text)
        {
            DataObject backup = null;
            var hadContent = false;
            pointInfo.Invoke(() =>
            {
                backup = Snapshot(out hadContent);
                Retry(() =>
                {
                    if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                    else Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, 5, 50);
                    return string.Empty;
                });
            });
            new InputSimulator().Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
            // The target reads the clipboard asynchronously after Ctrl+V.
            Thread.Sleep(250);
            pointInfo.Invoke(() => Retry(() =>
            {
                if (backup != null)
                    Clipboard.SetDataObject(backup, true, 5, 50);
                else if (!hadContent)
                    Clipboard.Clear();
                return string.Empty;
            }));
        }

        public static string GetText(PointInfo pointInfo)
        {
            var text = string.Empty;
            pointInfo.Invoke(() => text = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty) ?? string.Empty);
            return text;
        }

        public static void SetText(PointInfo pointInfo, string text)
        {
            pointInfo.Invoke(() => Retry(() =>
            {
                if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                else Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, 5, 50);
                return string.Empty;
            }));
        }

        private static DataObject Snapshot(out bool hadContent)
        {
            hadContent = false;
            try
            {
                var source = Clipboard.GetDataObject();
                if (source == null)
                    return null;
                hadContent = source.GetFormats(false).Length > 0;
                var copy = new DataObject();
                var any = false;
                foreach (var format in RestoredFormats)
                {
                    try
                    {
                        if (!source.GetDataPresent(format, false))
                            continue;
                        var data = source.GetData(format, false);
                        if (data == null)
                            continue;
                        copy.SetData(format, data);
                        any = true;
                    }
                    catch (Exception)
                    {
                        // A single unreadable format must not lose the others.
                    }
                }
                return any ? copy : null;
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Selected text capture: clipboard backup failed. Error={ex.GetType().Name}");
                return null;
            }
        }

        private static string Retry(Func<string> operation)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (ExternalException)
                {
                    Thread.Sleep(30);
                }
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();
    }
}
