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
Check(UiTranslationCatalog.TranslateFallback("en-GB", "新动作", "New Action") == "New Action", "British English editor caption");
foreach (var culture in UiTranslationCatalog.SupportedCultureNames)
{
    var result = UiTranslationCatalog.TranslateFallback(culture, "这个新字符串没有译文", "A new untranslated UI string");
    Check(result == (culture.StartsWith("zh-") ? "这个新字符串没有译文" : "A new untranslated UI string"), $"Missing translation fallback: {culture}");
}
Check(UiTranslationCatalog.TranslateFallback("invalid-culture", "动作", "Action") == "Action", "Unknown culture fallback");
foreach (var culture in new[] { "ru-RU", "de-DE", "fr-FR" })
{
    var path = Path.Combine(AppContext.BaseDirectory, "Languages", "UI", culture + ".json");
    var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    Check(UiTranslationCatalog.TranslateFallback(culture, "保存", "Save") == catalog["Save"], $"Existing translation retained: {culture}");
}
var stableDirections = new[] { "向右", "向左", "向上", "向下", "左上", "右上", "左下", "右下" };
for (int index = 0; index < stableDirections.Length; index++)
    Check(GestureTemplateDirection.FromIndex(index) == stableDirections[index], $"Stable gesture template direction {index}");
Check(GestureTemplateDirection.FromIndex(-1) == "向右", "Unselected direction default");
var localizationCalls = new HashSet<string> { "L", "T", "F", "IntentText", "IntentFormat" };
var uiProperties = new HashSet<string> { "Text", "PlaceholderText", "Content", "Header", "Title", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "OnContent", "OffContent" };
var chinese = new Regex("[\\u4e00-\\u9fff]");
string Slots(string value) => string.Join(",", Regex.Matches(value, @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})").Select(m => m.Groups[1].Value).Order());
foreach (var file in Directory.GetFiles(Path.Combine(repo, "GestureSign.WinUI"), "MainWindow*.cs"))
{
    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var name = call.Expression.ToString();
        if (name is not ("L" or "T" or "F")) continue;
        var values = call.ArgumentList.Arguments;
        if (values.Count < 2 || values[0].Expression is not LiteralExpressionSyntax zh || values[1].Expression is not LiteralExpressionSyntax en) continue;
        var key = en.Token.ValueText;
        Check(!chinese.IsMatch(key.Replace("风夏", "")), $"Chinese in English baseline: {file}: {key}");
        Check(Slots(zh.Token.ValueText) == Slots(key), $"Mismatched format slots: {key}");
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
var window = File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI", "MainWindow.xaml.cs"));
Check(window.Contains("GestureTemplateDirection.FromIndex(direction.SelectedIndex)"), "Gesture creation uses stable direction, not translated SelectedItem");
var routing = File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI", "MainWindow.CommandRouting.cs"));
Check(routing.Contains("case \"新建手势\":") && routing.Contains("case \"添加程序\":"), "Command IDs remain stable");
Console.WriteLine($"PASS: {checks} UI localization checks");