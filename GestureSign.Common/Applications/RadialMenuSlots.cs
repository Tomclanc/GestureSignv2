using System;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// Reserved built-in gesture names for the middle-button radial menu.
    /// Slot 0 points up; slots advance clockwise in 45 degree steps.
    /// Actions bound to these names live in the normal application groups,
    /// so an application group overrides the global action of the same slot.
    /// </summary>
    public static class RadialMenuSlots
    {
        public const int SlotsPerRing = 8;
        public const string InnerPrefix = "RadialMenu.Inner.";
        public const string OuterPrefix = "RadialMenu.Outer.";

        public static string Name(bool outer, int index)
            => (outer ? OuterPrefix : InnerPrefix) + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static bool IsRadialMenuGesture(string gestureName)
            => !string.IsNullOrEmpty(gestureName) &&
               (gestureName.StartsWith(InnerPrefix, StringComparison.OrdinalIgnoreCase) ||
                gestureName.StartsWith(OuterPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
