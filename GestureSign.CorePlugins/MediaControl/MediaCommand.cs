using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using GestureSign.Foundation;
using GestureSign.Foundation.Localization;
using Windows.Media.Control;

namespace GestureSign.CorePlugins.MediaControl;

internal static class MediaCommand
{
    private static readonly object Sync = new();
    private static Task<GlobalSystemMediaTransportControlsSessionManager> _manager;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    public static string Name(MediaOperation operation) => Text(operation switch
    {
        MediaOperation.Play => "Media Play", MediaOperation.Pause => "Media Pause",
        MediaOperation.Forward => "Media Fast Forward", MediaOperation.Backward => "Media Rewind",
        MediaOperation.Next => "Media Next Track", MediaOperation.Previous => "Media Previous Track",
        _ => "Media Controls"
    });
    public static string Text(string key) => IntentLocalization.Format(
        string.IsNullOrWhiteSpace(AppConfig.CultureName) ? CultureInfo.CurrentUICulture.Name : AppConfig.CultureName, key);

    public static bool Execute(MediaOperation operation)
    {
        try { return ExecuteAsync(operation).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            // Once a command has been dispatched, do not send another command
            // on timeout: the player may already have processed the first one.
            Logging.LogMessage($"Media control failed. Operation={operation}, Error={error.Message}");
            return false;
        }
    }

    private static async Task<bool> ExecuteAsync(MediaOperation operation)
    {
        GlobalSystemMediaTransportControlsSession session = null;
        try
        {
            Task<GlobalSystemMediaTransportControlsSessionManager> manager;
            lock (Sync)
            {
                if (_manager == null || _manager.IsFaulted || _manager.IsCanceled)
                    _manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                manager = _manager;
            }
            session = (await manager.WaitAsync(Timeout).ConfigureAwait(false)).GetCurrentSession();
        }
        catch (Exception error)
        {
            Logging.LogMessage($"Media session unavailable. Operation={operation}, Error={error.Message}");
        }
        if (session == null) return SendLegacyCommand(operation);

        var info = session.GetPlaybackInfo();
        var controls = info.Controls;
        Task<bool> request;
        switch (operation)
        {
            case MediaOperation.Play:
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return true;
                if (!controls.IsPlayEnabled) return Unsupported(operation);
                request = session.TryPlayAsync().AsTask(); break;
            case MediaOperation.Pause:
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) return true;
                if (!controls.IsPauseEnabled) return Unsupported(operation);
                request = session.TryPauseAsync().AsTask(); break;
            case MediaOperation.Next:
                if (!controls.IsNextEnabled) return Unsupported(operation);
                request = session.TrySkipNextAsync().AsTask(); break;
            case MediaOperation.Previous:
                if (!controls.IsPreviousEnabled) return Unsupported(operation);
                request = session.TrySkipPreviousAsync().AsTask(); break;
            default:
                if (controls.IsPlaybackPositionEnabled)
                {
                    var timeline = session.GetTimelineProperties();
                    var minimum = Math.Max(timeline.StartTime.Ticks, timeline.MinSeekTime.Ticks);
                    var maximum = timeline.MaxSeekTime.Ticks;
                    // Some players provide duration but leave MaxSeekTime at zero.
                    if (maximum <= minimum) maximum = timeline.EndTime.Ticks;
                    else if (timeline.EndTime.Ticks > minimum) maximum = Math.Min(maximum, timeline.EndTime.Ticks);
                    if (maximum <= minimum) return Unsupported(operation);
                    var position = MediaControlPolicy.EstimatePosition(timeline.Position.Ticks, minimum, maximum,
                        DateTimeOffset.UtcNow - timeline.LastUpdatedTime, info.PlaybackRate ?? 1.0,
                        info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                    var target = MediaControlPolicy.SeekTarget(position, minimum, maximum, operation == MediaOperation.Forward);
                    request = session.TryChangePlaybackPositionAsync(target).AsTask();
                }
                else if (operation == MediaOperation.Forward && controls.IsFastForwardEnabled)
                    request = session.TryFastForwardAsync().AsTask();
                else if (operation == MediaOperation.Backward && controls.IsRewindEnabled)
                    request = session.TryRewindAsync().AsTask();
                else return Unsupported(operation);
                break;
        }
        var result = await request.WaitAsync(Timeout).ConfigureAwait(false);
        Logging.LogMessage($"Media control completed. Operation={operation}, Backend=SystemMediaSession, Success={result}");
        return result;
    }

    private static bool Unsupported(MediaOperation operation)
    {
        Logging.LogMessage($"Media control skipped. Operation={operation}, Reason=PlayerDoesNotSupportOperation");
        return false;
    }

    private static bool SendLegacyCommand(MediaOperation operation)
    {
        var window = GetForegroundWindow();
        var sent = window != IntPtr.Zero && SendMessageTimeout(window, 0x0319, window,
            new IntPtr(MediaControlPolicy.AppCommand(operation) << 16), 0x0002, 250, out var handled) != IntPtr.Zero
            && handled != IntPtr.Zero;
        Logging.LogMessage($"Media control completed. Operation={operation}, Backend=AppCommand, Success={sent}");
        return sent;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
