using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GestureSign.PieMenu;

internal sealed class MenuIcons
{
    internal const string Kando = "kando";
    internal const string Material = "material-symbols-rounded";
    internal const string Simple = "simple-icons";
    internal const string Colored = "simple-icons-colored";
    internal const string Emoji = "emoji";
    internal const string Image = "base64";
    internal const string System = "system";
    internal const string WindowsApps = "windows-app-icons";
    internal const int ImageLimit = 192 * 1024;

    internal sealed record Theme(string Id, string Name);
    internal sealed record Icon(string Id, string Name, string SearchText, string? Path = null, string? Color = null);
    internal static readonly Theme[] Themes =
    [
        new(Kando, "Built-in menu icons"), new(Material, "Material rounded icons"), new(Simple, "Simple Icons (Monochrome)"),
        new(Colored, "Simple Icons (Colored)"), new(Emoji, "Emoji"), new(Image, "Custom image"),
        new(WindowsApps, "Installed app icons")
    ];

    private static readonly object CacheLock = new();
    private static string? _cachedRoot;
    private static Task<MenuIcons>? _catalog;
    private readonly Dictionary<string, Dictionary<string, Icon>> _icons = [];
    private readonly int _materialUnits;
    private readonly int _simpleUnits;

    private MenuIcons(string rootDirectory)
    {
        using var json = ReadCatalog(rootDirectory);
        var root = json.RootElement;
        _materialUnits = root.GetProperty("materialUnits").GetInt32();
        _simpleUnits = root.GetProperty("simpleUnits").GetInt32();
        _icons[Kando] = root.GetProperty("files").EnumerateArray().Select(entry =>
        {
            var id = entry.GetString()!;
            var name = Path.GetFileNameWithoutExtension(id).Replace('-', ' ');
            return new Icon(id, name, Normalize(name));
        }).ToDictionary(icon => icon.Id);
        _icons[Material] = root.GetProperty("material").EnumerateObject().Select(entry =>
        {
            var name = entry.Name.Replace('_', ' ');
            return new Icon(entry.Name, name, Normalize(name), entry.Value.GetString());
        }).ToDictionary(icon => icon.Id);
        _icons[Simple] = root.GetProperty("simple").EnumerateObject().Select(entry =>
        {
            var name = entry.Value.GetProperty("title").GetString()!;
            return new Icon(entry.Name, name, Normalize(name + " " + entry.Name),
                entry.Value.GetProperty("path").GetString(), entry.Value.GetProperty("color").GetString());
        }).ToDictionary(icon => icon.Id);
        _icons[Colored] = _icons[Simple];
        _icons[Emoji] = root.GetProperty("emoji").EnumerateObject().Select(entry =>
        {
            var descriptors = entry.Value.EnumerateArray().Select(value => value.GetString()).ToArray();
            var name = descriptors.FirstOrDefault()?.Replace('_', ' ') ?? entry.Name;
            return new Icon(entry.Name, name, Normalize(string.Join(' ', descriptors)));
        }).ToDictionary(icon => icon.Id);
    }

    private static JsonDocument ReadCatalog(string root)
    {
        var assets = Path.Combine(root, "KandoWeb");
        var archivePath = Path.Combine(assets, "native-icons.zip");
        if (File.Exists(archivePath))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry("native-icons.json")
                ?? throw new InvalidDataException("The native icon archive is missing its catalog.");
            using var stream = entry.Open();
            return JsonDocument.Parse(stream);
        }

        using var legacy = File.OpenRead(Path.Combine(assets, "native-icons.json"));
        return JsonDocument.Parse(legacy);
    }

    internal static Task<MenuIcons> GetAsync(string root)
    {
        lock (CacheLock)
        {
            if (_cachedRoot != root || _catalog is null || _catalog.IsFaulted)
            {
                _cachedRoot = root;
                _catalog = Task.Run(() => new MenuIcons(root));
            }
            return _catalog;
        }
    }

    internal Icon? Find(string theme, string id) => _icons.GetValueOrDefault(theme)?.GetValueOrDefault(id);

    internal IEnumerable<Icon> Search(string theme, string text)
    {
        if (!_icons.TryGetValue(theme, out var icons)) return [];
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Normalize).ToArray();
        var containsWords = words.Any(word => word.Length > 0);
        return words.Length == 0 ? icons.Values
            : icons.Values.Where(icon => icon.Id.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                containsWords && words.All(icon.SearchText.Contains));
    }

    internal static async Task<string?> SvgAsync(string root, string theme, string id, string color)
    {
        if (theme == Kando)
        {
            if (id.Length == 0 || !id.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                return null;
            var path = Path.Combine(root, "KandoWeb", "icons", id);
            return File.Exists(path) ? (await File.ReadAllTextAsync(path).ConfigureAwait(false))
                .Replace("currentColor", color, StringComparison.OrdinalIgnoreCase) : null;
        }
        return (await GetAsync(root).ConfigureAwait(false)).Svg(theme, id, color);
    }

    internal int ViewportSize(string theme) => theme == Material ? _materialUnits : _simpleUnits;

    private string? Svg(string theme, string id, string foreground)
    {
        if (Find(theme, id) is not { } icon) return null;
        if (string.IsNullOrEmpty(icon.Path)) return null;
        var units = ViewportSize(theme);
        var color = theme == Colored ? icon.Color : foreground;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {units} {units}\"><path fill=\"{color}\" d=\"{icon.Path}\"/></svg>";
    }

    internal static bool TryImage(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is not { Length: > 0 and <= ImageLimit * 4 / 3 + 64 }) return false;
        var comma = value.IndexOf(',');
        if (comma < 0 || value[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or
            "data:image/gif;base64" or "data:image/webp;base64" or "data:image/x-icon;base64")) return false;
        try
        {
            bytes = Convert.FromBase64String(value[(comma + 1)..]);
            if (bytes.Length < 12 || bytes.Length > ImageLimit) return false;
            ReadOnlySpan<byte> header = bytes;
            return value[..comma] switch
            {
                "data:image/png;base64" => header[0] == 0x89 && header.Slice(1, 3).SequenceEqual("PNG"u8),
                "data:image/jpeg;base64" => header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
                "data:image/gif;base64" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
                "data:image/webp;base64" => header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
                "data:image/x-icon;base64" => header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0,
                _ => false
            };
        }
        catch (FormatException) { return false; }
    }

    private static string Normalize(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
