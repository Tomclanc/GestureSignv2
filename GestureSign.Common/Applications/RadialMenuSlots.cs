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
        public const string ThirdPrefix = "RadialMenu.Third.";
        public const int MaxRings = 3;

        public static string Name(bool outer, int index) => Name(outer ? 1 : 0, index);

        /// <summary>Slot name of ring 0 (inner), 1 (outer) or 2 (third).</summary>
        public static string Name(int ring, int index)
            => (ring >= 2 ? ThirdPrefix : ring == 1 ? OuterPrefix : InnerPrefix) + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static bool IsRadialMenuGesture(string gestureName)
            => !string.IsNullOrEmpty(gestureName) &&
               (gestureName.StartsWith(InnerPrefix, StringComparison.OrdinalIgnoreCase) ||
                gestureName.StartsWith(OuterPrefix, StringComparison.OrdinalIgnoreCase) ||
                gestureName.StartsWith(ThirdPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
