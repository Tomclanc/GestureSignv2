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

public sealed record StepParameter(string Key, string NameZh, string NameEn, StepParameterKind Kind, string Default = null, string[] Choices = null, string HintZh = null, string HintEn = null);

public sealed record StepTypeInfo(string Type, string CategoryZh, string CategoryEn, string NameZh, string NameEn, StepParameter[] Parameters, bool HasBody = false, bool HasElse = false);

/// <summary>Every step type the engine runs and the editor offers, with its parameters.</summary>
public static class StepCatalog
{
    public const string If = "If";
    public const string Repeat = "Repeat";
    public const string ForEach = "ForEach";
    public const string Stop = "Stop";
    public const string Delay = "Delay";
    public const string SetVariable = "SetVariable";
    public const string Notify = "Notify";
    public const string GetSelectedText = "GetSelectedText";
    public const string GetClipboard = "GetClipboard";
    public const string SetClipboard = "SetClipboard";
    public const string PasteText = "PasteText";
    public const string TypeText = "TypeText";
    public const string SendKeys = "SendKeys";
    public const string RunProgram = "RunProgram";
    public const string Http = "Http";
    public const string RunJavaScript = "RunJavaScript";
    public const string RunLua = "RunLua";
    public const string RunCommand = "RunCommand";

    private static StepParameter Output(string defaultName) =>
        new("Output", "保存到变量", "Save to variable", StepParameterKind.Variable, defaultName);

    public static readonly IReadOnlyList<StepTypeInfo> Types = new[]
    {
        // Flow control
        new StepTypeInfo(If, "流程", "Flow", "如果", "If",
            new[] { new StepParameter("Condition", "条件（JavaScript 表达式）", "Condition (JavaScript expression)", StepParameterKind.Code, "text != \"\"",
                HintZh: "变量可直接使用，例如 text.length > 0", HintEn: "Variables are available by name, e.g. text.length > 0") },
            HasBody: true, HasElse: true),
        new StepTypeInfo(Repeat, "流程", "Flow", "重复", "Repeat",
            new[] { new StepParameter("Count", "次数", "Count", StepParameterKind.Text, "3"),
                    new StepParameter("Index", "序号变量", "Index variable", StepParameterKind.Variable, "index") },
            HasBody: true),
        new StepTypeInfo(ForEach, "流程", "Flow", "每个", "For each",
            new[] { new StepParameter("List", "列表（变量或每行一项的文字）", "List (variable or one item per line)", StepParameterKind.MultilineText, "{lines}"),
                    new StepParameter("Item", "当前项变量", "Item variable", StepParameterKind.Variable, "item") },
            HasBody: true),
        new StepTypeInfo(Stop, "流程", "Flow", "停止", "Stop", Array.Empty<StepParameter>()),
        new StepTypeInfo(Delay, "流程", "Flow", "等待", "Wait",
            new[] { new StepParameter("Milliseconds", "毫秒", "Milliseconds", StepParameterKind.Number, "200") }),

        // Variables and feedback
        new StepTypeInfo(SetVariable, "变量", "Variables", "设置变量", "Set variable",
            new[] { new StepParameter("Name", "变量名", "Variable", StepParameterKind.Variable, "text"),
                    new StepParameter("Value", "值", "Value", StepParameterKind.MultilineText, "") }),
        new StepTypeInfo(Notify, "变量", "Variables", "显示提示", "Show message",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}") }),

        // Text and clipboard
        new StepTypeInfo(GetSelectedText, "文字", "Text", "获取选中的文字", "Get selected text",
            new[] { Output("text") }),
        new StepTypeInfo(GetClipboard, "文字", "Text", "读取剪贴板", "Read clipboard",
            new[] { Output("clipboard") }),
        new StepTypeInfo(SetClipboard, "文字", "Text", "写入剪贴板", "Write clipboard",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}") }),
        new StepTypeInfo(PasteText, "文字", "Text", "粘贴文字", "Paste text",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "{text}",
                HintZh: "替换当前选中的文字，剪贴板会恢复原样", HintEn: "Replaces the selection; the clipboard is restored") }),
        new StepTypeInfo(TypeText, "文字", "Text", "输入文字", "Type text",
            new[] { new StepParameter("Text", "内容", "Text", StepParameterKind.MultilineText, "") }),
        new StepTypeInfo(SendKeys, "文字", "Text", "发送按键", "Send keys",
            new[] { new StepParameter("Keys", "按键", "Keys", StepParameterKind.Text, "Ctrl+C",
                HintZh: "例如 Ctrl+C、Alt+Left、Win+D；多组用空格分隔", HintEn: "For example Ctrl+C, Alt+Left, Win+D; separate several with spaces") }),

        // System
        new StepTypeInfo(RunProgram, "系统", "System", "运行程序或打开网址", "Run program or open URL",
            new[] { new StepParameter("Path", "程序、文件或网址", "Program, file or URL", StepParameterKind.Text, ""),
                    new StepParameter("Arguments", "参数", "Arguments", StepParameterKind.Text, ""),
                    new StepParameter("Wait", "等待结束并读取输出", "Wait and read output", StepParameterKind.Boolean, "false"),
                    Output("output") }),
        new StepTypeInfo(Http, "系统", "System", "网络请求", "HTTP request",
            new[] { new StepParameter("Method", "方法", "Method", StepParameterKind.Choice, "GET", new[] { "GET", "POST", "PUT", "DELETE" }),
                    new StepParameter("Url", "网址", "URL", StepParameterKind.Text, "https://"),
                    new StepParameter("Body", "请求内容", "Body", StepParameterKind.MultilineText, ""),
                    new StepParameter("ContentType", "内容类型", "Content type", StepParameterKind.Text, "application/json"),
                    Output("response") }),
        new StepTypeInfo(RunCommand, "系统", "System", "运行 GestureSign 命令", "Run GestureSign command",
            new[] { new StepParameter("PluginClass", "插件类名", "Plugin class", StepParameterKind.Text, "GestureSign.CorePlugins.HotKey.HotKeyPlugin"),
                    new StepParameter("Settings", "命令设置 JSON", "Command settings JSON", StepParameterKind.Code, "") }),

        // Scripts
        new StepTypeInfo(RunJavaScript, "脚本", "Script", "运行 JavaScript", "Run JavaScript",
            new[] { new StepParameter("Code", "代码", "Code", StepParameterKind.Code, "return text.toUpperCase();",
                HintZh: "变量可直接读取；用 setVar(\"名称\", 值) 设置变量；return 的值保存到输出变量",
                HintEn: "Read variables by name; setVar(\"name\", value) sets one; the returned value goes to the output variable"),
                    Output("result") }),
        new StepTypeInfo(RunLua, "脚本", "Script", "运行 Lua", "Run Lua",
            new[] { new StepParameter("Code", "代码", "Code", StepParameterKind.Code, "return string.upper(text)",
                HintZh: "变量可直接读取；用 setVar(\"名称\", 值) 设置变量；return 的值保存到输出变量",
                HintEn: "Read variables by name; setVar(\"name\", value) sets one; the returned value goes to the output variable"),
                    Output("result") }),
    };

    public static StepTypeInfo Find(string type)
        => Types.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));
}
