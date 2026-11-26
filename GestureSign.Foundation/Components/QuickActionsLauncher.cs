using System;

namespace GestureSign.Foundation.Components;

/// <summary>
/// Bridge between the step engine and the daemon for the optional Quick Actions
/// component. The step runs inside a plugin, which cannot reference the daemon,
/// and only the daemon knows where the component is installed and how to hand
/// the menu request over (process arguments or IPC). The daemon registers its
/// launcher once at start-up; without it the step reports "unavailable".
/// </summary>
public static class QuickActionsLauncher
{
    private static Func<bool>? _show;

    /// <summary>Registers how to open the component's menu. Pass null to clear it (tests).</summary>
    public static void Register(Func<bool>? show) => _show = show;

    /// <summary>Opens the component's menu. False when no component is installed or it refused to start.</summary>
    public static bool Show() => _show?.Invoke() == true;
}
