using ManagedWinapi.Hooks;

namespace GestureSign.Common.Input
{
    // Optional capture context keeps existing IPointCapture implementations compatible.
    public interface IMouseGestureCapture
    {
        MouseActions MouseGestureButton { get; }
    }
}
