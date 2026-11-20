using System.Runtime.InteropServices;

namespace GestureSign.PieMenu;

internal sealed unsafe class CompositionTarget : IDisposable
{
    private nint _device;
    private nint _target;
    private nint _visual;
    internal object RootVisual { get; }

    internal CompositionTarget(nint hwnd)
    {
        try
        {
            // IDCompositionDevice, IDCompositionTarget from the Windows SDK dcomp.h.
            var iid = new Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3");
            Check(DCompositionCreateDevice(0, in iid, out _device));
            var createTarget = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Slot(_device, 6);
            var createVisual = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(_device, 7);
            nint target = 0;
            Check(createTarget(_device, hwnd, 1, &target));
            _target = target;
            nint visual = 0;
            Check(createVisual(_device, &visual));
            _visual = visual;
            var setRoot = (delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(_target, 3);
            Check(setRoot(_target, _visual));
            RootVisual = Marshal.GetObjectForIUnknown(_visual);
            Commit();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Commit()
    {
        var commit = (delegate* unmanaged[Stdcall]<nint, int>)Slot(_device, 3);
        Check(commit(_device));
    }

    private static nint Slot(nint obj, int index) => ((nint*)*(nint*)obj)[index];
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    public void Dispose()
    {
        if (RootVisual is not null) Marshal.ReleaseComObject(RootVisual);
        if (_visual != 0) Marshal.Release(_visual);
        if (_target != 0) Marshal.Release(_target);
        if (_device != 0) Marshal.Release(_device);
        _visual = _target = _device = 0;
    }

    [DllImport("dcomp.dll", ExactSpelling = true)]
    private static extern int DCompositionCreateDevice(nint renderingDevice, in Guid iid, out nint device);
}
