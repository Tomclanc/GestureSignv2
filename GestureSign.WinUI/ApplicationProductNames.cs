using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GestureSign.WinUI;

internal static class ApplicationProductNames
{
    private sealed record Product(string Name, string? Korean, params string[] LegacyNames)
    {
        internal bool Matches(string name) =>
            string.Equals(Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Korean, name, StringComparison.OrdinalIgnoreCase) ||
            LegacyNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, Product> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["millie-desktop.exe"] = new("Millie's Library", "밀리의 서재", "밀리의서재", "Millie’s Library", "Millie"),
        ["KakaoTalk.exe"] = new("KakaoTalk", "카카오톡", "Kakao Talk"),
        ["firefox.exe"] = new("Mozilla Firefox", null, "Firefox", "Firefox Privacy Notice — Mozilla — Mozilla Firefox"),
        ["msedge.exe"] = new("Microsoft Edge", null, "Edge"),
        ["chrome.exe"] = new("Chrome", null, "Google Chrome"),
        ["AppleTV.exe"] = new("Apple TV", null),
        ["ONENOTE.EXE"] = new("OneNote", null, "Microsoft OneNote"),
        ["Discord.exe"] = new("Discord", null),
        ["DiscordPTB.exe"] = new("Discord PTB", null, "DiscordPTB"),
        ["DiscordCanary.exe"] = new("Discord Canary", null, "DiscordCanary")
    };

    internal static string DisplayName(string culture, string savedName, string executable)
    {
        if (!Products.TryGetValue(executable.Trim(), out var product))
            return savedName;

        // Shell-provided product titles can contain nonbreaking spaces or invisible layout marks.
        var normalized = savedName.Replace('\u00A0', ' ').Replace("\u200B", "").Trim();
        if (!product.Matches(normalized)) return savedName;
        return culture == "ko-KR" && product.Korean is { } korean ? korean : product.Name;
    }

    internal static string SuggestedName(string executable)
        => Products.TryGetValue(executable.Trim(), out var product)
            ? product.Name
            : Path.GetFileNameWithoutExtension(executable);
}
