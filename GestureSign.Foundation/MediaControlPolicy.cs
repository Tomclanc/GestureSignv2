namespace GestureSign.Foundation;

public enum MediaOperation { Play, Pause, Forward, Backward, Next, Previous, PlayPause }

public static class MediaControlPolicy
{
    public const int SeekSeconds = 10;
    public static long EstimatePosition(long position, long minimum, long maximum, TimeSpan elapsed, double rate, bool playing)
    {
        if (maximum < minimum) maximum = minimum;
        if (!playing || elapsed <= TimeSpan.Zero || !double.IsFinite(rate) || rate <= 0)
            return Math.Clamp(position, minimum, maximum);
        var estimated = position + elapsed.Ticks * rate;
        if (estimated >= maximum) return maximum;
        if (estimated <= minimum) return minimum;
        return (long)estimated;
    }
    public static long SeekTarget(long position, long minimum, long maximum, bool forward)
    {
        if (maximum < minimum) maximum = minimum;
        position = Math.Clamp(position, minimum, maximum);
        var step = TimeSpan.FromSeconds(SeekSeconds).Ticks;
        // Compare before adding/subtracting, avoiding overflow at timeline limits.
        return forward
            ? position > long.MaxValue - step ? maximum : Math.Min(maximum, position + step)
            : position < long.MinValue + step ? minimum : Math.Max(minimum, position - step);
    }
    public static int AppCommand(MediaOperation operation) => operation switch
    {
        MediaOperation.PlayPause => 14,
        MediaOperation.Play => 46, MediaOperation.Pause => 47,
        MediaOperation.Forward => 49, MediaOperation.Backward => 50,
        MediaOperation.Next => 11, MediaOperation.Previous => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
