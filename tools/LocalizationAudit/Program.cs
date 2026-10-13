using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GestureSign.WinUI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var output = Path.GetFullPath(args.Skip(1).FirstOrDefault() ?? "localization-audit.json");
var payload = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
var cultures = UiTranslationCatalog.SupportedCultureNames;
var calls = new List<TextCall>();
var hardcoded = new List<string>();
var technicalCaptions = new HashSet<string>(StringComparer.Ordinal)
{ "JSON", "GestureSign V2", "Win", "X", "Y", "TipTap", "exe", "CPU", "GPU", "NPU", "NPU / GPU / CPU" };
var uiProperties = new HashSet<string> { "Text", "Content", "Header", "Title", "PlaceholderText", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "OnContent", "OffContent" };
var names = new HashSet<string> { "L", "T", "F", "IntentText", "IntentFormat", "KandoText", "KandoFormat" };
foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "GestureSign.WinUI"), "*.cs", SearchOption.AllDirectories)
    .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin")))
{
    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var name = call.Expression.ToString();
        if (!names.Contains(name)) continue;
        var a = call.ArgumentList.Arguments;
        var index = name == "IntentFormat" ? 0 : 1;
        if (a.Count <= index || a[index].Expression is not LiteralExpressionSyntax en || !en.IsKind(SyntaxKind.StringLiteralExpression)) continue;
        string? Literal(int i) => a.Count > i && a[i].Expression is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression) ? l.Token.ValueText : null;
        calls.Add(new(name.StartsWith("Intent") ? "Intent" : "UI", en.Token.ValueText,
            name == "IntentFormat" ? null : Literal(0), name == "L" ? Literal(2) : null,
            name == "L" ? Literal(3) : null, Path.GetRelativePath(repo, file), call.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
    }
    foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
    {
        var property = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
        if (!uiProperties.Contains(property)) continue;
        if (assignment.Right is LiteralExpressionSyntax caption && caption.IsKind(SyntaxKind.StringLiteralExpression)
            && Regex.IsMatch(caption.Token.ValueText, "[A-Za-z]") && !technicalCaptions.Contains(caption.Token.ValueText))
            hardcoded.Add($"{Path.GetRelativePath(repo, file)}:{caption.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: unlocalized caption: {caption.Token.ValueText}");
        foreach (var literal in assignment.Right.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            if (!Regex.IsMatch(literal.Token.ValueText, "[\\u4e00-\\u9fff]")) continue;
            if (literal.Ancestors().OfType<InvocationExpressionSyntax>().Any(c => names.Contains(c.Expression.ToString()))) continue;
            if (literal.Ancestors().Any(n => n is AnonymousFunctionExpressionSyntax or ConstantPatternSyntax)) continue;
            hardcoded.Add($"{Path.GetRelativePath(repo, file)}:{literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {literal.Token.ValueText}");
        }
    }
}
foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "GestureSign.WinUI"), "*.xaml", SearchOption.AllDirectories)
    .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin")))
{
    var document = XDocument.Load(file);
    foreach (var element in document.Descendants())
    foreach (var attribute in element.Attributes().Where(a => uiProperties.Contains(a.Name.LocalName)))
        if (Regex.IsMatch(attribute.Value, "[\\u4e00-\\u9fff]") && !element.Attributes().Any(a => a.Name.LocalName == "Uid"))
            hardcoded.Add($"{Path.GetRelativePath(repo, file)}: {attribute.Name.LocalName}={attribute.Value}");
}
Dictionary<string, string> Read(string path, List<string> issues)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var item in doc.RootElement.EnumerateObject())
        if (!result.TryAdd(item.Name, item.Value.GetString() ?? "")) issues.Add("Duplicate key: " + item.Name);
    return result;
}
var uiKeys = new SortedSet<string>(calls.Where(c => c.Domain == "UI").Select(c => c.Key), StringComparer.Ordinal);
var intentKeys = new SortedSet<string>(calls.Where(c => c.Domain == "Intent").Select(c => c.Key), StringComparer.Ordinal);
// Include dynamic/legacy catalog entries, not only literal calls found above.
foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "GestureSign.WinUI/Languages/UI"), "*.json"))
    uiKeys.UnionWith(Read(file, new()).Keys);
foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "GestureSign.Foundation/Localization/Intent"), "*.json"))
    intentKeys.UnionWith(Read(file, new()).Keys);
var allowPath = Path.Combine(repo, "tools/localization-unchanged-allowlist.json");
var allow = File.Exists(allowPath) ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(allowPath))! : new();
bool Allowed(string culture, string key) => culture.StartsWith("en-") ||
    allow.TryGetValue("*", out var common) && common.Contains(key, StringComparer.Ordinal) ||
    allow.TryGetValue(culture, out var local) && local.Contains(key, StringComparer.Ordinal);
string Slots(string text) => string.Join("|", Regex.Matches(text, @"(?<!\{)\{\d+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})").Select(m => m.Value).Order(StringComparer.Ordinal));
var results = new List<object>(); int errors = hardcoded.Count;
foreach (var culture in cultures)
{
    var issues = new List<string>();
    var missing = new List<string>(); var unchanged = new List<string>();
    foreach (var (domain, directory, keys) in new[] { ("UI", "GestureSign.WinUI/Languages/UI", uiKeys), ("Intent", "GestureSign.Foundation/Localization/Intent", intentKeys) })
    {
        var path = Path.Combine(repo, directory, culture + ".json");
        if (!File.Exists(path)) { issues.Add(domain + ": missing catalog"); continue; }
        var catalog = Read(path, issues);
        foreach (var key in keys)
        {
            if (!catalog.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) { missing.Add(domain + ": " + key); continue; }
            if (Slots(key) != Slots(value)) issues.Add(domain + ": format mismatch: " + key);
            if (value.Contains('\uFFFD') || Regex.IsMatch(value, @"ZXQ(?:PH|SEP)|QXZ|\u202A|\u202B|\u202C")) issues.Add(domain + ": corrupt translation: " + key);
            if (!culture.StartsWith("en-") && Regex.IsMatch(value, @"\bTouchpad (?:Top|Bottom|Left|Right) Edge\b", RegexOptions.IgnoreCase))
                issues.Add(domain + ": untranslated edge phrase: " + key);
            if (string.Equals(Regex.Replace(value.Trim(), @"\s+", " "), Regex.Replace(key.Trim(), @"\s+", " "), StringComparison.OrdinalIgnoreCase)
                && !Allowed(culture, key)) unchanged.Add(domain + ": " + key);
            if (culture is not ("ja-JP" or "zh-CN" or "zh-TW") && Regex.IsMatch(value.Replace("风夏", ""), "[\\u4e00-\\u9fff]")) issues.Add(domain + ": Chinese text in translation: " + key);
        }
    }
    errors += missing.Count + issues.Count + unchanged.Count;
    results.Add(new { Culture = culture, Missing = missing, Issues = issues, Unchanged = unchanged });
}
var payloadIssues = new List<string>();
if (payload is not null)
{
    foreach (var culture in cultures)
    {
        var file = Path.Combine(payload, "Languages", "UI", culture + ".json");
        if (!File.Exists(file) || !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(repo, "GestureSign.WinUI", "Languages", "UI", culture + ".json"))))
            payloadIssues.Add("Missing or stale published UI catalog: " + culture);
    }
    var foundationPath = Path.Combine(payload, "GestureSign.Foundation.dll");
    if (!File.Exists(foundationPath)) payloadIssues.Add("Missing published GestureSign.Foundation.dll");
    else
    {
        var assembly = System.Reflection.Assembly.LoadFile(foundationPath);
        foreach (var culture in cultures)
        {
            using var stream = assembly.GetManifestResourceStream("Intent." + culture + ".json");
            using var buffer = new MemoryStream();
            stream?.CopyTo(buffer);
            if (stream is null || !buffer.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(repo, "GestureSign.Foundation", "Localization", "Intent", culture + ".json"))))
                payloadIssues.Add("Missing or stale published AI catalog: " + culture);
        }
    }
}
errors += payloadIssues.Count;
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new { Cultures = cultures.Count, Calls = calls.Count, UiKeys = uiKeys, IntentKeys = intentKeys, Sources = calls, HardcodedCaptions = hardcoded, Languages = results, PayloadChecked = payload is not null, PayloadIssues = payloadIssues, Errors = errors },
    new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Localization audit: {cultures.Count} cultures, {calls.Count} calls, UI={uiKeys.Count}, Intent={intentKeys.Count}, errors={errors}. Report: {output}");
return errors == 0 ? 0 : 1;

record TextCall(string Domain, string Key, string? Chinese, string? TraditionalChinese, string? Korean, string File, int Line);
