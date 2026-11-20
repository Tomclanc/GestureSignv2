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
    Variable
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
    public const string GetSelectedText = "GetSelectedText";
    public const string GetClipboard = "GetClipboard";
    public const string GetClipboardFiles = "GetClipboardFiles";
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
    public const string Http = "Http";
    public const string RunJavaScript = "RunJavaScript";
    public const string RunLua = "RunLua";
    public const string RunCommand = "RunCommand";

    public static readonly IReadOnlyList<StepCategory> Categories = new[]
    {
        new StepCategory("flow", "流程控制", "Flow", ""),
        new StepCategory("interact", "交互", "Interaction", ""),
        new StepCategory("text", "文本处理", "Text", ""),
        new StepCategory("input", "剪贴板与按键", "Clipboard and keys", ""),
        new StepCategory("data", "变量与计算", "Variables and math", ""),
        new StepCategory("system", "系统与网络", "System and web", ""),
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
                    new StepParameter("Items", "选项（每行一项或列表变量）", "Choices (one per line or a list variable)", StepParameterKind.MultilineText, ""),
                    Output("choice") },
            descriptionZh: "取消时停止动作。", descriptionEn: "Cancelling stops the action."),

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
            new[] { new StepParameter("Path", "程序、文件或网址", "Program, file or URL", StepParameterKind.Text, ""),
                    new StepParameter("Arguments", "参数", "Arguments", StepParameterKind.Text, ""),
                    new StepParameter("Wait", "等待结束并读取输出", "Wait and read output", StepParameterKind.Boolean, "false"),
                    Output("output") }),
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
