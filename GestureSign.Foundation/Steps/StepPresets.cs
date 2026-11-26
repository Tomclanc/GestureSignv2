#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Foundation.Steps;

/// <summary>A ready-made value for a step: picking it fills Path and, when given, Arguments and ProcessName.</summary>
public sealed record StepPreset(string GroupZh, string GroupEn, string NameZh, string NameEn, string Path, string Arguments = "", string ProcessName = "");

/// <summary>A variable format such as {name:quote}, shown in the editor's insert-variable menu.</summary>
public sealed record VariableFormat(string Suffix, string DescriptionZh, string DescriptionEn);

/// <summary>Common programs, Windows pages and launch arguments offered next to the Path parameter.</summary>
public static class StepPresets
{
    private const string SettingsZh = "系统设置", SettingsEn = "Windows settings";
    private const string ToolsZh = "系统工具", ToolsEn = "Windows tools";
    private const string ArgumentsZh = "带参数启动", ArgumentsEn = "With arguments";
    private const string AppsZh = "常用程序", AppsEn = "Programs";

    private static readonly StepPreset[] Settings =
    {
        new(SettingsZh, SettingsEn, "显示", "Display", "ms-settings:display"),
        new(SettingsZh, SettingsEn, "声音", "Sound", "ms-settings:sound"),
        new(SettingsZh, SettingsEn, "各应用音量", "App volume", "ms-settings:apps-volume"),
        new(SettingsZh, SettingsEn, "蓝牙和设备", "Bluetooth & devices", "ms-settings:bluetooth"),
        new(SettingsZh, SettingsEn, "WLAN", "Wi-Fi", "ms-settings:network-wifi"),
        new(SettingsZh, SettingsEn, "电源和睡眠", "Power & sleep", "ms-settings:powersleep"),
        new(SettingsZh, SettingsEn, "默认应用", "Default apps", "ms-settings:defaultapps"),
        new(SettingsZh, SettingsEn, "Windows 更新", "Windows Update", "ms-settings:windowsupdate"),
    };

    private static readonly StepPreset[] Tools =
    {
        new(ToolsZh, ToolsEn, "任务管理器", "Task Manager", "taskmgr"),
        new(ToolsZh, ToolsEn, "设备管理器", "Device Manager", "devmgmt.msc"),
        new(ToolsZh, ToolsEn, "磁盘管理", "Disk Management", "diskmgmt.msc"),
        new(ToolsZh, ToolsEn, "服务", "Services", "services.msc"),
        new(ToolsZh, ToolsEn, "注册表编辑器", "Registry Editor", "regedit"),
        new(ToolsZh, ToolsEn, "控制面板", "Control Panel", "control"),
        new(ToolsZh, ToolsEn, "程序和功能", "Programs and Features", "appwiz.cpl"),
        new(ToolsZh, ToolsEn, "网络连接", "Network Connections", "ncpa.cpl"),
        new(ToolsZh, ToolsEn, "环境变量", "Environment Variables", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
        new(ToolsZh, ToolsEn, "启动文件夹", "Startup folder", "shell:startup"),
        new(ToolsZh, ToolsEn, "锁屏", "Lock the PC", "rundll32.exe", "user32.dll,LockWorkStation"),
    };

    private static readonly StepPreset[] WithArguments =
    {
        new(ArgumentsZh, ArgumentsEn, "Edge 无痕窗口", "Edge InPrivate window", "msedge.exe", "--inprivate"),
        new(ArgumentsZh, ArgumentsEn, "Chrome 无痕窗口", "Chrome incognito window", "chrome.exe", "--incognito"),
        new(ArgumentsZh, ArgumentsEn, "在资源管理器中定位选中的文件", "Show the selected file in File Explorer", "explorer.exe", "/select,{files:quote}"),
        new(ArgumentsZh, ArgumentsEn, "在当前文件夹打开终端", "Terminal in the current folder", "wt.exe", "-d {folder:quote}"),
        new(ArgumentsZh, ArgumentsEn, "用 VS Code 打开当前文件夹", "Open the current folder in VS Code", "code", "{folder:quote}"),
        new(ArgumentsZh, ArgumentsEn, "读取命令输出（ipconfig）", "Read command output (ipconfig)", "cmd.exe", "/c chcp 65001>nul & ipconfig"),
        new(ArgumentsZh, ArgumentsEn, "远程桌面连接", "Remote Desktop", "mstsc.exe", "/v:hostname"),
    };

    // For "Launch or switch": the program whose window is matched, when it differs from the one launched.
    private static readonly StepPreset[] Apps =
    {
        new(AppsZh, AppsEn, "记事本", "Notepad", "notepad.exe"),
        new(AppsZh, AppsEn, "Microsoft Edge", "Microsoft Edge", "msedge.exe"),
        new(AppsZh, AppsEn, "Google Chrome", "Google Chrome", "chrome.exe"),
        new(AppsZh, AppsEn, "VS Code", "VS Code", "code", ProcessName: "Code.exe"),
        new(AppsZh, AppsEn, "Windows 终端", "Windows Terminal", "wt.exe", ProcessName: "WindowsTerminal.exe"),
    };

    public static IReadOnlyList<VariableFormat> VariableFormats { get; } = new[]
    {
        new VariableFormat("quote", "加引号（路径、程序参数；列表会逐项加引号并用空格连接）", "Quoted (paths, program arguments; list items are quoted and joined with spaces)"),
        new VariableFormat("url", "网址编码（拼到网址后面）", "URL-encoded (to append to a link)"),
        new VariableFormat("json", "JSON 转义（放进 JSON 字符串）", "JSON-escaped (inside a JSON string)"),
    };

    /// <summary>Presets offered for a step type, in menu order; empty when it has none.</summary>
    public static IReadOnlyList<StepPreset> ForStep(string stepType) => stepType switch
    {
        StepCatalog.RunProgram => Apps.Concat(Settings).Concat(Tools).Concat(WithArguments).ToArray(),
        StepCatalog.LaunchOrActivate => Apps,
        _ => Array.Empty<StepPreset>()
    };
}
