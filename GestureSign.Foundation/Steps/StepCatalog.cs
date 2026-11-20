#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Foundation.Steps;

public enum StepParameterKind
{
    Text,
    MultilineText,
    Code,
    Number,
    Boolean,
    Choice,
    /// <summary>A variable name (output or loop variable), without braces.</summary>
    Variable,
    /// <summary>A program or file path; the editor offers a Browse button.</summary>
    FilePath,
    /// <summary>A secret such as an API key: masked in the editor and stored encrypted (see SecretStore).</summary>
    Secret
}

/// <summary>
/// One parameter of a step type. For <see cref="StepParameterKind.Choice"/>, <see cref="Choices"/>
/// are the stored values and <see cref="ChoiceNamesZh"/>/<see cref="ChoiceNamesEn"/> their labels.
/// </summary>
public sealed record StepParameter(string Key, string NameZh, string NameEn, StepParameterKind Kind, string Default = null, string[] Choices = null, string HintZh = null, string HintEn = null, string[] ChoiceNamesZh = null, string[] ChoiceNamesEn = null);

/// <summary>A step category in the editor's palette.</summary>
public sealed record StepCategory(string Id, string NameZh, string NameEn, string Glyph);

public sealed record StepTypeInfo(string Type, string CategoryZh, string CategoryEn, string NameZh, string NameEn, StepParameter[] Parameters, bool HasBody = false, bool HasElse = false, string Glyph = "", string Category = null, string DescriptionZh = null, string DescriptionEn = null);

/// <summary>Every step type the engine runs and the editor offers, with its parameters.</summary>
public static class StepCatalog
{
    public const string If = "If";
    public const string Repeat = "Repeat";
    public const string ForEach = "ForEach";
    public const string Group = "Group";
    public const string Comment = "Comment";
    public const string Stop = "Stop";
    public const string Delay = "Delay";
    public const string SetVariable = "SetVariable";
    public const string Compute = "Compute";
    public const string Notify = "Notify";
    public const string MessageBox = "MessageBox";
    public const string UserInput = "UserInput";
    public const string UserSelect = "UserSelect";
    public const string UserForm = "UserForm";
    public const string ChooseFile = "ChooseFile";
    public const string GetSelectedText = "GetSelectedText";
    public const string GetClipboard = "GetClipboard";
    public const string GetClipboardFiles = "GetClipboardFiles";
    public const string GetSelectedFiles = "GetSelectedFiles";
    public const string GetExplorerFolder = "GetExplorerFolder";
    public const string SetClipboard = "SetClipboard";
    public const string PasteText = "PasteText";
    public const string TypeText = "TypeText";
    public const string SendKeys = "SendKeys";
    public const string ReplaceText = "ReplaceText";
    public const string RegexExtract = "RegexExtract";
    public const string ConvertText = "ConvertText";
    public const string SplitText = "SplitText";
    public const string JoinList = "JoinList";
    public const string GetDateTime = "GetDateTime";
    public const string OpenUrl = "OpenUrl";
    public const string RunProgram = "RunProgram";
    public const string LaunchOrActivate = "LaunchOrActivate";
    public const string Http = "Http";
    public const string RunJavaScript = "RunJavaScript";
    public const string RunLua = "RunLua";
    public const string RunCommand = "RunCommand";
    public const string AiChat = "AiChat";
    public const string ShowText = "ShowText";

    public static readonly IReadOnlyList<StepCategory> Categories = new[]
    {
        new StepCategory("flow", "流程控制", "Flow", ""),
        new StepCategory("interact", "交互", "Interaction", ""),
        new StepCategory("text", "文本处理", "Text", ""),
        new StepCategory("input", "剪贴板与按键", "Clipboard and keys", ""),
        new StepCategory("data", "变量与计算", "Variables and math", ""),
        new StepCategory("system", "系统与网络", "System and web", ""),
        new StepCategory("ai", "AI", "AI", "\uE82F"),
        new StepCategory("script", "脚本", "Scripts", ""),
    };

    private static StepParameter Output(string defaultName) =>
        new("Output", "保存到变量", "Save to variable", StepParameterKind.Variable, defaultName);

    private static StepParameter Input(string defaultValue = "{text}") =>
        new("Input", "输入文本", "Input text", StepParameterKind.MultilineText, defaultValue);

    private static StepTypeInfo Type(string category, string type, string nameZh, string nameEn, string glyph, StepParameter[] parameters,
        bool body = false, bool @else = false, string descriptionZh = null, string descriptionEn = null)
    {
        var info = Categories.First(c => c.Id == category);
        return new StepTypeInfo(type, info.NameZh, info.NameEn, nameZh, nameEn, parameters, body, @else, glyph, category, descriptionZh, descriptionEn);
    }

    private const string ScriptHintZh = "变量可直接读取；用 setVar(\"名称\", 值) 设置变量；return 的值保存到输出变量";
    private const string ScriptHintEn = "Read variables by name; setVar(\"name\", value) sets one; the returned value goes to the output variable";

    public static readonly IReadOnlyList<StepTypeInfo> Types = new[]
    {
        // Flow control
        Type("flow", Group, "步骤组", "Step group", "",
            new[] { new StepParameter("Title", "标题", "Title", StepParameterKind.Text, "") },
            body: true, descriptionZh: "把几个步骤归为一组，便于折叠和整体停用。", descriptionEn: "Groups steps so they can be collapsed or disabled together."),
        Type("flow", If, "如果/否则", "If / else", "",
            new[] { new StepParameter("Condition", "条件（JavaScript 表达式）", "Condition (JavaScript expression)", StepParameterKind.Code, "text != \"\"",
                HintZh: "变量可直接使用，例如 text.length > 0", HintEn: "Variables are available by name, e.g. text.length > 0") },
            body: true, @else: true),
        Type("flow", Repeat, "重复", "Repeat", "",
            new[] { new StepParameter("Count", "次数", "Count", StepParameterKind.Text, "3"),
                    new StepParameter("Index", "序号变量", "Index variable", StepParameterKind.Variable, "index") },
            body: true),
        Type("flow", ForEach, "循环每一项", "For each", "",
            new[] { new StepParameter("List", "列表（变量或每行一项的文字）", "List (variable or one item per line)", StepParameterKind.MultilineText, "{lines}"),
                    new StepParameter("Item", "当前项变量", "Item variable", StepParameterKind.Variable, "item") },
            body: true),
        Type("flow", Delay, "等待时间", "Wait", "",
            new[] { new StepParameter("Milliseconds", "毫秒", "Milliseconds", StepParameterKind.Number, "200") }),
        Type("flow", Stop, "停止", "Stop", "", Array.Empty<StepParameter>()),
        Type("flow", Comment, "注释", "Comment", "",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "") }),

        // Interaction
        Type("interact", Notify, "提示消息", "Show message", "",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}") },
            descriptionZh: "在屏幕右下角短暂显示一条消息。", descriptionEn: "Briefly shows a message at the bottom right of the screen."),
        Type("interact", MessageBox, "弹窗提示或确认", "Message box", "",
            new[] { new StepParameter("Title", "标题", "Title", StepParameterKind.Text, "GestureSign"),
                    new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, ""),
                    new StepParameter("Buttons", "按钮", "Buttons", StepParameterKind.Choice, "OK", new[] { "OK", "OKCancel", "YesNo" },
                        ChoiceNamesZh: new[] { "确定", "确定 / 取消", "是 / 否" }, ChoiceNamesEn: new[] { "OK", "OK / Cancel", "Yes / No" }),
                    Output("confirmed") }),
        Type("interact", UserInput, "用户输入", "Ask for text", "",
            new[] { new StepParameter("Prompt", "提示", "Prompt", StepParameterKind.Text, ""),
                    new StepParameter("Default", "默认值", "Default value", StepParameterKind.MultilineText, ""),
                    new StepParameter("Multiline", "多行", "Multiple lines", StepParameterKind.Boolean, "false"),
                    Output("input") },
            descriptionZh: "取消时停止动作。", descriptionEn: "Cancelling stops the action."),
        Type("interact", UserSelect, "用户选择", "Ask to choose", "",
            new[] { new StepParameter("Prompt", "提示", "Prompt", StepParameterKind.Text, ""),
                    new StepParameter("Items", "选项（每行一项或列表变量）", "Choices (one per line or a list variable)", StepParameterKind.MultilineText, "",
                        HintZh: "写成「显示名|值」时，列表里显示前面的名字，结果是后面的值。例如：百度|https://www.baidu.com/s?wd=",
                        HintEn: "Write \"label|value\" to show the label and get the value, e.g. Bing|https://www.bing.com/search?q="),
                    new StepParameter("Multiple", "允许多选（结果是列表）", "Allow several (the result is a list)", StepParameterKind.Boolean, "false"),
                    Output("choice") },
            descriptionZh: "从列表中选一项或多项。取消时停止动作。", descriptionEn: "Pick one or more items from a list. Cancelling stops the action."),
        Type("interact", UserForm, "表单（一次填多项）", "Form (several fields)", "\uE70F",
            new[] { new StepParameter("Title", "标题", "Title", StepParameterKind.Text, ""),
                    new StepParameter("Fields", "字段（每行一个）", "Fields (one per line)", StepParameterKind.MultilineText,
                        "name|名称|text|\ncategory|分类|choice|工作|工作,生活,学习\nnote|备注|multiline|\npinned|置顶|check|false",
                        HintZh: "每行：变量名|标签|类型|默认值|选项。类型：text 文本、multiline 多行、number 数字、choice 下拉（选项用逗号分隔）、check 勾选、file 文件、folder 文件夹。每个字段的结果存到同名变量。",
                        HintEn: "Each line: variable|label|kind|default|options. Kinds: text, multiline, number, choice (comma-separated options), check, file, folder. Each field is saved to the variable of the same name.") },
            descriptionZh: "在一个窗口里同时填写多项。取消时停止动作。", descriptionEn: "Fill in several fields in one window. Cancelling stops the action."),
        Type("interact", ChooseFile, "选择文件或文件夹", "Choose a file or folder", "\uE8E5",
            new[] { new StepParameter("Mode", "方式", "Mode", StepParameterKind.Choice, "open",
                        new[] { "open", "openMultiple", "save", "folder" },
                        ChoiceNamesZh: new[] { "打开一个文件", "打开多个文件（结果是列表）", "保存文件", "选择文件夹" },
                        ChoiceNamesEn: new[] { "Open a file", "Open several files (a list)", "Save a file", "Choose a folder" }),
                    new StepParameter("Title", "标题", "Title", StepParameterKind.Text, ""),
                    new StepParameter("Filter", "文件类型（可留空）", "File types (optional)", StepParameterKind.Text, "",
                        HintZh: "例如：图片|*.png;*.jpg;*.jpeg|所有文件|*.*", HintEn: "For example: Images|*.png;*.jpg;*.jpeg|All files|*.*"),
                    new StepParameter("InitialPath", "起始文件夹或默认文件名（可留空）", "Start folder or default file name (optional)", StepParameterKind.Text, ""),
                    Output("path") },
            descriptionZh: "弹出系统的打开、保存或选择文件夹对话框。取消时停止动作。", descriptionEn: "Shows the Windows open, save or folder dialog. Cancelling stops the action."),

        // Text
        Type("text", ReplaceText, "替换文本", "Replace text", "",
            new[] { Input(),
                    new StepParameter("Find", "查找", "Find", StepParameterKind.Text, ""),
                    new StepParameter("Replace", "替换为", "Replace with", StepParameterKind.Text, ""),
                    new StepParameter("Regex", "正则表达式", "Regular expression", StepParameterKind.Boolean, "false"),
                    new StepParameter("IgnoreCase", "忽略大小写", "Ignore case", StepParameterKind.Boolean, "false"),
                    Output("text") }),
        Type("text", RegexExtract, "正则提取", "Regex extract", "",
            new[] { Input(),
                    new StepParameter("Pattern", "正则表达式", "Regular expression", StepParameterKind.Text, ""),
                    new StepParameter("All", "提取全部匹配（列表）", "All matches (list)", StepParameterKind.Boolean, "false"),
                    Output("match") },
            descriptionZh: "有分组时取第 1 个分组。", descriptionEn: "Uses the first group when the pattern has groups."),
        Type("text", ConvertText, "文本转换", "Convert text", "",
            new[] { Input(),
                    new StepParameter("Mode", "转换方式", "Conversion", StepParameterKind.Choice, "upper",
                        new[] { "upper", "lower", "title", "trim", "toSimplified", "toTraditional", "urlEncode", "urlDecode", "base64Encode", "base64Decode" },
                        ChoiceNamesZh: new[] { "转大写", "转小写", "首字母大写", "去除首尾空白", "繁体转简体", "简体转繁体", "URL 编码", "URL 解码", "Base64 编码", "Base64 解码" },
                        ChoiceNamesEn: new[] { "Upper case", "Lower case", "Title case", "Trim", "Traditional to Simplified Chinese", "Simplified to Traditional Chinese", "URL encode", "URL decode", "Base64 encode", "Base64 decode" }),
                    Output("text") }),
        Type("text", SplitText, "拆分文本为列表", "Split text into list", "",
            new[] { Input(),
                    new StepParameter("Separator", "分隔符（留空按行）", "Separator (empty: lines)", StepParameterKind.Text, ""),
                    Output("items") }),
        Type("text", JoinList, "列表合并成文本", "Join list into text", "",
            new[] { new StepParameter("List", "列表", "List", StepParameterKind.Text, "{items}"),
                    new StepParameter("Separator", "分隔符（留空用换行）", "Separator (empty: new line)", StepParameterKind.Text, ""),
                    Output("text") }),

        // Clipboard and keys
        Type("input", GetSelectedText, "获取选中的文本", "Get selected text", "",
            new[] { Output("text") }),
        Type("input", GetSelectedFiles, "获取选中的文件", "Get selected files", "\uE8E5",
            new[] { Output("files") },
            descriptionZh: "资源管理器或桌面中选中的文件和文件夹的完整路径（列表）。其他文件管理器通过 Ctrl+C 读取。用 {files:quote} 可以把每个路径加上引号并用空格连接，直接作为程序参数。",
            descriptionEn: "Full paths of the files and folders selected in File Explorer or on the desktop, as a list. Other file managers are read with Ctrl+C. {files:quote} quotes each path and joins them with spaces, ready to use as program arguments."),
        Type("input", GetExplorerFolder, "获取当前文件夹", "Get current folder", "\uE838",
            new[] { Output("folder") },
            descriptionZh: "资源管理器当前打开的文件夹；在桌面上为桌面文件夹。", descriptionEn: "The folder open in File Explorer; the desktop folder on the desktop."),
        Type("input", GetClipboard, "获取剪贴板文本", "Get clipboard text", "",
            new[] { Output("clipboard") }),
        Type("input", GetClipboardFiles, "获取剪贴板文件列表", "Get clipboard files", "",
            new[] { Output("files") }),
        Type("input", SetClipboard, "写入剪贴板", "Write clipboard", "",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}") }),
        Type("input", PasteText, "粘贴文本", "Paste text", "",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}",
                HintZh: "替换当前选中的文字，剪贴板会恢复原样", HintEn: "Replaces the selection; the clipboard is restored") }),
        Type("input", TypeText, "输入文本", "Type text", "",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "") }),
        Type("input", SendKeys, "模拟按键", "Send keys", "",
            new[] { new StepParameter("Keys", "按键", "Keys", StepParameterKind.Text, "Ctrl+C",
                HintZh: "例如 Ctrl+C、Alt+Left、Win+D；多组用空格分隔", HintEn: "For example Ctrl+C, Alt+Left, Win+D; separate several with spaces") }),

        // Variables and math
        Type("data", SetVariable, "赋值", "Set variable", "",
            new[] { new StepParameter("Name", "变量名", "Variable", StepParameterKind.Variable, "text"),
                    new StepParameter("Value", "值", "Value", StepParameterKind.MultilineText, "") }),
        Type("data", Compute, "计算", "Calculate", "",
            new[] { new StepParameter("Expression", "表达式（JavaScript）", "Expression (JavaScript)", StepParameterKind.Code, "1 + 1",
                HintZh: "例如 count * 2、text.length、Math.max(a, b)", HintEn: "For example count * 2, text.length, Math.max(a, b)"),
                    Output("result") }),
        Type("data", GetDateTime, "获取日期时间", "Get date and time", "",
            new[] { new StepParameter("Format", "格式", "Format", StepParameterKind.Text, "yyyy-MM-dd HH:mm:ss",
                HintZh: "例如 yyyy-MM-dd、HH:mm、yyyyMMddHHmmss", HintEn: "For example yyyy-MM-dd, HH:mm, yyyyMMddHHmmss"),
                    Output("now") }),

        // System and web
        Type("system", OpenUrl, "打开网址", "Open URL", "",
            new[] { new StepParameter("Url", "网址", "URL", StepParameterKind.Text, "https://") }),
        Type("system", RunProgram, "运行或打开", "Run or open", "",
            new[] { new StepParameter("Path", "程序、文件或网址", "Program, file or URL", StepParameterKind.FilePath, ""),
                    new StepParameter("Arguments", "参数", "Arguments", StepParameterKind.Text, "",
                        HintZh: "可以用变量，例如 {files:quote}、\"{folder}\"", HintEn: "Variables work, e.g. {files:quote} or \"{folder}\""),
                    new StepParameter("WorkingDirectory", "起始位置（可留空）", "Start in (optional)", StepParameterKind.Text, ""),
                    new StepParameter("RunAsAdmin", "以管理员身份运行", "Run as administrator", StepParameterKind.Boolean, "false"),
                    new StepParameter("Wait", "等待结束并读取输出", "Wait and read output", StepParameterKind.Boolean, "false"),
                    Output("output") }),
        Type("system", LaunchOrActivate, "启动或切换程序", "Launch or switch to app", "\uE8A7",
            new[] { new StepParameter("Path", "程序路径或名称", "Program path or name", StepParameterKind.FilePath, "notepad.exe",
                        HintZh: "例如 C:\\Program Files\\App\\app.exe，或 PATH 里的 notepad.exe", HintEn: "For example C:\\Program Files\\App\\app.exe, or notepad.exe from PATH"),
                    new StepParameter("Arguments", "启动参数（只在启动时使用）", "Arguments (used only when launching)", StepParameterKind.Text, ""),
                    new StepParameter("ProcessName", "窗口所属程序（可留空）", "Program that owns the window (optional)", StepParameterKind.FilePath, "",
                        HintZh: "用 .bat、快捷方式或启动器启动，而窗口属于另一个程序时填这里，例如 微信小店助手.exe 或它的完整路径。留空时按上面的路径找窗口。",
                        HintEn: "Fill this in when you start a .bat file, shortcut or launcher but the window belongs to another program, e.g. App.exe or its full path. When empty, the path above is used."),
                    new StepParameter("HideLaunchWindow", "启动时隐藏命令行窗口（.bat、.cmd）", "Hide the console window when launching (.bat, .cmd)", StepParameterKind.Boolean, "false"),
                    new StepParameter("TitleFilter", "窗口标题包含（可留空）", "Window title contains (optional)", StepParameterKind.Text, "",
                        HintZh: "同一程序有多种窗口时，用来只匹配其中一种。", HintEn: "Use it to match only one kind of window when a program has several."),
                    new StepParameter("WhenActive", "已经在最前面时", "When it is already in front", StepParameterKind.Choice, "minimize",
                        new[] { "minimize", "cycle", "none" },
                        ChoiceNamesZh: new[] { "最小化", "切换到它的下一个窗口", "不操作" },
                        ChoiceNamesEn: new[] { "Minimize it", "Switch to its next window", "Do nothing" }),
                    Output("status") },
            descriptionZh: "程序没运行就启动；已经运行就切换到它的窗口（最小化的会还原）。status 为 launched、activated、minimized 或 none。",
            descriptionEn: "Launches the program if it is not running; otherwise switches to its window and restores it if minimized. status is launched, activated, minimized or none."),
        Type("system", Http, "HTTP 请求", "HTTP request", "",
            new[] { new StepParameter("Method", "方法", "Method", StepParameterKind.Choice, "GET", new[] { "GET", "POST", "PUT", "DELETE" }),
                    new StepParameter("Url", "网址", "URL", StepParameterKind.Text, "https://"),
                    new StepParameter("Body", "请求内容", "Body", StepParameterKind.MultilineText, ""),
                    new StepParameter("ContentType", "内容类型", "Content type", StepParameterKind.Text, "application/json"),
                    Output("response") }),
        Type("system", RunCommand, "执行命令", "Run command", "",
            new[] { new StepParameter("PluginClass", "插件类名", "Plugin class", StepParameterKind.Text, "GestureSign.CorePlugins.HotKey.HotKeyPlugin"),
                    new StepParameter("Settings", "命令设置 JSON", "Command settings JSON", StepParameterKind.Code, "") },
            descriptionZh: "快捷键、音量、打开文件、启动应用等 GestureSign 命令。", descriptionEn: "Hotkey, volume, open file, launch app and other GestureSign commands."),

        // AI
        Type("ai", AiChat, "AI 处理", "Ask AI", "\uE82F",
            new[] { new StepParameter("Prompt", "内容（发给 AI 的文字）", "Prompt (text sent to the AI)", StepParameterKind.MultilineText, "{text}"),
                    new StepParameter("SystemPrompt", "要求（系统提示词）", "Instructions (system prompt)", StepParameterKind.MultilineText, "",
                        HintZh: "例如：把用户的文字翻译成英文，只输出译文。", HintEn: "For example: Translate the user's text into English. Output only the translation."),
                    new StepParameter("Model", "模型（留空用 AI 设置）", "Model (empty: AI settings)", StepParameterKind.Text, ""),
                    new StepParameter("Endpoint", "接口地址（留空用 AI 设置）", "API address (empty: AI settings)", StepParameterKind.Text, ""),
                    new StepParameter("ApiKey", "API Key（留空用 AI 设置）", "API key (empty: AI settings)", StepParameterKind.Secret, ""),
                    new StepParameter("Temperature", "随机性 0~2（可留空）", "Temperature 0-2 (optional)", StepParameterKind.Text, ""),
                    Output("result") },
            descriptionZh: "调用兼容 OpenAI 的接口（OpenAI、DeepSeek、通义千问、Ollama 等），把回复保存到变量。",
            descriptionEn: "Calls an OpenAI-compatible API (OpenAI, DeepSeek, Qwen, Ollama and others) and saves the reply to a variable."),
        Type("interact", ShowText, "显示文本窗口", "Show text window", "\uE8A5",
            new[] { new StepParameter("Title", "标题", "Title", StepParameterKind.Text, "GestureSign"),
                    new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{result}") },
            descriptionZh: "在可复制的窗口中显示较长的文字。", descriptionEn: "Shows longer text in a window you can copy from."),

        // Scripts
        Type("script", RunJavaScript, "运行 JavaScript", "Run JavaScript", "",
            new[] { new StepParameter("Code", "代码", "Code", StepParameterKind.Code, "return text.toUpperCase();", HintZh: ScriptHintZh, HintEn: ScriptHintEn),
                    Output("result") }),
        Type("script", RunLua, "运行 Lua", "Run Lua", "",
            new[] { new StepParameter("Code", "代码", "Code", StepParameterKind.Code, "return string.upper(text)", HintZh: ScriptHintZh, HintEn: ScriptHintEn),
                    Output("result") }),
    };

    public static StepTypeInfo Find(string type)
        => Types.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));
}
