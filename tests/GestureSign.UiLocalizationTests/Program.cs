using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestureSign.WinUI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

int checks = 0;
void Check(bool passed, string description)
{
    if (!passed) throw new Exception(description);
    checks++;
}
var repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
Check(UiTranslationCatalog.TranslateFallback("en-US", "新动作", "New Action") == "New Action", "English editor caption");
Check(UiTranslationCatalog.Translate("ja-JP", "Default configuration") == "既定の設定", "Japanese default data source");
Check(UiTranslationCatalog.Translate("ja-JP", "User configuration") == "ユーザー設定", "Japanese user data source");
Check(UiTranslationCatalog.Translate("ja-JP", "New Gesture") == "新しいジェスチャー", "Japanese new gesture default name");
Check(UiTranslationCatalog.TranslateMessage("ja-JP", "No OneDrive folder was detected. Sign in to OneDrive first.") ==
    "OneDrive フォルダーが見つかりません。先に OneDrive にサインインしてください。", "English exception translated to Japanese");
Check(UiTranslationCatalog.TranslateMessage("ja-JP", "未检测到 OneDrive 文件夹。请先登录并启用 OneDrive。") ==
    "OneDrive フォルダーが見つかりません。先に OneDrive にサインインしてください。", "Legacy Chinese exception translated at display boundary");
var customPath = "C:/用户自定义/Right/未检测到 OneDrive 文件夹。/file.json";
Check(UiTranslationCatalog.TranslateMessage("ja-JP", customPath) == customPath, "Do not translate text inside user paths");
foreach (var (key, expected) in new[] { ("Left button", "左ボタン"), ("Middle button", "中央ボタン"), ("Right button", "右ボタン") })
    Check(UiTranslationCatalog.Translate("ja-JP", key) == expected, "Consistent Japanese mouse button terminology");
Check(UiTranslationCatalog.TranslateFallback("en-GB", "新动作", "New Action") == "New Action", "British English editor caption");
foreach (var culture in UiTranslationCatalog.SupportedCultureNames)
{
    var result = UiTranslationCatalog.TranslateFallback(culture, "这个新字符串没有译文", "A new untranslated UI string");
    Check(result == (culture == "zh-CN" ? "这个新字符串没有译文" : "A new untranslated UI string"), $"Missing translation fallback: {culture}");
}
Check(UiTranslationCatalog.TranslateFallback("invalid-culture", "动作", "Action") == "Action", "Unknown culture fallback");
Check(UiTranslationCatalog.ResolveCultureName("sr-Cyrl-ME") == "sr-Cyrl-RS", "Regional fallback preserves Cyrillic script");
Check(UiTranslationCatalog.ResolveCultureName("sr-Latn-BA") == "sr-Latn-RS", "Regional fallback preserves Latin script");
Check(UiTranslationCatalog.ResolveCultureName(" sr_Cyrl_ME ") == "sr-Cyrl-RS", "Normalize culture separators and whitespace");
Check(UiTranslationCatalog.ResolveCultureName("zh-HK") == "zh-TW", "Hong Kong uses Traditional Chinese");
foreach (var culture in UiTranslationCatalog.SupportedCultureNames)
{
    var korean = culture == "ko-KR";
    Check(ApplicationProductNames.DisplayName(culture, "밀리의 서재", "millie-desktop.exe") ==
        (korean ? "밀리의 서재" : "Millie's Library"), $"Imported Millie product label: {culture}");
    Check(ApplicationProductNames.DisplayName(culture, "카카오톡", "KakaoTalk.exe") ==
        (korean ? "카카오톡" : "KakaoTalk"), $"Imported KakaoTalk product label: {culture}");
}
Check(ApplicationProductNames.DisplayName("ko-KR", "KakaoTalk", "KakaoTalk.exe") == "카카오톡",
    "A newly captured application follows the Korean UI");
Check(ApplicationProductNames.DisplayName("zh-CN", "내 채팅 규칙", "KakaoTalk.exe") == "내 채팅 규칙",
    "A custom rule name is preserved even for a known executable");
Check(ApplicationProductNames.DisplayName("zh-CN", "카카오톡", "unknown.exe") == "카카오톡",
    "An identical caption on an unrelated executable is preserved");
Check(ApplicationProductNames.DisplayName("zh-CN", "Microsoft\u200B Edge", "msedge.exe") == "Microsoft Edge",
    "Known product names discard invisible Shell formatting");
Check(ApplicationProductNames.DisplayName("zh-CN", "Apple\u00A0TV", "AppleTV.exe") == "Apple TV",
    "Known product names normalize nonbreaking spaces");
Check(ApplicationProductNames.SuggestedName("millie-desktop.exe") == "Millie's Library",
    "Executable capture suggests the stable product name");
foreach (var culture in new[] { "ar-SA", "fa-IR", "he-IL", "ur-PK", "ug-CN" })
    Check(UiTranslationCatalog.IsRightToLeft(culture), $"Right-to-left language: {culture}");
Check(!UiTranslationCatalog.IsRightToLeft("de-DE"), "Left-to-right language");
Check(!Regex.IsMatch(UiTranslationCatalog.TranslateComponent("sr-Latn-RS", "Custom command"), "[\\u0400-\\u04ff]"), "Serbian Latin catalog keeps Latin script");
Check(Regex.IsMatch(UiTranslationCatalog.TranslateComponent("sr-Cyrl-ME", "Custom command"), "[\\u0400-\\u04ff]"), "Serbian regional fallback returns Cyrillic text");
foreach (var culture in new[] { "ru-RU", "de-DE", "fr-FR", "ja-JP" })
{
    var path = Path.Combine(AppContext.BaseDirectory, "Languages", "UI", culture + ".json");
    var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    Check(UiTranslationCatalog.TranslateFallback(culture, "保存", "Save") == catalog["Save"], $"Existing translation retained: {culture}");
}
Check(UiTranslationCatalog.HasCatalog("ja-JP"), "Japanese uses the external UI catalog");
Check(UiTranslationCatalog.TranslateFallback("ja-JP", "程序名称", "App Name") == "アプリ名", "Two-language editor captions use contributed Japanese translations");
var japanesePath = Path.Combine(AppContext.BaseDirectory, "Languages", "UI", "ja-JP.json");
using var japaneseDocument = JsonDocument.Parse(File.ReadAllText(japanesePath));
var japaneseEntries = japaneseDocument.RootElement.EnumerateObject().ToArray();
Check(japaneseEntries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() == japaneseEntries.Length, "No duplicate Japanese keys");
var japaneseCatalog = japaneseEntries.ToDictionary(e => e.Name, e => e.Value.GetString()!, StringComparer.Ordinal);
var stableDirections = new[] { "向右", "向左", "向上", "向下", "左上", "右上", "左下", "右下" };
for (int index = 0; index < stableDirections.Length; index++)
    Check(GestureTemplateDirection.FromIndex(index) == stableDirections[index], $"Stable gesture template direction {index}");
Check(GestureTemplateDirection.FromIndex(-1) == "向右", "Unselected direction default");
var localizationCalls = new HashSet<string> { "L", "T", "F", "IntentText", "IntentFormat", "KandoText", "KandoFormat" };
var uiProperties = new HashSet<string> { "Text", "PlaceholderText", "Content", "Header", "Title", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "OnContent", "OffContent" };
var chinese = new Regex("[\\u4e00-\\u9fff]");
string Slots(string value) => string.Join(",", Regex.Matches(value, @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})").Select(m => m.Groups[1].Value).Order());
var productionUiFiles = Directory.GetFiles(Path.Combine(repo, "GestureSign.WinUI"), "*.cs", SearchOption.AllDirectories)
    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")).ToArray();
var requiredUiKeys = new HashSet<string>(StringComparer.Ordinal);
foreach (var file in productionUiFiles)
{
    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var name = call.Expression.ToString();
        if (name is not ("L" or "T" or "F")) continue;
        var values = call.ArgumentList.Arguments;
        if (values.Count < 2 || values[0].Expression is not LiteralExpressionSyntax zh || values[1].Expression is not LiteralExpressionSyntax en) continue;
        var key = en.Token.ValueText;
        requiredUiKeys.Add(key);
        Check(!chinese.IsMatch(key.Replace("风夏", "")), $"Chinese in English baseline: {file}: {key}");
        Check(Slots(zh.Token.ValueText) == Slots(key), $"Mismatched format slots: {key}");
        if (name == "L")
        {
            Check(values.Count == 4, "Japanese strings are not embedded in L() calls");
            Check(japaneseCatalog.ContainsKey(key), $"Existing Japanese translation preserved: {key}");
        }
        if (name == "F")
        {
            // A user's Chinese name must stay untouched within a translated sentence.
            var parameters = Enumerable.Repeat<object>("用户自定义名称", values.Count - 2).ToArray();
            var formatted = string.Format(CultureInfo.InvariantCulture, key, parameters);
            Check(formatted.Contains("用户自定义名称"), $"User data lost in formatted message: {key}");
        }
    }
    foreach (var comparison in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
    {
        if (comparison.Left is not MemberAccessExpressionSyntax field || field.Name.Identifier.ValueText != "Type") continue;
        Check(!comparison.Right.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(c => localizationCalls.Contains(c.Expression.ToString())), "Application type comparison must use stored values, not localized captions");
    }
    foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(n => n.IsKind(SyntaxKind.StringLiteralExpression)))
    {
        if (!chinese.IsMatch(literal.Token.ValueText)) continue;
        if (literal.Ancestors().OfType<InvocationExpressionSyntax>().Any(c => localizationCalls.Contains(c.Expression.ToString()))) continue;
        var assignment = literal.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if (assignment == null) continue;
        var property = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
        if (!uiProperties.Contains(property)) continue;
        // Only direct caption/default text expressions; nested event/data code is separate.
        if (literal.Ancestors().Any(n => n is AnonymousFunctionExpressionSyntax or ConstantPatternSyntax)) continue;
        Check(false, $"Hard-coded UI caption: {Path.GetFileName(file)}:{literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {literal.Token.ValueText}");
    }
}
// Include catalog-only entries used by dynamic captions and older components.
var catalogs = UiTranslationCatalog.SupportedCultureNames.ToDictionary(culture => culture, culture =>
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Languages", "UI", culture + ".json")));
    var entries = doc.RootElement.EnumerateObject().ToArray();
    Check(entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() == entries.Length, $"No duplicate UI keys: {culture}");
    return entries.ToDictionary(e => e.Name, e => e.Value.GetString()!, StringComparer.Ordinal);
});
foreach (var catalog in catalogs.Values) requiredUiKeys.UnionWith(catalog.Keys);
var iconLibraries = GestureSign.PieMenu.MenuIcons.Themes;
foreach (var (culture, catalog) in catalogs)
{
    var names = iconLibraries.Select(library => UiTranslationCatalog.TranslateComponent(culture, library.Name)).ToArray();
    Check(iconLibraries.All(library => catalog.ContainsKey(library.Name)), $"All icon libraries have captions: {culture}");
    Check(names.Distinct(StringComparer.Ordinal).Count() == iconLibraries.Length, $"Icon library captions are distinct: {culture}");
}
var iconLanguageSwitch = new[] { "zh-CN", "en-US", "zh-TW", "ja-JP", "ko-KR", "zh-CN" };
foreach (var culture in iconLanguageSwitch.Where(culture => culture != "en-US"))
{
    foreach (var library in iconLibraries.Take(4))
        Check(!Regex.IsMatch(UiTranslationCatalog.TranslateComponent(culture, library.Name), "[A-Za-z]"),
            $"Icon library caption uses the active language: {culture}: {library.Id}");
}
Check(UiTranslationCatalog.TranslateComponent("en-US", iconLibraries[0].Name) == "Built-in menu icons",
    "English icon library caption remains the library name");
// These workflows previously passed key/format checks while their values were
// English copies. Guard the actual translations as well as their presence.
foreach (var (culture, catalog) in catalogs.Where(entry => !entry.Key.StartsWith("en-")))
foreach (var key in new[] { "Export diagnostics", "Advanced options", "Custom command", "Back up and import",
    "This import replaces the configuration files included in the archive; it does not merge them. Existing files are backed up and restored if writing fails." })
    Check(catalog.TryGetValue(key, out var text) && text != key, $"Workflow must be translated: {culture}: {key}");
foreach (var (culture, catalog) in catalogs)
foreach (var key in requiredUiKeys)
{
    Check(catalog.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), $"Missing UI text: {culture}: {key}");
    Check(Slots(key) == Slots(value!), $"UI format items preserved: {culture}: {key}");
    if (Regex.IsMatch(key, @"(?<!\{)\{\d+(?:[,}:])"))
    {
        try { _ = string.Format(CultureInfo.GetCultureInfo(culture), value!, Enumerable.Repeat<object>(427, 12).ToArray()); }
        catch (FormatException) { Check(false, $"UI format can execute: {culture}: {key}"); }
    }
    Check(UiTranslationCatalog.TranslateComponent(culture, key) == value, $"Runtime UI catalog lookup: {culture}: {key}");
    if (culture != "zh-CN")
        Check(UiTranslationCatalog.TranslateFallback(culture, "禁止简体中文回退", key) == value,
            $"T/F must use selected language: {culture}: {key}");
}
foreach (var entry in japaneseCatalog)
{
    Check(!string.IsNullOrWhiteSpace(entry.Value), $"Empty Japanese translation: {entry.Key}");
    Check(Slots(entry.Key) == Slots(entry.Value), $"Japanese placeholders preserved: {entry.Key}");
}
var window = File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI", "MainWindow.xaml.cs"));
Check(window.Contains("GestureTemplateDirection.FromIndex(direction.SelectedIndex)"), "Gesture creation uses stable direction, not translated SelectedItem");
var routing = File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI", "MainWindow.CommandRouting.cs"));
Check(routing.Contains("case \"新建手势\":") && routing.Contains("case \"添加程序\":"), "Command IDs remain stable");
Console.WriteLine($"PASS: {checks} UI localization checks");

// Verify this dialog in every supported culture, including normally inline ones.
var kandoKeys = new[] { "MainWindow.KandoUpdates.cs", "MainWindow.xaml.cs" }
    .SelectMany(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI", file))).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
    .Where(c => c.Expression.ToString() is "KandoText" or "KandoFormat")
    .Where(c => c.ArgumentList.Arguments.Count >= 2 && c.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax)
    .Select(c => ((LiteralExpressionSyntax)c.ArgumentList.Arguments[1].Expression).Token.ValueText).Distinct().ToArray();
foreach (var culture in UiTranslationCatalog.SupportedCultureNames)
{
    var catalog = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Languages", "UI", culture + ".json")))!;
    foreach (var key in kandoKeys)
    {
        Check(catalog.TryGetValue(key, out var translated) && !string.IsNullOrWhiteSpace(translated), $"Missing Kando dialog text: {culture}: {key}");
        Check(Slots(key) == Slots(translated!), $"Kando dialog placeholder mismatch: {culture}: {key}");
        Check(UiTranslationCatalog.TranslateComponent(culture, key) == translated, $"Kando component runtime lookup: {culture}: {key}");
        _ = string.Format(CultureInfo.InvariantCulture, translated!, 50, "v3.0.0");
    }
}
Console.WriteLine($"PASS: {checks} total UI checks, {UiTranslationCatalog.SupportedCultureNames.Count} Kando dialog locales");
