using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GestureSign.WinUI;

internal static class UiTranslationCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

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
        if (cultureName.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr-FR";
        if (cultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru-RU";
        if (cultureName.StartsWith("ar", StringComparison.OrdinalIgnoreCase)) return "ar-SA";
        if (cultureName.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es-ES";
        if (cultureName.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        return null;
    }
}
