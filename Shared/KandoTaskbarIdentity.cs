#pragma warning disable CS8600

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GestureSign.Shared
{
    internal static class KandoTaskbarIdentity
    {
        private const string KandoAppUserModelId = "menu.kando.Kando";
        private const ushort VtLpWStr = 31;
        private static readonly Guid PropertyStoreInterfaceId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        private static readonly Guid AppUserModelFormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        public static void ApplyWhenWindowAvailable(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return;

            string expectedPath;
            try
            {
                expectedPath = Path.GetFullPath(executablePath);
            }
            catch
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var appliedWindows = new HashSet<IntPtr>();
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    ApplyToKandoWindows(expectedPath, appliedWindows);
                    Thread.Sleep(250);
                }
            });
        }

        private static void ApplyToKandoWindows(string expectedPath, ISet<IntPtr> appliedWindows)
        {
            foreach (var process in Process.GetProcessesByName("kando"))
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (!string.Equals(Path.GetFullPath(processPath ?? string.Empty), expectedPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    EnumWindows((windowHandle, _) =>
                    {
                        GetWindowThreadProcessId(windowHandle, out var windowProcessId);
                        if (windowProcessId == (uint)process.Id && appliedWindows.Add(windowHandle))
                            TryApply(windowHandle, expectedPath);

                        return true;
                    }, IntPtr.Zero);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void TryApply(IntPtr windowHandle, string executablePath)
        {
            IPropertyStore propertyStore = null;
            var appId = PropVariant.FromString(KandoAppUserModelId);
            var iconResource = PropVariant.FromString(executablePath + ",0");

            try
            {
                var interfaceId = PropertyStoreInterfaceId;
                if (SHGetPropertyStoreForWindow(windowHandle, ref interfaceId, out propertyStore) != 0 || propertyStore == null)
                    return;

                var appIdKey = new PropertyKey(AppUserModelFormatId, 5);
                var iconResourceKey = new PropertyKey(AppUserModelFormatId, 3);
                if (propertyStore.SetValue(ref appIdKey, ref appId) != 0)
                    return;
                if (propertyStore.SetValue(ref iconResourceKey, ref iconResource) != 0)
                    return;

                propertyStore.Commit();
            }
            catch
            {
            }
            finally
            {
                PropVariantClear(ref appId);
                PropVariantClear(ref iconResource);
                if (propertyStore != null)
                    Marshal.ReleaseComObject(propertyStore);
            }
        }

        private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(
            IntPtr windowHandle,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant propVariant);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public PropertyKey(Guid formatId, uint propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }

            public Guid FormatId;
            public uint PropertyId;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)]
            public ushort VariantType;

            [FieldOffset(8)]
            public IntPtr PointerValue;

            public static PropVariant FromString(string value)
            {
                return new PropVariant
                {
                    VariantType = VtLpWStr,
                    PointerValue = Marshal.StringToCoTaskMemUni(value)
                };
            }
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            uint GetCount(out uint propertyCount);

            [PreserveSig]
            uint GetAt(uint propertyIndex, out PropertyKey propertyKey);

            [PreserveSig]
            uint GetValue(ref PropertyKey propertyKey, out PropVariant value);

            [PreserveSig]
            uint SetValue(ref PropertyKey propertyKey, ref PropVariant value);

            [PreserveSig]
            uint Commit();
        }
    }
}
