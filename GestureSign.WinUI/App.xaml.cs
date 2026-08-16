using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GestureSign.WinUI;

public partial class App : Application
{
    private const string SingleInstanceKeyBase = "GestureSign.WinUI.Settings";
    private const string SingleInstanceMutexNameBase = "Local\\TransposonY.GestureSign.WinUI.Settings";
    private const string LegacySingleInstanceMutexNameBase = "Local\\GestureSignWinUI";
    private const string SettingsWindowLockNameBase = "GestureSign.WinUI.Settings";
    private const int SW_RESTORE = 9;
    private const int WM_CLOSE = 0x0010;
    private const uint GW_OWNER = 4;
    private static readonly string InstanceScope = CreateInstanceScope();

    private static bool s_launched;
    private Mutex? _singleInstanceMutex;
    private Mutex? _legacySingleInstanceMutex;
    private FileStream? _settingsWindowLock;
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (s_launched)
        {
            ActivateCurrentWindow();
            return;
        }

        s_launched = true;

        var currentInstance = AppInstance.GetCurrent();
        var mainInstance = AppInstance.FindOrRegisterForKey($"{SingleInstanceKeyBase}.{InstanceScope}");
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
            Exit();
            return;
        }

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        mainInstance.Activated += (_, _) => dispatcherQueue.TryEnqueue(ActivateCurrentWindow);

        if (ActivateExistingWindow(closeDuplicates: true))
        {
            Exit();
            return;
        }

        _singleInstanceMutex = new Mutex(true, $"{SingleInstanceMutexNameBase}.{InstanceScope}", out var createdNew);
        _legacySingleInstanceMutex = new Mutex(true, $"{LegacySingleInstanceMutexNameBase}.{InstanceScope}", out var legacyCreatedNew);
        if (!createdNew || !legacyCreatedNew)
        {
            ActivateExistingWindow(closeDuplicates: true);
            Exit();
            return;
        }

        if (!TryAcquireSettingsWindowLock())
        {
            ActivateExistingWindow(closeDuplicates: true);
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Closed += (_, _) =>
        {
            _settingsWindowLock?.Dispose();
            _singleInstanceMutex?.Dispose();
            _legacySingleInstanceMutex?.Dispose();
        };
        _window.Activate();
    }

    private void ActivateCurrentWindow()
    {
        if (_window is null)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }

        _window.Activate();
    }

    private bool TryAcquireSettingsWindowLock()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestureSign");
            Directory.CreateDirectory(directory);

            var lockPath = Path.Combine(directory, $"{SettingsWindowLockNameBase}.{InstanceScope}.lock");
            _settingsWindowLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _settingsWindowLock.SetLength(0);
            using var writer = new StreamWriter(_settingsWindowLock, Encoding.UTF8, leaveOpen: true);
            writer.Write(Environment.ProcessId.ToString());
            writer.Flush();
            _settingsWindowLock.Flush();
            _settingsWindowLock.Position = 0;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ActivateExistingWindow(bool closeDuplicates)
    {
        var windows = FindExistingGestureSignWindows(Environment.ProcessId, Environment.ProcessPath ?? string.Empty);
        if (windows.Count == 0)
            return false;

        var primary = windows[0];
        if (closeDuplicates)
        {
            for (var index = 1; index < windows.Count; index++)
                PostMessage(windows[index], WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        ShowWindow(primary, SW_RESTORE);
        SetForegroundWindow(primary);
        return true;
    }

    private static List<IntPtr> FindExistingGestureSignWindows(int currentProcessId, string currentExecutablePath)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
                return true;

            if (!GetWindowRect(hwnd, out var rect))
                return true;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 360 || height < 240)
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == currentProcessId)
                return true;

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            if (IsGestureSignSettingsWindow(processId, title.ToString(), currentExecutablePath))
                windows.Add(hwnd);

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IsGestureSignSettingsWindow(int processId, string title, string currentExecutablePath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var fileName = string.Empty;
            try
            {
                fileName = Path.GetFileName(process.MainModule?.FileName ?? string.Empty);
                var processPath = process.MainModule?.FileName ?? string.Empty;
                if (!PathsEqual(processPath, currentExecutablePath))
                    return false;
            }
            catch
            {
                // Do not redirect across installations when the executable
                // location cannot be verified (for example, across elevation).
                return false;
            }

            var titleLooksRight = title.Contains("GestureSign", StringComparison.OrdinalIgnoreCase);
            var processLooksRight =
                processName.Equals("GestureSign.WinUI", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("GestureSign.WinUI.exe", StringComparison.OrdinalIgnoreCase);

            var legacySettingsWindow =
                processName.Equals("GestureSign", StringComparison.OrdinalIgnoreCase) &&
                titleLooksRight;

            return processLooksRight || legacySettingsWindow;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateInstanceScope()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            executablePath = Path.Combine(AppContext.BaseDirectory, "GestureSign.WinUI.exe");

        var normalizedPath = Path.GetFullPath(executablePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hash, 0, 8);
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
