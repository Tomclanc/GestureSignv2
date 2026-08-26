using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GestureSign.WinUI;

internal static class UiTranslationCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SupportedCultureNames { get; } = new[]
    {
        "lo-LA", "da-DK", "uk-UA", "uz-Latn-UZ", "ur-PK", "hy-AM", "ru-RU", "bg-BG",
        "quz-PE", "hr-HR", "is-IS", "gl-ES", "ca-ES", "hu-HU", "af-ZA", "lb-LU",
        "hi-IN", "id-ID", "gu-IN", "kk-KZ", "tr-TR", "kn-IN", "sr-Latn-RS", "sr-Cyrl-RS",
        "sr-Cyrl-BA", "or-IN", "cy-GB", "kok-IN", "bn-IN", "bn-BD", "ne-NP", "ca-ES-valencia",
        "eu-ES", "he-IL", "el-GR", "de-DE", "it-IT", "lv-LV", "nb-NO", "nn-NO", "cs-CZ",
        "sk-SK", "sl-SI", "sw-KE", "pa-IN", "ja-JP", "ko-KR", "ka-GE", "mi-NZ", "fr-CA",
        "fr-FR", "pl-PL", "bs-Latn-BA", "fa-IR", "te-IN", "ta-IN", "th-TH", "ga-IE", "et-EE",
        "sv-SE", "be-BY", "lt-LT", "zh-CN", "zh-TW", "ug-CN", "ro-RO", "fi-FI", "gd-GB",
        "en-US", "en-GB", "nl-NL", "fil-PH", "pt-BR", "pt-PT", "es-MX", "es-ES", "ha-Latn-NG",
        "vi-VN", "az-Latn-AZ", "am-ET", "sq-AL", "ar-SA", "as-IN", "tt-RU", "mk-MK", "mr-IN",
        "ml-IN", "ms-MY", "mt-MT", "km-KH"
    };

    private static readonly HashSet<string> InlineCultureNames = new(
        new[] { "zh-CN", "zh-TW", "en-US", "en-GB", "ja-JP", "ko-KR" },
        StringComparer.OrdinalIgnoreCase);

    public static string Translate(string cultureName, string english)
    {
        var catalogName = CatalogName(cultureName);
        if (catalogName is null)
            return english;

        var catalog = Load(catalogName);
        return catalog.TryGetValue(english, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : english;
    }

    public static bool HasCatalog(string cultureName)
    {
        var resolved = ResolveCultureName(cultureName);
        return !InlineCultureNames.Contains(resolved) &&
               SupportedCultureNames.Contains(resolved, StringComparer.OrdinalIgnoreCase);
    }

    public static string ResolveCultureName(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return "en-US";

        var exact = SupportedCultureNames.FirstOrDefault(name =>
            string.Equals(name, cultureName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        if (cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            cultureName.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            cultureName.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
            cultureName.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
            return "zh-TW";
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        try
        {
            var requested = CultureInfo.GetCultureInfo(cultureName);
            var language = requested.TwoLetterISOLanguageName;
            foreach (var candidate in SupportedCultureNames)
            {
                if (CultureInfo.GetCultureInfo(candidate).TwoLetterISOLanguageName.Equals(
                        language, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        return "en-US";
    }

    public static string GetNativeDisplayName(string cultureName)
    {
        try
        {
            var nativeName = CultureInfo.GetCultureInfo(cultureName).NativeName;
            return string.IsNullOrWhiteSpace(nativeName) ? cultureName : nativeName;
        }
        catch (CultureNotFoundException)
        {
            return cultureName;
        }
    }

    public static bool IsRightToLeft(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(ResolveCultureName(cultureName)).TextInfo.IsRightToLeft;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> Load(string catalogName)
    {
        lock (SyncRoot)
        {
            if (Catalogs.TryGetValue(catalogName, out var cached))
                return cached;

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Languages", "UI", catalogName + ".json");
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                             ?? new Dictionary<string, string>(StringComparer.Ordinal);
                Catalogs[catalogName] = loaded;
            }
            catch
            {
                Catalogs[catalogName] = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return Catalogs[catalogName];
        }
    }

    private static string? CatalogName(string cultureName)
    {
        var resolved = ResolveCultureName(cultureName);
        return HasCatalog(resolved) ? resolved : null;
    }
}
