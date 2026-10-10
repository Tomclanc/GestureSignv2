using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Input;
using ManagedWinapi.Hooks;

namespace GestureSign.Common.Applications
{
    public static class MouseGestureBinding
    {
        public static bool Matches(IAction action, Devices device, MouseActions button)
        {
            if (device != Devices.None && (action.IgnoredDevices & device) != 0) return false;
            return action.MouseGestureButton == MouseActions.None ||
                (device == Devices.Mouse && button != MouseActions.None && action.MouseGestureButton == button);
        }

        public static List<IAction> Select(IEnumerable<IAction> actions, Devices device, MouseActions button)
        {
            var matches = actions.Where(action => Matches(action, device, button)).ToList();
            var specific = new HashSet<string>(matches.Where(action => action.MouseGestureButton != MouseActions.None)
                .Select(action => action.GestureName ?? ""), StringComparer.OrdinalIgnoreCase);
            return matches.Where(action => action.MouseGestureButton != MouseActions.None ||
                !specific.Contains(action.GestureName ?? "")).ToList();
        }
    }
}
