using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GestureSign.Foundation.RadialMenus;

/// <summary>What an action's icon comes from.</summary>
public enum ActionIconKind
{
    /// <summary>The action has no icon of its own; use the plugin's glyph.</summary>
    None,

    /// <summary>A single Segoe Fluent Icons character.</summary>
    Glyph,

    /// <summary>An image file shown as it is (PNG, JPEG, BMP, GIF).</summary>
    Image,

    /// <summary>A program, shortcut, document or icon resource to read the shell icon from.</summary>
    Program
}

/// <summary>Where an action's icon comes from: a glyph, an image file, or a file to read the icon from.</summary>
public sealed class ActionIconSource
{
    private ActionIconSource(ActionIconKind kind, string value, int iconIndex)
    {
        Kind = kind;
        Value = value;
        IconIndex = iconIndex;
    }

    public ActionIconKind Kind { get; }

    /// <summary>The glyph character, the image path, or the file to extract the icon from.</summary>
    public string Value { get; }

    /// <summary>Index of the icon inside an exe/dll/ico resource; negative values are resource ids.</summary>
    public int IconIndex { get; }

    public static ActionIconSource Glyph(string glyph) => new(ActionIconKind.Glyph, glyph, 0);

    public static ActionIconSource Image(string path) => new(ActionIconKind.Image, path, 0);

    public static ActionIconSource Program(string path, int iconIndex = 0) => new(ActionIconKind.Program, path, iconIndex);

    /// <summary>Stable key for caches; changes when the file it reads changes.</summary>
    public string CacheKey
    {
        get
        {
            if (Kind is ActionIconKind.None or ActionIconKind.Glyph)
                return Kind + "|" + Value;
            long stamp = 0;
            try
            {
                if (File.Exists(Value) || Directory.Exists(Value))
                    stamp = File.GetLastWriteTimeUtc(Value).Ticks;
            }
            catch
            {
                // An unreadable timestamp only costs a cache hit.
            }
            return Kind + "|" + Value + "|" + IconIndex + "|" + stamp;
        }
    }
}

/// <summary>Icon pixels as straight (non-premultiplied) BGRA, top-down.</summary>
public readonly struct IconPixels
{
    public IconPixels(byte[] bgra, int width, int height)
    {
        Bgra = bgra;
        Width = width;
        Height = height;
    }

    public byte[] Bgra { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// Chooses and reads the icon of an action. An action can carry its own icon (a
/// glyph or an image path); otherwise the program or file its commands mention
/// supplies it. Shortcuts (.lnk) are resolved to their target so the icon has no
/// shortcut arrow on it. Reading pixels goes through the shell image lists, so
/// no drawing library is needed and 48×48 icons stay sharp.
/// </summary>
public static class ActionIcons
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    // ----- choosing the source -------------------------------------------

    /// <summary>The icon an action shows, or null when it has to fall back to a glyph.</summary>
    public static ActionIconSource? Resolve(RadialMenuAction? action)
    {
        if (action is null)
            return null;

        var icon = action.Icon;
        if (!string.IsNullOrEmpty(icon))
        {
            if (icon.Length == 1)
                return ActionIconSource.Glyph(icon);
            var chosen = ForPath(icon);
            if (chosen is not null)
                return chosen;
        }

        foreach (var command in action.Commands ?? Enumerable.Empty<RadialMenuCommand>())
        {
            if (command is null || !command.IsEnabled)
                continue;
            foreach (var path in ReferencedPaths(command.CommandSettings))
            {
                var found = ForPath(path);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    /// <summary>The icon source for one path: an image file, or the shell icon of the file (resolving shortcuts).</summary>
    public static ActionIconSource? ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Exists(path))
                return null;
        }
        catch
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return ActionIconSource.Image(path);

        if (IsShortcut(path) && TryReadShortcut(path, out var target, out var iconPath, out var iconIndex))
        {
            // The shortcut's own icon choice wins, then its target: both avoid the arrow overlay.
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                return ActionIconSource.Program(iconPath, iconIndex);
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                return ActionIconSource.Program(target);
        }
        return ActionIconSource.Program(path);
    }

    public static bool IsShortcut(string? path)
        => !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);

    public static bool IsImageFile(string? path)
        => !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The Segoe Fluent Icons glyph that stands for a plugin, used when nothing else is available.</summary>
    public static string PluginGlyph(string? pluginClass)
    {
        var plugin = pluginClass ?? string.Empty;
        var name = plugin.Substring(plugin.LastIndexOf('.') + 1);
        return name switch
        {
            "HotKeyPlugin" or "SendKeystrokes" or "KeyDownKeyUpPlugin" or "TouchKeyboard" => "\uE765",
            "RunCommandPlugin" => "\uE756",
            "LaunchApp" => "\uECA8",
            "OpenFilePlugin" => "\uE8E5",
            "DefaultBrowser" => "\uE774",
            "MouseActionsPlugin" => "\uE962",
            "VolumePlugin" => "\uE767",
            "MediaPlay" or "MediaPlayPause" => "\uE768",
            "MediaPause" => "\uE769",
            "MediaNext" or "MediaForward" => "\uE893",
            "MediaPrevious" or "MediaBackward" => "\uE892",
            "ScreenBrightnessPlugin" => "\uE706",
            "SmartClose" => "\uE8BB",
            "Minimize" => "\uE921",
            "MaximizeRestore" => "\uE922",
            "ToggleWindowTopmost" => "\uE718",
            "SmartNewTab" => "\uE710",
            "ActivateWindowPlugin" or "NextApplication" or "PreviousApplication" or "SendMessagePlugin" or "WindowStateCommand" => "\uE8A7",
            "Delay" => "\uE916",
            "TemporarilyDisable" or "ToggleDisableGestures" => "\uE711",
            "Notifier" => "\uEA8F",
            "ClipboardMatch" => "\uE77F",
            "TextCopyer" => "\uE8C8",
            "StepsPlugin" => "\uE8FD",
            "LibraryActionPlugin" => "\uE8F1",
            _ => "\uE945"
        };
    }

    // ----- paths mentioned by a command ----------------------------------

    /// <summary>Existing local files mentioned anywhere in a command's settings.</summary>
    public static IEnumerable<string> ReferencedPaths(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            yield break;

        var values = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(settings);
            Collect(document.RootElement, values);
        }
        catch (JsonException)
        {
            values.Add(settings);
        }

        foreach (var value in values)
        {
            var path = FirstPath(value);
            if (path != null)
                yield return path;
        }
    }

    private static void Collect(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (text != null)
                    values.Add(text);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, values);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, values);
                break;
        }
    }

    private static string? FirstPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        string? candidate;
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            candidate = end > 1 ? text.Substring(1, end - 1) : null;
        }
        else
        {
            // A command line may carry arguments after the program path.
            candidate = text;
            if (!LooksLikeLocalPath(candidate) || !Exists(candidate))
            {
                candidate = null;
                var match = CommandLinePath.Match(text);
                if (match.Success)
                    candidate = match.Value;
            }
        }
        return candidate != null && LooksLikeLocalPath(candidate) && Exists(candidate) ? candidate : null;
    }

    /// <summary>Quoted paths already handled; this matches a program path at the start of a command line.</summary>
    private static readonly System.Text.RegularExpressions.Regex CommandLinePath =
        new(@"^[a-zA-Z]:\\[^""<>|?*\r\n]*?\.(?:exe|lnk|ico|bat|cmd|com|ps1|py|msc|cpl|scr)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Skip UNC paths so a slow network share can never stall the mouse hook.
    private static bool LooksLikeLocalPath(string path)
        => path.Length > 3 && path.Length < 260 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    /// <summary>Programs, documents, shortcuts and folders all supply an icon.</summary>
    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    // ----- reading the icon ----------------------------------------------

    /// <summary>
    /// Reads the icon of a file as BGRA pixels, at the closest shell image size to
    /// <paramref name="size"/> (16, 32, 48 or 256). Returns false when the file has
    /// no readable icon.
    /// </summary>
    public static bool TryGetIconPixels(string? path, int iconIndex, int size, out IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            var file = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Exists(file))
                return false;
            size = Math.Clamp(size, 16, 256);

            // A shortcut is read through its target so no arrow is drawn on the icon.
            if (IsShortcut(file) && TryReadShortcut(file, out var target, out var iconPath, out var shortcutIndex))
            {
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    file = iconPath;
                    iconIndex = shortcutIndex;
                }
                else if (!string.IsNullOrWhiteSpace(target) && Exists(target))
                {
                    file = target;
                    iconIndex = 0;
                }
            }

            var handle = IconHandle(file, iconIndex, size);
            if (handle == IntPtr.Zero)
                return false;
            try
            {
                return ToPixels(handle, out pixels);
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr IconHandle(string path, int iconIndex, int size)
    {
        // An explicit index means "icon N of this exe/dll/ico"; only ExtractIconEx honours it.
        if (iconIndex != 0 || string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            if (ExtractIconEx(path, iconIndex, out var large, out var small, 1) > 0)
            {
                if (large != IntPtr.Zero)
                {
                    if (small != IntPtr.Zero)
                        DestroyIcon(small);
                    return large;
                }
                if (small != IntPtr.Zero)
                    return small;
            }
        }

        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX) != IntPtr.Zero)
        {
            var listId = typeof(IImageList).GUID;
            if (SHGetImageList(ImageListFor(size), ref listId, out var list) == 0 && list != null)
            {
                try
                {
                    if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) == 0 && icon != IntPtr.Zero)
                        return icon;
                }
                finally
                {
                    Marshal.ReleaseComObject(list);
                }
            }
        }

        // Last resort: the 32×32 associated icon.
        var fallback = new SHFILEINFO();
        return SHGetFileInfo(path, 0, ref fallback, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) != IntPtr.Zero
            ? fallback.hIcon
            : IntPtr.Zero;
    }

    private static int ImageListFor(int size) => size <= 16 ? SHIL_SMALL : size <= 32 ? SHIL_LARGE : size <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;

    private static bool ToPixels(IntPtr handle, out IconPixels pixels)
    {
        pixels = default;
        if (!GetIconInfo(handle, out var info))
            return false;
        try
        {
            if (info.hbmColor == IntPtr.Zero)
                return false;

            var bitmap = new BITMAP();
            if (GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0)
                return false;
            int width = bitmap.bmWidth;
            int height = Math.Abs(bitmap.bmHeight);
            if (width <= 0 || height <= 0)
                return false;

            var color = ReadDib(info.hbmColor, width, height, 32);
            if (color is null)
                return false;

            // Icons without an alpha channel come back fully transparent: the mask
            // then says which pixels are see-through.
            var hasAlpha = false;
            for (var i = 3; i < color.Length; i += 4)
            {
                if (color[i] == 0)
                    continue;
                hasAlpha = true;
                break;
            }
            if (!hasAlpha)
            {
                var mask = info.hbmMask != IntPtr.Zero ? ReadDib(info.hbmMask, width, height, 1) : null;
                if (mask is not null)
                    ApplyMask(color, mask, width, height);
                else
                    for (var i = 3; i < color.Length; i += 4)
                        color[i] = 255;
            }

            pixels = new IconPixels(color, width, height);
            return true;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero)
                DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero)
                DeleteObject(info.hbmMask);
        }
    }

    private static void ApplyMask(byte[] color, byte[] mask, int width, int height)
    {
        var maskStride = (width + 31) / 32 * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var transparent = (mask[y * maskStride + x / 8] & (0x80 >> (x % 8))) != 0;
                color[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }

    private static byte[]? ReadDib(IntPtr bitmap, int width, int height, int bitCount)
    {
        var header = new BITMAPINFO
        {
            bmiColors = new uint[1],
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down rows
                biPlanes = 1,
                biBitCount = (ushort)bitCount,
                biCompression = 0
            }
        };
        var stride = (width * bitCount + 31) / 32 * 4;
        var buffer = new byte[stride * height];
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;
        try
        {
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                if (GetDIBits(dc, bitmap, 0, (uint)height, pinned.AddrOfPinnedObject(), ref header, 0) == 0)
                    return null;
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        return buffer;
    }

    // ----- shortcuts ------------------------------------------------------

    /// <summary>
    /// Reads a shortcut's stored target and icon choice without resolving it (no
    /// network or disk search), so this stays cheap enough for the mouse hook.
    /// </summary>
    public static bool TryReadShortcut(string lnkPath, out string? targetPath, out string? iconPath, out int iconIndex)
    {
        targetPath = null;
        iconPath = null;
        iconIndex = 0;
        if (!IsShortcut(lnkPath))
            return false;
        object? instance = null;
        try
        {
            instance = new ShellLinkCoClass();
            var link = (IShellLinkW)instance;
            ((IPersistFile)link).Load(lnkPath, 0);

            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            if (buffer.Length > 0)
                targetPath = Environment.ExpandEnvironmentVariables(buffer.ToString());

            var iconBuffer = new StringBuilder(1024);
            link.GetIconLocation(iconBuffer, iconBuffer.Capacity, out var index);
            iconIndex = index;
            if (iconBuffer.Length > 0)
                iconPath = Environment.ExpandEnvironmentVariables(iconBuffer.ToString());
            else
                iconIndex = 0;
            return targetPath is not null || iconPath is not null;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
                Marshal.ReleaseComObject(instance);
        }
    }

    // ----- native ---------------------------------------------------------

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_SMALL = 0x1;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_TRANSPARENT = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public uint[] bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint infoSize, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int imageList, ref Guid riid, out IImageList? list);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, IntPtr bits, ref BITMAPINFO info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    // Only the methods up to GetIconLocation are called; they keep their vtable order.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int maxPath, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    }

    // A prefix of IImageList is enough: GetIcon keeps its vtable slot.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
        [PreserveSig] int ReplaceIcon(int index, IntPtr icon, out int result);
        [PreserveSig] int SetOverlayImage(int index, int overlay);
        [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int color, out int index);
        [PreserveSig] int Draw(IntPtr drawParams);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, int flags, out IntPtr icon);
    }
}
