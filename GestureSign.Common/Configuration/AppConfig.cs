using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GestureSign.Common.Configuration
{
    public class AppConfig
    {
        private static bool _loadFlag = true;
        private static Dictionary<string, object> _settingCache = new Dictionary<string, object>(16);
        static System.Configuration.Configuration _config;
        static Timer Timer;
        public static event EventHandler ConfigChanged;
        private const int ConfigRetryCount = 20;
        private const int ConfigRetryDelayMilliseconds = 80;
        private const string ConfigMutexName = @"Local\GestureSignConfigFileLock";
        private static readonly object ConfigSyncRoot = new object();
        private static readonly Mutex ConfigMutex = new Mutex(false, ConfigMutexName);

        private static ExeConfigurationFileMap ExeMap;
        private static string _applicationDataPath;
        private static string _localApplicationDataPath;

        private static System.Configuration.Configuration Config
        {
            get
            {
                if (_config == null || _loadFlag)
                {
                    try
                    {
                        WithConfigFileLock(() =>
                        {
                            FileManager.WaitFile(ConfigPath);
                            _config = RetryConfigOperation(() => ConfigurationManager.OpenMappedExeConfiguration(ExeMap, ConfigurationUserLevel.None));
                            MigrateLegacyDrawingButtonDefault(_config);
                            _settingCache.Clear();
                            _loadFlag = false;
                        });
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }
                return _config;
            }
        }

        public static string ApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_applicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_applicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _applicationDataPath;
            }
            private set => _applicationDataPath = value;
        }

        public static string LocalApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_localApplicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_localApplicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _localApplicationDataPath;
            }
            private set => _localApplicationDataPath = value;
        }

        public static string BackupPath { private set; get; }

        public static string ConfigPath { private set; get; }

        public static string CurrentFolderPath { private set; get; }

        #region Setting Parameters

        public static System.Drawing.Color VisualFeedbackColor
        {
            get
            {
                return (System.Drawing.Color)GetValue("VisualFeedbackColor", System.Drawing.Color.DeepSkyBlue);
            }
            set
            {
                SetValue("VisualFeedbackColor", value);
            }
        }

        public static string VisualFeedbackColorSetting
        {
            get
            {
                return GetValue("VisualFeedbackColor", string.Empty);
            }
        }

        public static int VisualFeedbackWidth
        {
            get
            {
                return (int)GetValue("VisualFeedbackWidth", 9);
            }
            set
            {
                SetValue("VisualFeedbackWidth", value);
            }
        }

        public static bool ShowGestureActionHint
        {
            get
            {
                return GetValue(nameof(ShowGestureActionHint), false);
            }
            set
            {
                SetValue(nameof(ShowGestureActionHint), value);
            }
        }

        public static int MinimumPointDistance
        {
            get
            {
                return (int)GetValue("MinimumPointDistance", 20);
            }
            set
            {
                SetValue("MinimumPointDistance", value);
            }
        }

        public static double Opacity
        {
            get
            {
                return (double)GetValue("Opacity", 0.35);
            }
            set
            {
                SetValue("Opacity", value);
            }
        }

        public static bool IsOrderByLocation
        {
            get
            {
                return (bool)GetValue("IsOrderByLocation", true);
            }
            set
            {
                SetValue("IsOrderByLocation", value);
            }
        }

        public static bool UiAccess { get; set; }
        public static bool ShowTrayIcon
        {
            get
            {
                return (bool)GetValue("ShowTrayIcon", true);
            }
            set
            {
                SetValue("ShowTrayIcon", value);
            }
        }

        public static string CultureName
        {
            get
            {
                return (string)GetValue("CultureName", "");
            }
            set
            {
                SetValue("CultureName", value);
            }
        }

        public static bool SendErrorReport
        {
            get
            {
                return (bool)GetValue("SendErrorReport", true);
            }
            set
            {
                SetValue("SendErrorReport", value);
            }
        }

        public static DateTime LastErrorTime
        {
            get
            {
                return GetValue("LastErrorTime", DateTime.MinValue);
            }
            set
            {
                SetValue("LastErrorTime", value);
            }
        }

        public static int InitialTimeout
        {
            get
            {
                return (int)GetValue(nameof(InitialTimeout), 0);
            }
            set
            {
                SetValue(nameof(InitialTimeout), value);
            }
        }

        public static MouseActions DrawingButton
        {
            get
            {
                return (MouseActions)GetValue(nameof(DrawingButton), (int)MouseActions.Right);
            }
            set
            {
                SetValue(nameof(DrawingButton), (int)value);
            }
        }

        public static bool PreferEdgeMouseGestures
        {
            get
            {
                return (bool)GetValue(nameof(PreferEdgeMouseGestures), false);
            }
            set
            {
                SetValue(nameof(PreferEdgeMouseGestures), value);
            }
        }

        public static bool RegisterTouchPad
        {
            get
            {
                return (bool)GetValue(nameof(RegisterTouchPad), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchPad), value);
            }
        }

        public static bool PreferWindowsTouchPadGestures
        {
            get
            {
                return (bool)GetValue(nameof(PreferWindowsTouchPadGestures), false);
            }
            set
            {
                SetValue(nameof(PreferWindowsTouchPadGestures), value);
            }
        }

        public static bool RegisterTouchScreen
        {
            get
            {
                return GetValue(nameof(RegisterTouchScreen), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchScreen), value);
            }
        }

        public static bool IgnoreFullScreen
        {
            get
            {
                return GetValue(nameof(IgnoreFullScreen), false);
            }
            set
            {
                SetValue(nameof(IgnoreFullScreen), value);
            }
        }

        public static bool IgnoreFullScreenVideo
        {
            get
            {
                return GetValue(nameof(IgnoreFullScreenVideo), false);
            }
            set
            {
                SetValue(nameof(IgnoreFullScreenVideo), value);
            }
        }

        public static bool IgnoreTouchInputWhenUsingPen
        {
            get
            {
                return GetValue(nameof(IgnoreTouchInputWhenUsingPen), true);
            }
            set
            {
                SetValue(nameof(IgnoreTouchInputWhenUsingPen), value);
            }
        }

        public static DeviceStates PenGestureButton
        {
            get
            {
                return (DeviceStates)GetValue(nameof(PenGestureButton), 0);
            }
            set
            {
                SetValue(nameof(PenGestureButton), (int)value);
            }
        }

        public static bool RunAsAdmin
        {
            get
            {
                return GetValue(nameof(RunAsAdmin), false);
            }
            set
            {
                SetValue(nameof(RunAsAdmin), value);
            }
        }

        public static string OpenSettingsHotKey
        {
            get
            {
                return GetValue(nameof(OpenSettingsHotKey), string.Empty);
            }
            set
            {
                SetValue(nameof(OpenSettingsHotKey), value);
            }
        }

        public static bool KandoEnabled
        {
            get
            {
                return GetValue(nameof(KandoEnabled), false);
            }
            set
            {
                SetValue(nameof(KandoEnabled), value);
            }
        }

        public static string KandoHotKey
        {
            get
            {
                return GetValue(nameof(KandoHotKey), string.Empty);
            }
            set
            {
                SetValue(nameof(KandoHotKey), value);
            }
        }

        public static string KandoSettingsHotKey
        {
            get
            {
                return GetValue(nameof(KandoSettingsHotKey), string.Empty);
            }
            set
            {
                SetValue(nameof(KandoSettingsHotKey), value);
            }
        }

        public static string KandoExecutablePath
        {
            get
            {
                return GetValue(nameof(KandoExecutablePath), string.Empty);
            }
            set
            {
                SetValue(nameof(KandoExecutablePath), value);
            }
        }

        public static string KandoMenuName
        {
            get
            {
                return GetValue(nameof(KandoMenuName), string.Empty);
            }
            set
            {
                SetValue(nameof(KandoMenuName), value);
            }
        }

        public static string KandoTrigger
        {
            get
            {
                return GetValue(nameof(KandoTrigger), string.Empty);
            }
            set
            {
                SetValue(nameof(KandoTrigger), value);
            }
        }

        #endregion

        static AppConfig()
        {
#if uiAccess
            UiAccess = VersionHelper.IsWindows8OrGreater();
#endif
            CurrentFolderPath = Path.GetDirectoryName(new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath);
#if Portable
            ApplicationDataPath = Path.Combine(CurrentFolderPath, "AppData");
            LocalApplicationDataPath = ApplicationDataPath;

            ConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            BackupPath = Path.Combine(LocalApplicationDataPath, "Backup");
#else
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ApplicationDataPath = Path.Combine(appDataPath, "GestureSign V2");
            LocalApplicationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign V2");
            EnsureApplicationDataMigrated(Path.Combine(appDataPath, "GestureSign"), ApplicationDataPath);

            ConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            BackupPath = LocalApplicationDataPath + "\\Backup";

#endif
            ExeMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = ConfigPath,
                RoamingUserConfigFilename = ConfigPath,
            };
            Timer = new Timer(SaveFile, null, Timeout.Infinite, Timeout.Infinite);
        }

        private static void EnsureApplicationDataMigrated(string legacyPath, string targetPath)
        {
            if (!Directory.Exists(legacyPath))
                return;

            try
            {
                Directory.CreateDirectory(targetPath);
                foreach (var sourcePath in Directory.EnumerateFiles(legacyPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = sourcePath.Substring(legacyPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var destinationPath = Path.Combine(targetPath, relativePath);
                    if (File.Exists(destinationPath))
                        continue;

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        File.Copy(sourcePath, destinationPath, false);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static void Reload()
        {
            WithConfigFileLock(() =>
            {
                _loadFlag = true;
                _config = null;
                _settingCache.Clear();
                RetryConfigOperation(() => ConfigurationManager.RefreshSection("appSettings"));
            });
            if (ConfigChanged != null)
                ConfigChanged(new object(), EventArgs.Empty);
        }


        private static void Save()
        {
            Timer.Change(100, Timeout.Infinite);
        }

        private static void SaveFile(object state)
        {
            try
            {
                WithConfigFileLock(() =>
                {
                    FileManager.WaitFile(ConfigPath);
                    // Save the configuration file.
                    var config = Config;
                    config.AppSettings.SectionInformation.ForceSave = true;
                    RetryConfigOperation(() => config.Save(ConfigurationSaveMode.Modified));
                    RetryConfigOperation(() => ConfigurationManager.RefreshSection("appSettings"));
                });
            }
            catch (ConfigurationErrorsException e)
            {
                Reload();
                Logging.LogAndNotice(new Exceptions.FileWriteException(e));
            }
            catch (Exception e)
            {
                Logging.LogAndNotice(e);
            }
            ConfigChanged?.Invoke(new object(), EventArgs.Empty);
        }

        private static void WithConfigFileLock(Action action)
        {
            WithConfigFileLock<object>(() =>
            {
                action();
                return null;
            });
        }

        private static T WithConfigFileLock<T>(Func<T> action)
        {
            lock (ConfigSyncRoot)
            {
                var lockTaken = false;
                try
                {
                    try
                    {
                        lockTaken = ConfigMutex.WaitOne(TimeSpan.FromSeconds(5));
                    }
                    catch (AbandonedMutexException)
                    {
                        lockTaken = true;
                    }

                    if (!lockTaken)
                        throw new TimeoutException("Timed out waiting for GestureSign configuration file lock.");

                    return action();
                }
                finally
                {
                    if (lockTaken)
                        ConfigMutex.ReleaseMutex();
                }
            }
        }

        private static void RetryConfigOperation(Action action)
        {
            RetryConfigOperation<object>(() =>
            {
                action();
                return null;
            });
        }

        private static T RetryConfigOperation<T>(Func<T> action)
        {
            Exception lastException = null;
            for (var attempt = 0; attempt < ConfigRetryCount; attempt++)
            {
                try
                {
                    return action();
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (ConfigurationErrorsException ex) when (HasSharingViolation(ex))
                {
                    lastException = ex;
                }

                Thread.Sleep(ConfigRetryDelayMilliseconds);
            }

            throw lastException ?? new IOException("Configuration file operation failed.");
        }

        private static bool HasSharingViolation(Exception exception)
        {
            while (exception != null)
            {
                if (exception is IOException ioException)
                {
                    var errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ioException) & 65535;
                    if (errorCode == 32 || errorCode == 33)
                        return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        private static T GetValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            if (Config == null)
                return defaultValue;
            var setting = Config.AppSettings.Settings[key];
            if (setting != null)
            {
                try
                {
                    return converter(setting.Value);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private static T GetCacheValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            lock (ConfigSyncRoot)
            {
                object output;
                if (_settingCache.TryGetValue(key, out output))
                {
                    return (T)output;
                }

                var value = GetValue(key, defaultValue, converter);
                _settingCache[key] = value;
                return value;
            }
        }

        private static int GetValue(string key, int defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => int.Parse(s));
        }

        private static double GetValue(string key, double defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => double.Parse(s));
        }

        private static bool GetValue(string key, bool defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => bool.Parse(s));
        }

        private static string GetValue(string key, string defaultValue)
        {
            return GetValue(key, defaultValue, s => s);
        }

        private static void MigrateLegacyDrawingButtonDefault(System.Configuration.Configuration config)
        {
            var drawingButton = config.AppSettings.Settings[nameof(DrawingButton)];
            if (drawingButton == null)
                return;
            if (drawingButton.Value != "0")
                return;

            var explicitlyDisabled = config.AppSettings.Settings["MouseGesturesDisabledByUser"];
            if (explicitlyDisabled != null &&
                bool.TryParse(explicitlyDisabled.Value, out var disabled) &&
                disabled)
            {
                return;
            }

            drawingButton.Value = ((int)MouseActions.Right).ToString(CultureInfo.InvariantCulture);
            if (config.AppSettings.Settings["DrawingButtonMigratedToRight"] == null)
                config.AppSettings.Settings.Add("DrawingButtonMigratedToRight", "true");
            else
                config.AppSettings.Settings["DrawingButtonMigratedToRight"].Value = "true";
            config.Save(ConfigurationSaveMode.Modified);
            RetryConfigOperation(() => ConfigurationManager.RefreshSection("appSettings"));
        }

        private static DateTime GetValue(string key, DateTime defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return DateTime.Parse(setting);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            else return defaultValue;
        }

        private static System.Drawing.Color GetValue(string key, System.Drawing.Color defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return System.Drawing.ColorTranslator.FromHtml(setting);
                }
                catch
                {
                    return defaultValue;
                }
            }
            else
            {
                System.Drawing.Color color;
                if (GetWindowGlassColor(out color))
                    return color;
                return defaultValue;
            }
        }

        private static void SetValue<T>(string key, T value)
        {
            if (Config == null)
                return;
            _settingCache.Clear();

            if (Config.AppSettings.Settings[key] != null)
            {
                Config.AppSettings.Settings[key].Value = value.ToString();
            }
            else
            {
                Config.AppSettings.Settings.Add(key, value.ToString());
            }
            Save();
        }

        private static void SetValue(string key, System.Drawing.Color value)
        {
            SetValue(key, System.Drawing.ColorTranslator.ToHtml(value));
        }

        private static void SetValue(string key, DateTime value)
        {
            SetValue(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static bool GetWindowGlassColor(out System.Drawing.Color windowGlassColor)
        {
            windowGlassColor = System.Drawing.Color.Empty;
            try
            {
                if (VersionHelper.IsWindowsVistaOrGreater())
                {
                    using (RegistryKey dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        if (dwm == null)
                            return false;
                        var colorizationColor = dwm.GetValue("ColorizationColor");
                        if (colorizationColor == null)
                            return false;

                        windowGlassColor = System.Drawing.Color.FromArgb((int)colorizationColor | -16777216);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
    }
}
