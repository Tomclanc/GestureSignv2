using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GestureSign.PieMenu;

internal sealed class MenuThemes
{
    internal const string DefaultPalette = "Kando Default";
    private readonly Dictionary<string, string> _names = [];
    private readonly Dictionary<string, string[]> _presets = [];
    private JsonElement _labels;

    internal static MenuThemes Load()
    {
        var result = new MenuThemes();
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(PieMenuAssets.Web("catalog.json")));
        foreach (var theme in catalog.RootElement.GetProperty("themes").EnumerateObject())
            result._names.Add(theme.Name, theme.Value.GetProperty("name").GetString() ?? theme.Name);
        if (catalog.RootElement.TryGetProperty("presets", out var presets))
            foreach (var entry in presets.EnumerateObject())
                result._presets.Add(entry.Name, entry.Value.EnumerateObject().Select(item => item.Name).ToArray());

        var labelPath = Path.Combine(System.AppContext.BaseDirectory, "EditorAssets", "theme-labels.json");
        if (!File.Exists(labelPath)) labelPath = PieMenuAssets.Editor("theme-labels.json");
        if (File.Exists(labelPath))
        {
            using var labels = JsonDocument.Parse(File.ReadAllBytes(labelPath));
            result._labels = labels.RootElement.Clone();
        }
        return result;
    }

    internal string[] Ids => _names.Keys.OrderBy(key => key == "default" ? "" : _names[key]).ToArray();
    internal string Name(string id, MenuLanguage language) =>
        language.ThemeLabel("themes", id) ?? Label(language.UpstreamTag, "themes", id) ?? _names.GetValueOrDefault(id, id);

    internal string PaletteName(string id, MenuLanguage language) =>
        language.ThemeLabel("presets", id) ?? Label(language.UpstreamTag, "presets", id) ??
        (id == DefaultPalette ? language.Text("settings.menu-themes-dialog.default-colors") : id);

    internal string[] Palettes(string id) => [DefaultPalette, .. _presets.GetValueOrDefault(id, [])];

    private string? Label(string tag, string group, string key) =>
        _labels.ValueKind == JsonValueKind.Object &&
        _labels.TryGetProperty(tag, out var localized) &&
        localized.TryGetProperty(group, out var names) &&
        names.TryGetProperty(key, out var label)
            ? label.GetString()
            : null;
}
