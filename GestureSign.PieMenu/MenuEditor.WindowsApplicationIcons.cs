using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GestureSign.PieMenu;

internal static class WindowsApplicationIcons
{
    internal sealed record Entry(string Name, string ParsingName)
    {
        public override string ToString() => Name;
    }

    private const string AppsFolder = "shell:AppsFolder";

    private static readonly Lazy<Task<Entry[]>> InstalledApps = new(() => RunOnStaAsync(ReadApps));
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string?>>> ImportedIcons =
        new(StringComparer.OrdinalIgnoreCase);

    internal static Task<string?> ResolveImportedAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 512) return Task.FromResult<string?>(null);
        return ImportedIcons.GetOrAdd(id, name => new Lazy<Task<string?>>(() => ResolveAsync(name))).Value;
    }

    private static async Task<string?> ResolveAsync(string id)
    {
        if (Path.IsPathFullyQualified(id) || id.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("::", StringComparison.Ordinal))
        {
            if (await GetImageAsync(id).ConfigureAwait(false) is { } exactIcon) return exactIcon;
        }
        else
        {
            // Kando can store a Start-menu display name instead of an app ID.
            // A matching shortcut avoids enumerating the Shell AppsFolder on first use.
            var shortcut = await Task.Run(() => FindStartMenuShortcut(id)).ConfigureAwait(false);
            if (shortcut is not null && await GetImageAsync(shortcut).ConfigureAwait(false) is { } shortcutIcon)
                return shortcutIcon;
            if (await GetImageAsync(AppsFolder + "\\" + id).ConfigureAwait(false) is { } appIcon)
                return appIcon;
        }

        var apps = await InstalledApps.Value.ConfigureAwait(false);
        var match = apps.FirstOrDefault(app =>
            app.ParsingName.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            app.ParsingName.Equals(AppsFolder + "\\" + id, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(app => app.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : await GetImageAsync(match.ParsingName);
    }

    private static string? FindStartMenuShortcut(string displayName)
    {
        var search = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var directory = Environment.GetFolderPath(folder);
            if (!Directory.Exists(directory)) continue;
            try
            {
                var match = Directory.EnumerateFiles(directory, "*.lnk", search).FirstOrDefault(path =>
                    Path.GetFileNameWithoutExtension(path).Equals(displayName, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    internal static Task<Entry[]> ListAsync() => RunOnStaAsync(ReadApps);

    internal static async Task<string?> GetImageAsync(string parsingName)
    {
        try
        {
            if (await RunOnStaAsync(() => ReadPixels(parsingName)) is not { } icon) return null;
            foreach (var limit in new[] { 256, 192, 128 })
            {
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)icon.Width, (uint)icon.Height, 96, 96, icon.Pixels);
                var maxDimension = Math.Max(icon.Width, icon.Height);
                if (maxDimension > limit)
                {
                    encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, icon.Width * limit / maxDimension);
                    encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, icon.Height * limit / maxDimension);
                    encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                }
                await encoder.FlushAsync();
                if (stream.Size > MenuIcons.ImageLimit) continue;
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                var bytes = new byte[await reader.LoadAsync((uint)stream.Size)];
                reader.ReadBytes(bytes);
                return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            return null;
        }
        catch (Exception error) when (error is COMException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private static Entry[] ReadApps()
    {
        object? shell = null, folder = null, items = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return [];
            shell = Activator.CreateInstance(type);
            if (shell is null) return [];
            folder = ((dynamic)shell).NameSpace(AppsFolder);
            if (folder is null) return [];
            items = ((dynamic)folder).Items();
            var apps = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic item in (dynamic)items)
            {
                try
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    var parsingName = Path.IsPathFullyQualified(path) || path.StartsWith("::", StringComparison.Ordinal)
                        || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                        ? path : AppsFolder + "\\" + path;
                    apps.TryAdd(parsingName, new Entry(name, parsingName));
                }
                finally { Release(item); }
            }
            return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return [];
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static Task<T> RunOnStaAsync<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { result.TrySetResult(action()); }
            catch (Exception error) { result.TrySetException(error); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return result.Task;
    }

    private sealed record IconPixels(int Width, int Height, byte[] Pixels);

    private static IconPixels? ReadPixels(string name)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(name, 0, ref iid, out var factory) < 0 || factory is null) return null;
        nint bitmap = 0, dc = 0;
        try
        {
            const uint iconOnly = 0x4;
            const uint biggerSizeOk = 0x1;
            if (factory.GetImage(new Size { Width = 256, Height = 256 }, iconOnly | biggerSizeOk, out bitmap) < 0 || bitmap == 0)
            {
                if (bitmap != 0) DeleteObject(bitmap);
                if (factory.GetImage(new Size { Width = 96, Height = 96 }, iconOnly, out bitmap) < 0 || bitmap == 0)
                    return null;
            }
            if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var shape) == 0 ||
                shape.Width is <= 0 or > 1024 || shape.Height is <= 0 or > 1024)
                return null;
            var pixels = new byte[shape.Width * shape.Height * 4];
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = shape.Width, Height = -shape.Height,
                Planes = 1, BitCount = 32
            };
            dc = CreateCompatibleDC(0);
            if (dc == 0 || GetDIBits(dc, bitmap, 0, (uint)shape.Height, pixels, ref info, 0) != shape.Height)
                return null;

            // Some legacy Shell icons return an unused alpha channel.
            bool hasAlpha = false;
            for (var i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
            if (!hasAlpha)
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new IconPixels(shape.Width, shape.Height, pixels);
        }
        finally
        {
            if (dc != 0) DeleteDC(dc);
            if (bitmap != 0) DeleteObject(bitmap);
            Release(factory);
        }
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { internal int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        internal int Type, Width, Height, WidthBytes;
        internal ushort Planes, BitsPixel;
        internal nint Pixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, SizeImage;
        internal int XPels, YPels;
        internal uint UsedColors, ImportantColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(nint handle, int size, out Bitmap result);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint count, [Out] byte[] pixels, ref BitmapInfo info, uint colors);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}
