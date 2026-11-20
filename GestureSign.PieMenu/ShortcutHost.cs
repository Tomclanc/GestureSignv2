using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace GestureSign.PieMenu;

internal sealed class ShortcutHost : IDisposable
{
    private const uint HotkeyMessage = 0x0312;
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private readonly WindowProc _procedure;
    private readonly string _className = "GestureSign.PieMenu.Shortcuts";
    private readonly nint _module = GetModuleHandle(null);
    private readonly Action<MenuShortcut> _trigger;
    private readonly Dictionary<MenuShortcut, int> _registered = [];
    private nint _window;

    internal ShortcutHost(Action<MenuShortcut> trigger)
    {
        _trigger = trigger;
        _procedure = Dispatch;
        var windowClass = new WindowClass
        {
            Name = _className, Instance = _module, Procedure = Marshal.GetFunctionPointerForDelegate(_procedure)
        };
        if (RegisterClass(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _window = CreateWindowEx(0x80, _className, "", 0x80000000, 0, 0, 0, 0, 0, 0, _module, 0);
        if (_window != 0) return;
        var error = Marshal.GetLastWin32Error();
        UnregisterClass(_className, _module);
        throw new Win32Exception(error);
    }

    internal Dictionary<MenuShortcut, int> Bind(IEnumerable<MenuShortcut> shortcuts)
    {
        var requested = shortcuts.ToHashSet();
        foreach (var shortcut in _registered.Keys.Where(key => !requested.Contains(key)).ToArray())
        {
            UnregisterHotKey(_window, _registered[shortcut]);
            _registered.Remove(shortcut);
        }
        var failed = new Dictionary<MenuShortcut, int>();
        var ids = _registered.Values.ToHashSet();
        var id = 1;
        foreach (var shortcut in requested)
        {
            if (_registered.ContainsKey(shortcut)) continue;
            while (ids.Contains(id)) id++;
            const uint noRepeat = 0x4000;
            if (id <= 0xBFFF && RegisterHotKey(_window, id, shortcut.Modifiers | noRepeat, shortcut.Key))
            {
                _registered.Add(shortcut, id);
                ids.Add(id++);
            }
            else failed.Add(shortcut, id <= 0xBFFF ? Marshal.GetLastWin32Error() : 0);
        }
        return failed;
    }

    private nint Dispatch(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == HotkeyMessage)
        {
            var packed = unchecked((uint)lParam);
            var shortcut = new MenuShortcut(packed & 0xF, packed >> 16);
            if (_registered.TryGetValue(shortcut, out var id) && id == (int)wParam)
                _queue.TryEnqueue(() => _trigger(shortcut));
            return 0;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window == 0) return;
        Bind([]);
        DestroyWindow(_window);
        _window = 0;
        UnregisterClass(_className, _module);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? Menu, Name;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass entry);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extended, string name, string title, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
}
