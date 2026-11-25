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

    private static Step NoSelection(bool zh) => S(StepCatalog.Notify, ("Text", Z(zh, "没有选中文字", "No text is selected")), ("Kind", "warn"));

    /// <summary>Gets the selection into {text}; stops with a message when nothing is selected.</summary>
    private static Step RequireSelection(bool zh)
        => S(StepCatalog.Group, ("Title", Z(zh, "获取选中的文字", "Get the selected text"))).With(new[]
        {
            S(StepCatalog.GetSelectedText, ("Output", "text")),
            S(StepCatalog.If, ("Condition", "text.trim() == \"\"")).With(new[] { NoSelection(zh), S(StepCatalog.Stop) })
        });

    private static Step Thinking(bool zh) => S(StepCatalog.Notify, ("Text", Z(zh, "AI 处理中…", "Asking AI…")));

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
        new ActionTemplate("search-choose-engine", "网络", "Web", "选择搜索引擎搜索", "Search with a chosen engine",
            "选中文字后触发，弹出列表选择搜索引擎。列表每行写「名称|搜索地址」，可以自己增删。", "Pick a search engine from a list for the selected text. Each line of the list is \"name|search address\"; add or remove lines as you like.", "\uE721",
            zh => P(new[] { Var("engines", Z(zh,
                                "必应|https://www.bing.com/search?q=\n百度|https://www.baidu.com/s?wd=\nGoogle|https://www.google.com/search?q=\nGitHub|https://github.com/search?q=\n知乎|https://www.zhihu.com/search?q=",
                                "Bing|https://www.bing.com/search?q=\nGoogle|https://www.google.com/search?q=\nDuckDuckGo|https://duckduckgo.com/?q=\nGitHub|https://github.com/search?q=\nWikipedia|https://en.wikipedia.org/w/index.php?search="),
                                Z(zh, "搜索引擎（每行：名称|地址）", "Engines (one per line: name|address)"), "list") },
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.If, ("Condition", "text.trim() == \"\"")).With(
                    new[] { S(StepCatalog.UserInput, ("Prompt", Z(zh, "搜索什么？", "Search for?")), ("Output", "text")) },
                    Array.Empty<Step>()),
                S(StepCatalog.UserSelect, ("Prompt", Z(zh, "用哪个搜索？", "Search with?")), ("Items", "{engines}"), ("Output", "engine")),
                S(StepCatalog.OpenUrl, ("Url", "{engine}{text:url}")))),
        new ActionTemplate("compose-email", "网络", "Web", "填表写邮件", "Write an email from a form",
            "在一个窗口里填收件人、主题、正文，然后用默认邮件程序打开草稿。演示「表单」步骤。", "Fill in recipient, subject and body in one window, then open a draft in the default mail app. Shows the Form step.", "\uE715",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.UserForm, ("Title", Z(zh, "写邮件", "New email")),
                    ("Fields", Z(zh, "to|收件人|text|\nsubject|主题|text|\nbody|正文|multiline|", "to|To|text|\nsubject|Subject|text|\nbody|Message|multiline|"))),
                S(StepCatalog.OpenUrl, ("Url", "mailto:{to}?subject={subject:url}&body={body:url}")))),
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
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "选中的文字里没有网址", "The selection contains no link")), ("Kind", "warn")) }))),

        // AI (uses the AI settings: API address, key and model)
        new ActionTemplate("ai-translate", "AI", "AI", "AI 翻译选中文字", "AI: translate selection",
            "用 AI 翻译选中的文字，在窗口中显示译文。", "Translates the selected text with AI and shows the result.", "\uE82F",
            zh => P(new[] { Var("targetLanguage", zh ? "简体中文" : "English", Z(zh, "目标语言", "Target language")) },
                RequireSelection(zh),
                Thinking(zh),
                S(StepCatalog.AiChat, ("SystemPrompt", Z(zh, "你是专业翻译。把用户的文字翻译成{targetLanguage}，保留原有格式，只输出译文。", "You are a professional translator. Translate the user's text into {targetLanguage}, keep the formatting, and output only the translation.")),
                    ("Prompt", "{text}"), ("Output", "result")),
                S(StepCatalog.ShowText, ("Title", Z(zh, "翻译", "Translation")), ("Text", "{result}")))),
        new ActionTemplate("ai-rewrite", "AI", "AI", "AI 润色改写", "AI: polish selection",
            "让表达更通顺，直接替换选中的文字。", "Makes the selected text read better and replaces it.", "\uE82F",
            zh => P(new[] { Var("style", Z(zh, "通顺、自然、简洁", "clear, natural and concise"), Z(zh, "改写风格", "Style")) },
                RequireSelection(zh),
                Thinking(zh),
                S(StepCatalog.AiChat, ("SystemPrompt", Z(zh, "把用户的文字改写得{style}，保持原语言和原意，只输出改写后的文字。", "Rewrite the user's text to be {style}. Keep the language and meaning, and output only the rewritten text.")),
                    ("Prompt", "{text}"), ("Output", "result")),
                S(StepCatalog.PasteText, ("Text", "{result}")))),
        new ActionTemplate("ai-fix", "AI", "AI", "AI 修正错别字和语法", "AI: fix spelling and grammar",
            "修正选中文字的错别字、标点和语法，直接替换。", "Fixes spelling, punctuation and grammar in the selection and replaces it.", "\uE82F",
            zh => P(Array.Empty<StepVariableDef>(),
                RequireSelection(zh),
                Thinking(zh),
                S(StepCatalog.AiChat, ("SystemPrompt", Z(zh, "修正用户文字中的错别字、标点和语法错误，不改变意思和风格，只输出修正后的文字。", "Fix spelling, punctuation and grammar mistakes in the user's text without changing its meaning or style. Output only the corrected text.")),
                    ("Prompt", "{text}"), ("Output", "result")),
                S(StepCatalog.PasteText, ("Text", "{result}")))),
        new ActionTemplate("ai-summarize", "AI", "AI", "AI 总结", "AI: summarize",
            "总结选中文字的要点。", "Summarizes the key points of the selected text.", "\uE82F",
            zh => P(Array.Empty<StepVariableDef>(),
                RequireSelection(zh),
                Thinking(zh),
                S(StepCatalog.AiChat, ("SystemPrompt", Z(zh, "用简体中文分条总结用户文字的要点，简洁准确。", "Summarize the key points of the user's text as a short bulleted list.")),
                    ("Prompt", "{text}"), ("Output", "result")),
                S(StepCatalog.ShowText, ("Title", Z(zh, "总结", "Summary")), ("Text", "{result}")))),
        new ActionTemplate("ai-explain", "AI", "AI", "AI 解释", "AI: explain",
            "解释选中的代码、术语或句子。", "Explains the selected code, term or sentence.", "\uE82F",
            zh => P(Array.Empty<StepVariableDef>(),
                RequireSelection(zh),
                Thinking(zh),
                S(StepCatalog.AiChat, ("SystemPrompt", Z(zh, "用通俗易懂的简体中文解释用户给出的内容（代码、术语或句子），必要时举例。", "Explain what the user gives you (code, a term or a sentence) in plain language, with an example if useful.")),
                    ("Prompt", "{text}"), ("Output", "result")),
                S(StepCatalog.ShowText, ("Title", Z(zh, "解释", "Explanation")), ("Text", "{result}")))),
        new ActionTemplate("ai-ask", "AI", "AI", "AI 提问（带上选中文字）", "AI: ask about selection",
            "输入问题，连同选中的文字一起发给 AI。", "Asks a question about the selected text.", "\uE82F",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedText, ("Output", "text")),
                S(StepCatalog.UserInput, ("Prompt", Z(zh, "想问 AI 什么？", "What do you want to ask?")), ("Output", "question")),
                Thinking(zh),
                S(StepCatalog.AiChat, ("Prompt", Z(zh, "{question}\n\n参考内容：\n{text}", "{question}\n\nContext:\n{text}")), ("Output", "result")),
                S(StepCatalog.ShowText, ("Title", "AI"), ("Text", "{result}")))),

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
            "复制资源管理器或桌面中选中文件的完整路径（每行一个）。", "Copies the full paths of the files selected in File Explorer or on the desktop, one per line.", "",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.GetSelectedFiles, ("Output", "files")),
                S(StepCatalog.If, ("Condition", "files.length > 0")).With(
                    new[] { S(StepCatalog.JoinList, ("List", "{files}"), ("Separator", @"\r\n"), ("Output", "paths")),
                            S(StepCatalog.SetClipboard, ("Text", "{paths}")),
                            S(StepCatalog.Notify, ("Text", Z(zh, "已复制路径：\n{paths}", "Copied:\n{paths}"))) },
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "没有选中文件", "No files are selected")), ("Kind", "warn")) }))),

        // Files
        new ActionTemplate("open-with", "文件", "Files", "用指定程序打开选中文件", "Open selected files with a program",
            "在资源管理器或桌面选中文件后触发，用指定的程序打开它们，例如用 Photoshop 打开图片。", "Opens the files selected in File Explorer or on the desktop with a chosen program, e.g. images in Photoshop.", "\uE7AC",
            zh => P(new[] { Var("program", @"C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe", Z(zh, "用来打开文件的程序", "Program that opens the files")),
                            Var("extraArguments", "", Z(zh, "放在文件前面的额外参数，可留空", "Extra arguments before the files (optional)")) },
                S(StepCatalog.GetSelectedFiles, ("Output", "files")),
                S(StepCatalog.If, ("Condition", "files.length > 0")).With(
                    new[] { S(StepCatalog.RunProgram, ("Path", "{program}"), ("Arguments", "{extraArguments} {files:quote}")) },
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "没有选中文件", "No files are selected")), ("Kind", "warn")) }))),
        new ActionTemplate("terminal-here", "文件", "Files", "在当前文件夹打开终端", "Open a terminal here",
            "在资源管理器当前打开的文件夹中启动 Windows 终端。用 cmd.exe 时把参数改成 /k cd /d {folder:quote}。", "Starts Windows Terminal in the folder open in File Explorer. For cmd.exe use the arguments /k cd /d {folder:quote}.", "\uE756",
            zh => P(new[] { Var("terminal", "wt.exe", Z(zh, "终端程序", "Terminal program")),
                            Var("admin", "false", Z(zh, "以管理员身份运行", "Run as administrator"), "bool") },
                S(StepCatalog.GetExplorerFolder, ("Output", "folder")),
                S(StepCatalog.If, ("Condition", "folder != \"\"")).With(
                    new[] { S(StepCatalog.RunProgram, ("Path", "{terminal}"), ("Arguments", "-d {folder:quote}"), ("WorkingDirectory", "{folder}"), ("RunAsAdmin", "{admin}")) },
                    new[] { S(StepCatalog.RunProgram, ("Path", "{terminal}"), ("RunAsAdmin", "{admin}")) }))),
        new ActionTemplate("open-folder-with", "文件", "Files", "用程序打开当前文件夹", "Open the current folder in a program",
            "把资源管理器当前打开的文件夹交给指定程序，例如用 VS Code 打开。", "Opens the folder shown in File Explorer in a chosen program, e.g. VS Code.", "\uE838",
            zh => P(new[] { Var("program", "code", Z(zh, "程序", "Program")) },
                S(StepCatalog.GetExplorerFolder, ("Output", "folder")),
                S(StepCatalog.If, ("Condition", "folder != \"\"")).With(
                    new[] { S(StepCatalog.RunProgram, ("Path", "{program}"), ("Arguments", "{folder:quote}")) },
                    new[] { S(StepCatalog.Notify, ("Text", Z(zh, "没有找到当前文件夹", "No folder is open")), ("Kind", "warn")) }))),

        // System
        new ActionTemplate("system-tool", "系统", "System", "打开系统设置或工具", "Open a Windows setting or tool",
            "带参数打开系统页面或工具，例如 ms-settings:display（显示）、ms-settings:bluetooth（蓝牙）、ncpa.cpl（网络连接）、devmgmt.msc（设备管理器）、taskmgr（任务管理器）、control（控制面板）、rundll32.exe 加参数 sysdm.cpl,EditEnvironmentVariables（环境变量）。",
            "Opens a Windows page or tool, e.g. ms-settings:display, ms-settings:bluetooth, ncpa.cpl (network connections), devmgmt.msc (Device Manager), taskmgr, control (Control Panel), or rundll32.exe with sysdm.cpl,EditEnvironmentVariables (environment variables).", "\uE713",
            zh => P(new[] { Var("path", "ms-settings:display", Z(zh, "页面或程序", "Page or program")),
                            Var("arguments", "", Z(zh, "参数，可留空", "Arguments (optional)")),
                            Var("admin", "false", Z(zh, "以管理员身份运行", "Run as administrator"), "bool") },
                S(StepCatalog.RunProgram, ("Path", "{path}"), ("Arguments", "{arguments}"), ("RunAsAdmin", "{admin}")))),
        new ActionTemplate("run-program", "系统", "System", "运行程序", "Run a program",
            "运行程序或打开文件、文件夹。", "Runs a program or opens a file or folder.", "",
            zh => P(new[] { Var("path", "notepad.exe", Z(zh, "程序、文件或文件夹路径", "Program, file or folder")),
                            Var("arguments", "", Z(zh, "启动参数，可留空", "Arguments (optional)")) },
                S(StepCatalog.RunProgram, ("Path", "{path}"), ("Arguments", "{arguments}")))),
        new ActionTemplate("launch-or-switch", "系统", "System", "启动或切换程序", "Launch or switch to an app",
            "程序没运行就启动，已运行就切换过去；已经在最前面时最小化。", "Launches the app, or switches to it if it is running; minimizes it when it is already in front.", "",
            zh => P(new[] { Var("path", "notepad.exe", Z(zh, "程序路径或名称", "Program path or name")),
                            Var("arguments", "", Z(zh, "启动参数，可留空", "Arguments (optional)")),
                            Var("processName", "", Z(zh, "窗口所属程序，可留空（用 .bat 或启动器启动时填真正的 exe）", "Program that owns the window (optional; the real exe when you start a .bat or launcher)")) },
                S(StepCatalog.LaunchOrActivate, ("Path", "{path}"), ("Arguments", "{arguments}"), ("ProcessName", "{processName}"), ("WhenActive", "minimize")))),
        new ActionTemplate("send-keys", "系统", "System", "发送快捷键", "Send a shortcut",
            "向当前窗口发送快捷键，可写多组，例如 Ctrl+A Ctrl+C。", "Sends shortcuts to the current window, e.g. Ctrl+A Ctrl+C.", "",
            zh => P(new[] { Var("keys", "Ctrl+Shift+T", Z(zh, "按键，多组用空格分隔", "Keys; separate several with spaces")) },
                S(StepCatalog.SendKeys, ("Keys", "{keys}")))),
        // Opens the optional component so a middle click or gesture can show it too.
        new ActionTemplate("open-quick-actions", "系统", "System", "打开环形菜单组件", "Open the radial menu component",
            "打开「快捷操作」里可选安装的环形菜单组件，让中键或手势也能唤起它。没装组件时提示先去下载。",
            "Opens the radial menu component installed from Quick Actions so a middle click or gesture can show it too. Warns when the component is missing.", "\uE945",
            zh => P(Array.Empty<StepVariableDef>(),
                S(StepCatalog.ShowQuickActions, ("Output", "status")),
                S(StepCatalog.If, ("Condition", "status == \"unavailable\"")).With(new[]
                {
                    S(StepCatalog.Notify, ("Text", Z(zh, "还没有安装环形菜单组件，请先在「快捷操作」里下载。", "The radial menu component is not installed. Download it in Quick Actions.")), ("Kind", "warn"))
                }))),
    };

    public static ActionTemplate Find(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
