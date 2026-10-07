namespace GestureSign.Common.Input
{
    public static class PenGesturePolicy
    {
        public static DeviceStates Normalize(DeviceStates setting)
        {
            // Older UI versions saved a button without selecting a drawing mode.
            if (setting != 0 && (setting & (DeviceStates.Tip | DeviceStates.InRange)) == 0)
                setting |= DeviceStates.Tip;
            return setting;
        }
        public static bool IsActive(DeviceStates setting, DeviceStates state)
        {
            setting = Normalize(setting);
            if (setting == 0 || (state & (DeviceStates.InRange | DeviceStates.Tip | DeviceStates.Eraser)) == 0)
                return false;
            var buttons = setting & (DeviceStates.RightClickButton | DeviceStates.Invert | DeviceStates.Eraser);
            return buttons == 0 ||
                (buttons & DeviceStates.RightClickButton) != 0 && (state & DeviceStates.RightClickButton) != 0 ||
                (buttons & (DeviceStates.Invert | DeviceStates.Eraser)) != 0 && (state & (DeviceStates.Invert | DeviceStates.Eraser)) != 0;
        }
    }
}