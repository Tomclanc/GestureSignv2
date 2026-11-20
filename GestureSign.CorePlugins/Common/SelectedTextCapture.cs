using System;
using System.Diagnostics;
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
                Logging.LogMessage("Selected text capture: clipboard did not change; nothing selected or copy not supported.");
                return string.Empty;
            }
            // Some applications publish several formats in a row.
            Thread.Sleep(40);

            var text = string.Empty;
            pointInfo.Invoke(() =>
            {
                text = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty) ?? string.Empty;
                Retry(() =>
                {
                    if (backup != null)
                        Clipboard.SetDataObject(backup, true, 5, 50);
                    else if (!hadContent)
                        Clipboard.Clear();
                    return string.Empty;
                });
            });
            Logging.LogMessage($"Selected text captured. Length={text.Length}, ClipboardRestored={backup != null || !hadContent}");
            return text;
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
