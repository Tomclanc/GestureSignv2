using System;
using System.Drawing;

namespace GestureSign.Daemon.Surface;

internal static class GestureHintLayout
{
    // Return coordinates relative to the virtual-desktop overlay, while keeping
    // the hint inside one monitor's usable area (including negative origins).
    internal static RectangleF Place(Rectangle workArea, Point overlayOrigin, SizeF desiredSize)
    {
        var width = Math.Min(desiredSize.Width, Math.Max(1, workArea.Width - 24));
        var height = Math.Min(desiredSize.Height, Math.Max(1, workArea.Height - 24));
        return new RectangleF(
            workArea.Left - overlayOrigin.X + (workArea.Width - width) / 2f,
            workArea.Bottom - overlayOrigin.Y - height - 12f,
            width, height);
    }
}
