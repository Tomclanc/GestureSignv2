using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using GestureSign.WinUI;
using Windows.System.UserProfile;

namespace GestureSign.PieMenu;

internal sealed class MenuLanguage
{
    internal sealed record Option(string Id, string Name);

    private readonly JsonElement _english;
    private readonly JsonElement _editorStrings;
    private readonly JsonElement _upstream;
    private readonly JsonElement _upstreamEnglish;
    private readonly JsonElement _translation;

    internal MenuLanguage(string? preference)
    {
        Tag = UiTranslationCatalog.ResolveCultureName(string.IsNullOrWhiteSpace(preference)
            ? GlobalizationPreferences.Languages.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name
            : preference);
        UpstreamTag = Tag switch
        {
            "zh-CN" => "zh-Hans",
            "zh-TW" => "zh-Hant",
            "nb-NO" => "nb-NO",
            "pt-BR" => "pt-BR",
            _ => Tag.Split('-')[0]
        };
        var localized = AppLocale(Tag);
        var upstream = PieMenuAssets.Web(Path.Combine("locales", UpstreamTag, "translation.json"));
        RendererTag = File.Exists(localized) ? Tag : File.Exists(upstream) ? UpstreamTag : "en";
        _translation = Read(localized);
        _upstream = Read(upstream);
        _english = Read(AppLocale("en-US"));
        _upstreamEnglish = UpstreamTag == "en" ? _upstream : Read(PieMenuAssets.Web("locales/en/translation.json"));
        var editorStrings = Path.Combine(AppContext.BaseDirectory, "EditorAssets", "editor-strings.json");
        _editorStrings = Read(File.Exists(editorStrings) ? editorStrings : PieMenuAssets.Editor("editor-strings.json"));
    }

    internal string Tag { get; }
    internal string UpstreamTag { get; }
    internal string RendererTag { get; }

    internal static MenuLanguage FromHostConfiguration(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    MaxCharactersInDocument = 2 * 1024 * 1024
                });
                var settings = XDocument.Load(reader).Root?.Element("appSettings");
                var preference = settings?.Elements("add").LastOrDefault(
                    entry => string.Equals((string?)entry.Attribute("key"), "CultureName", StringComparison.OrdinalIgnoreCase));
                return new MenuLanguage((string?)preference?.Attribute("value"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
        }
        return new MenuLanguage(null);
    }

    internal string Text(string key) => Find(_translation, key) ?? Find(_editorStrings, UpstreamTag + "." + key)
        ?? Find(_upstream, key) ?? Find(_english, key) ?? Find(_editorStrings, "en." + key)
        ?? Find(_upstreamEnglish, key) ?? key;

    internal string? ThemeLabel(string group, string id)
    {
        var key = $"theme-labels.{group}.{id}";
        return Find(_translation, key) ?? Find(_upstream, key);
    }

    private static string AppLocale(string tag) =>
        Path.Combine(AppContext.BaseDirectory, "KandoWeb", "locales", tag, "translation.json");

    private static JsonElement Read(string path)
    {
        if (!File.Exists(path)) return default;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    private static string? Find(JsonElement element, string key)
    {
        foreach (var part in key.Split('.'))
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element)) return null;
        return element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value ? value : null;
    }
}
