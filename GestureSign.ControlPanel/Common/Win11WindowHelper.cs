using MahApps.Metro;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GestureSign.ControlPanel.Common
{
    internal static class Win11WindowHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_MAINWINDOW = 2;
        private const int DWMSBT_TABBEDWINDOW = 4;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        public static void Apply(Window window)
        {
            if (window == null || Environment.OSVersion.Version.Major < 10)
                return;

            ApplyApplicationTheme();
            window.Background = Brushes.Transparent;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int darkMode = IsLightTheme() ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));

            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            int backdropType = DWMSBT_TABBEDWINDOW;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int)) != 0)
            {
                backdropType = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
        }

        public static void ApplyApplicationTheme()
        {
            try
            {
                bool isLightTheme = IsLightTheme();
                ThemeManager.ChangeAppStyle(
                    Application.Current,
                    ThemeManager.GetAccent("Blue"),
                    ThemeManager.GetAppTheme(isLightTheme ? "BaseLight" : "BaseDark"));
                ApplyThemeResources(isLightTheme);
            }
            catch
            {
            }
        }

        private static void ApplyThemeResources(bool isLightTheme)
        {
            ResourceDictionary resources = Application.Current.Resources;
            resources["GestureSign.WindowBackgroundBrush"] = new SolidColorBrush(isLightTheme ? Color.FromArgb(210, 248, 250, 252) : Color.FromArgb(210, 18, 20, 23));
            resources["GestureSign.ContentBackgroundBrush"] = new SolidColorBrush(isLightTheme ? Color.FromArgb(232, 255, 255, 255) : Color.FromArgb(232, 30, 32, 36));
            resources["GestureSign.SubtleBackgroundBrush"] = new SolidColorBrush(isLightTheme ? Color.FromArgb(180, 244, 247, 250) : Color.FromArgb(180, 38, 41, 46));
            resources["GestureSign.TextBrush"] = new SolidColorBrush(isLightTheme ? Color.FromRgb(18, 18, 18) : Color.FromRgb(242, 244, 248));
            resources["GestureSign.SecondaryTextBrush"] = new SolidColorBrush(isLightTheme ? Color.FromRgb(78, 82, 88) : Color.FromRgb(205, 210, 218));
            resources["GestureSign.BorderBrush"] = new SolidColorBrush(isLightTheme ? Color.FromArgb(130, 210, 218, 226) : Color.FromArgb(130, 78, 84, 92));
        }

        private static bool IsLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int)
                        return (int)value != 0;
                }
            }
            catch
            {
            }

            return true;
        }
    }
}
