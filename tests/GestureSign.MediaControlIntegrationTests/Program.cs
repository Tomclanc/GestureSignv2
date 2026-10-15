using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Control;
using GestureSign.CorePlugins.MediaControl;

if (!args.Contains("--run-integration", StringComparer.Ordinal))
{
    Console.WriteLine("Skipped: pass --run-integration to create an isolated silent Windows media session. Existing media sessions are never controlled.");
    return;
}
var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
if (manager.GetSessions().Count != 0) throw new Exception("Existing media session present; refusing to interfere.");
var path = Path.Combine(Path.GetTempPath(), "GestureSign-media-test-" + Guid.NewGuid() + ".wav");
using (var stream = File.Create(path))
using (var w = new BinaryWriter(stream))
{
    var count = 8000 * 2 * 60;
    w.Write("RIFF"u8.ToArray()); w.Write(36 + count); w.Write("WAVEfmt "u8.ToArray()); w.Write(16);
    w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8.ToArray()); w.Write(count); w.Write(new byte[count]);
}
try
{
    using var player = new MediaPlayer { Volume = 0 };
    player.CommandManager.IsEnabled = false;
    player.Source = MediaSource.CreateFromUri(new Uri(path));
    var controls = player.SystemMediaTransportControls;
    controls.IsEnabled = true;
    controls.IsPlayEnabled = controls.IsPauseEnabled = controls.IsNextEnabled = controls.IsPreviousEnabled = true;
    controls.DisplayUpdater.Type = MediaPlaybackType.Music;
    controls.DisplayUpdater.MusicProperties.Title = "GestureSign isolated silent media test";
    controls.DisplayUpdater.Update();
    controls.PlaybackStatus = MediaPlaybackStatus.Playing;
    int play = 0, pause = 0, next = 0, previous = 0, seek = 0;
    var positions = new System.Collections.Concurrent.ConcurrentQueue<TimeSpan>();
    controls.ButtonPressed += (_, a) =>
    {
        switch (a.Button)
        {
            case SystemMediaTransportControlsButton.Play: Interlocked.Increment(ref play); controls.PlaybackStatus = MediaPlaybackStatus.Playing; break;
            case SystemMediaTransportControlsButton.Pause: Interlocked.Increment(ref pause); controls.PlaybackStatus = MediaPlaybackStatus.Paused; break;
            case SystemMediaTransportControlsButton.Next: Interlocked.Increment(ref next); break;
            case SystemMediaTransportControlsButton.Previous: Interlocked.Increment(ref previous); break;
        }
    };
    controls.PlaybackPositionChangeRequested += (_, a) => { positions.Enqueue(a.RequestedPlaybackPosition); Interlocked.Increment(ref seek); };
    controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
    { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(60), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(60), Position = TimeSpan.FromSeconds(30) });
    player.Play();
    for (int i = 0; i < 30 && manager.GetCurrentSession() is null; i++) await Task.Delay(100);
    if (manager.GetCurrentSession() is null) throw new Exception("The isolated test player did not expose a system media session.");
    if (!new MediaPause().Gestured(null)) throw new Exception("Pause was rejected");
    await Task.Delay(200);
    if (pause != 1) throw new Exception("Pause did not reach isolated player");
    if (!new MediaPause().Gestured(null)) throw new Exception("Repeated pause was rejected");
    await Task.Delay(100);
    if (pause != 1) throw new Exception("Repeated pause toggled or sent a duplicate command");
    if (!new MediaPlay().Gestured(null)) throw new Exception("Play was rejected");
    await Task.Delay(200);
    if (play != 1) throw new Exception("Play did not reach isolated player");
    if (!new MediaPlay().Gestured(null)) throw new Exception("Repeated play was rejected");
    await Task.Delay(100);
    if (play != 1) throw new Exception("Repeated play toggled or sent a duplicate command");
    if (!new MediaNext().Gestured(null) || !new MediaPrevious().Gestured(null)) throw new Exception("Track command rejected");
    await Task.Delay(200);
    if (next != 1 || previous != 1) throw new Exception("Track commands did not reach isolated player");
    controls.PlaybackStatus = MediaPlaybackStatus.Paused;
    controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
    { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(60), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(60), Position = TimeSpan.FromSeconds(30) });
    await Task.Delay(200);
    if (!new MediaForward().Gestured(null) || !new MediaBackward().Gestured(null)) throw new Exception("Seek command rejected");
    await Task.Delay(200);
    if (!positions.TryDequeue(out var forward) || !positions.TryDequeue(out var backward)
        || forward != TimeSpan.FromSeconds(40) || backward != TimeSpan.FromSeconds(20))
        throw new Exception("Seek commands did not request the expected 10-second positions");
    Console.WriteLine($"PASS: isolated real Windows media session; play={play}, pause={pause}, next={next}, previous={previous}, seek-events={seek}");
    player.Pause(); controls.PlaybackStatus = MediaPlaybackStatus.Closed; controls.IsEnabled = false;
}
finally { File.Delete(path); }
