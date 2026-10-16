using GestureSign.Foundation;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
long T(double seconds) => TimeSpan.FromSeconds(seconds).Ticks;
Check(MediaControlPolicy.SeekTarget(T(30), T(0), T(100), true) == T(40), "Seek forward 10 seconds");
Check(MediaControlPolicy.SeekTarget(T(30), T(0), T(100), false) == T(20), "Seek backward 10 seconds");
Check(MediaControlPolicy.SeekTarget(T(95), T(0), T(100), true) == T(100), "Do not seek beyond track end");
Check(MediaControlPolicy.SeekTarget(T(5), T(0), T(100), false) == T(0), "Do not seek before track start");
Check(MediaControlPolicy.SeekTarget(T(35), T(30), T(100), false) == T(30), "Respect live stream seek boundary");
Check(MediaControlPolicy.SeekTarget(long.MaxValue - 1, 0, long.MaxValue, true) == long.MaxValue, "No forward overflow");
Check(MediaControlPolicy.SeekTarget(long.MinValue + 1, long.MinValue, 0, false) == long.MinValue, "No backward overflow");
Check(MediaControlPolicy.EstimatePosition(T(20), 0, T(100), TimeSpan.FromSeconds(4), 1.5, true) == T(26), "Account for elapsed time and playback speed");
Check(MediaControlPolicy.EstimatePosition(T(20), 0, T(100), TimeSpan.FromSeconds(4), 1.5, false) == T(20), "Paused tracks do not advance");
Check(MediaControlPolicy.EstimatePosition(T(98), 0, T(100), TimeSpan.FromSeconds(4), 1, true) == T(100), "Clamp estimated progress");
Check(MediaControlPolicy.EstimatePosition(T(20), 0, T(100), TimeSpan.FromSeconds(-4), 1, true) == T(20), "Ignore future timestamps");
Check(MediaControlPolicy.EstimatePosition(T(20), 0, T(100), TimeSpan.FromSeconds(4), double.NaN, true) == T(20), "Ignore invalid playback rate");
Check(MediaControlPolicy.AppCommand(MediaOperation.Play) != 14 && MediaControlPolicy.AppCommand(MediaOperation.Pause) != 14,
    "Independent play and pause must never send the toggle command");
Check(MediaControlPolicy.AppCommand(MediaOperation.Next) != MediaControlPolicy.AppCommand(MediaOperation.Forward), "Next track is not seeking");
Check(MediaControlPolicy.AppCommand(MediaOperation.Previous) != MediaControlPolicy.AppCommand(MediaOperation.Backward), "Previous track is not rewinding");
Check(MediaControlPolicy.AppCommand(MediaOperation.PlayPause) == 14, "Combined play/pause uses the native toggle command");
Console.WriteLine($"PASS: {checks} media seek, timing and discrete-command checks.");
