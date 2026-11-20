#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Foundation.Steps;

/// <summary>A ready-made action: a step program whose settings are declared variables the user fills in.</summary>
public sealed record ActionTemplate(string Id, string CategoryZh, string CategoryEn, string NameZh, string NameEn,
    string DescriptionZh, string DescriptionEn, string Glyph, Func<bool, StepProgram> Build);

/// <summary>Built-in action templates, grouped by category.</summary>
public static class ActionTemplates
{
    private static Step S(string type, params (string Key, string Value)[] args)
    {
        var step = Step.Create(type);
        foreach (var (key, value) in args)
            step.Args[key] = value;
        return step;
    }

    private static Step With(this Step step, IEnumerable<Step> body, IEnumerable<Step> @else = null)
    {
        step.Body = body.ToList();
        if (@else != null) step.Else = @else.ToList();
        return step;
    }

    private static StepVariableDef Var(string name, string value, string description, string kind = "text")
        => new() { Name = name, Kind = kind, Default = value, Description = description };

    private static StepProgram P(IEnumerable<StepVariableDef> variables, params Step[] steps)
        => new() { Variables = variables.ToList(), Steps = steps.ToList() };

    private static string Z(bool zh, string chinese, string english) => zh ? chinese : english;

    private static Step NoSelection(bool zh) => S(StepCatalog.Notify, ("Text", Z(zh, "没有选中文字", "No text is selected")));

    public static readonly IReadOnlyList<ActionTemplate> All = new[]
    {
        // Web
        new ActionTemplate("search-selection", "网络", "Web", "搜索选中文字", "Search selected text",
            "用搜索引擎搜索选中的文字。可在变量中修改搜索地址。", "Searches the selected text. Change the search address in the variables.", "",
            zh => P(new[] { Var("engineUrl", "https://www.bing.com/search?q=", Z(zh, "搜索地址（关键词接在后面）", "Search address (the query is appended)")) },
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.If, ("Condition", "text.trim() != \"\"")).With(
                    new[] { S(StepCatalog.OpenUrl, ("Url", "{engineUrl}{text:url}")) },
                    new[] { NoSelection(zh) }))),
        new ActionTemplate("ask-search", "网络", "Web", "输入关键词搜索", "Search for a keyword",
            "弹出输入框，输入关键词后搜索。", "Asks for a keyword and searches for it.", "",
            zh => P(new[] { Var("engineUrl", "https://www.bing.com/search?q=", Z(zh, "搜索地址（关键词接在后面）", "Search address (the query is appended)")) },
                S(StepCatalog.UserInput, ("Prompt", Z(zh, "搜索什么？", "Search for?")), ("Output", "query")),
                S(StepCatalog.OpenUrl, ("Url", "{engineUrl}{query:url}")))),
        new ActionTemplate("translate-selection", "网络", "Web", "翻译选中文字", "Translate selected text",
            "在必应翻译中打开选中的文字。", "Opens the selected text in Bing Translator.", "",
            zh => P(new[] { Var("targetLanguage", zh ? "zh-Hans" : "en", Z(zh, "目标语言代码，例如 zh-Hans、en、ja", "Target language code, e.g. zh-Hans, en, ja")) },
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.If, ("Condition", "text.trim() != \"\"")).With(
                    new[] { S(StepCatalog.OpenUrl, ("Url", "https://www.bing.com/translator?from=auto-detect&to={targetLanguage}&text={text:url}")) },
                    new[] { NoSelection(zh) }))),
        new ActionTemplate("open-url", "网络", "Web", "打开网址", "Open a web page",
            "打开固定的网址。", "Opens a fixed address.", "",
            zh => P(new[] { Var("url", "https://www.bing.com", Z(zh, "要打开的网址", "Address to open")) },
                S(StepCatalog.OpenUrl, ("Url", "{url}")))),
        new ActionTemplate("open-selected-url", "网络", "Web", "打开选中的网址", "Open the selected link",
            "从选中的文字中找出网址并打开。", "Finds a link in the selected text and opens it.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.RegexExtract, ("Input", "{text}"), ("Pattern", @"https?://[^\s""'<>]+|www\.[^\s""'<>]+"), ("Output", "link")),
                S(StepCatalog.If, ("Condition", "link != \"\"")).With(
                    new[] { S(StepCatalog.OpenUrl, ("Url", "{link}")) },
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "选中的文字里没有网址", "The selection contains no link"))) }))),

        // Text
        new ActionTemplate("zh-convert", "文本", "Text", "简繁互换", "Simplified/Traditional Chinese",
            "选中的文字含繁体时转为简体，否则转为繁体，并替换原文。", "Converts the selection to Simplified Chinese when it has Traditional characters, otherwise to Traditional, and replaces it.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.If, ("Condition", "text == \"\"")).With(new[] { NoSelection(zh), S(StepCatalog.Stop) }),
                S(StepCatalog.ConvertText, ("Input", "{text}"), ("Mode", "toSimplified"), ("Output", "simplified")),
                S(StepCatalog.If, ("Condition", "simplified != text")).With(
                    new[] { S(StepCatalog.PasteText, ("Text", "{simplified}")), S(StepCatalog.Notify, ("Text", Z(zh, "已转换为简体", "Converted to Simplified Chinese"))) },
                    new[] { S(StepCatalog.ConvertText, ("Input", "{text}"), ("Mode", "toTraditional"), ("Output", "traditional")),
                            S(StepCatalog.PasteText, ("Text", "{traditional}")), S(StepCatalog.Notify, ("Text", Z(zh, "已转换为繁体", "Converted to Traditional Chinese"))) }))),
        new ActionTemplate("toggle-case", "文本", "Text", "大小写切换", "Toggle case",
            "选中的文字全是大写时转小写，否则转大写。", "Lower-cases the selection when it is all upper case, otherwise upper-cases it.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.If, ("Condition", "text == text.toUpperCase()")).With(
                    new[] { S(StepCatalog.ConvertText, ("Input", "{text}"), ("Mode", "lower"), ("Output", "text")) },
                    new[] { S(StepCatalog.ConvertText, ("Input", "{text}"), ("Mode", "upper"), ("Output", "text")) }),
                S(StepCatalog.PasteText, ("Text", "{text}")))),
        new ActionTemplate("clean-lines", "文本", "Text", "去除空行", "Remove blank lines",
            "删除选中文字中的空行。", "Removes blank lines from the selection.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.ReplaceText, ("Input", "{text}"), ("Find", @"(\r?\n)[ \t]*(?=\r?\n)"), ("Replace", ""), ("Regex", "true"), ("Output", "text")),
                S(StepCatalog.SplitText, ("Input", "{text}"), ("Output", "lines")),
                S(StepCatalog.JoinList, ("List", "{lines}"), ("Separator", @"\r\n"), ("Output", "text")),
                S(StepCatalog.PasteText, ("Text", "{text}")))),
        new ActionTemplate("insert-datetime", "文本", "Text", "插入当前时间", "Insert date and time",
            "在光标处粘贴当前日期时间，格式可在变量中修改。", "Pastes the current date and time; change the format in the variables.", "",
            zh => P(new[] { Var("format", "yyyy-MM-dd HH:mm", Z(zh, "日期时间格式", "Date and time format")) },
                S(StepCatalog.GetDateTime, ("Format", "{format}"), ("Output", "now")),
                S(StepCatalog.PasteText, ("Text", "{now}")))),
        new ActionTemplate("snippets", "文本", "Text", "常用短语", "Text snippets",
            "从列表中选一条短语粘贴到光标处。", "Pick a phrase from a list and paste it.", "",
            zh => P(new[] { Var("phrases", zh ? "你好！\n谢谢！\n收到，稍后回复。" : "Hello!\nThank you!\nGot it, I will reply soon.", Z(zh, "短语，每行一条", "Phrases, one per line"), "list") },
                S(StepCatalog.UserSelect, ("Prompt", Z(zh, "选择要粘贴的短语", "Choose a phrase to paste")), ("Items", "{phrases}"), ("Output", "choice")),
                S(StepCatalog.PasteText, ("Text", "{choice}")))),
        new ActionTemplate("js-selection", "文本", "Text", "用 JavaScript 处理选中文字", "Process selection with JavaScript",
            "用一段 JavaScript 处理选中的文字后替换原文。默认把连续空白合并为一个空格。", "Transforms the selection with JavaScript and replaces it. By default collapses runs of whitespace.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.RunJavaScript, ("Code", "return text.replace(/\\s+/g, \" \").trim();"), ("Output", "result")),
                S(StepCatalog.PasteText, ("Text", "{result}")))),

        // Clipboard
        new ActionTemplate("paste-plain", "剪贴板", "Clipboard", "粘贴为纯文本", "Paste as plain text",
            "去掉格式，只粘贴剪贴板中的文字。", "Pastes only the text of the clipboard, without formatting.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetClipboard, ("Output", "clipboard")),
                S(StepCatalog.PasteText, ("Text", "{clipboard}")))),
        new ActionTemplate("copy-file-paths", "剪贴板", "Clipboard", "复制文件路径", "Copy file paths",
            "在资源管理器中复制选中文件的完整路径（每行一个）。", "Copies the full paths of the files selected in File Explorer, one per line.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.SendKeys, ("Keys", "Ctrl+C")),
                S(StepCatalog.Delay, ("Milliseconds", "200")),
                S(StepCatalog.GetClipboardFiles, ("Output", "files")),
                S(StepCatalog.If, ("Condition", "files.length > 0")).With(
                    new[] { S(StepCatalog.JoinList, ("List", "{files}"), ("Separator", @"\r\n"), ("Output", "paths")),
                            S(StepCatalog.SetClipboard, ("Text", "{paths}")),
                            S(StepCatalog.Notify, ("Text", Z(zh, "已复制路径：\n{paths}", "Copied:\n{paths}"))) },
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "没有选中文件", "No files are selected"))) }))),

        // System
        new ActionTemplate("run-program", "系统", "System", "运行程序", "Run a program",
            "运行程序或打开文件、文件夹。", "Runs a program or opens a file or folder.", "",
            zh => P(new[] { Var("path", "notepad.exe", Z(zh, "程序、文件或文件夹路径", "Program, file or folder")),
                            Var("arguments", "", Z(zh, "启动参数，可留空", "Arguments (optional)")) },
                S(StepCatalog.RunProgram, ("Path", "{path}"), ("Arguments", "{arguments}")))),
        new ActionTemplate("send-keys", "系统", "System", "发送快捷键", "Send a shortcut",
            "向当前窗口发送快捷键，可写多组，例如 Ctrl+A Ctrl+C。", "Sends shortcuts to the current window, e.g. Ctrl+A Ctrl+C.", "",
            zh => P(new[] { Var("keys", "Ctrl+Shift+T", Z(zh, "按键，多组用空格分隔", "Keys; separate several with spaces")) },
                S(StepCatalog.SendKeys, ("Keys", "{keys}")))),
    };

    public static ActionTemplate Find(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
