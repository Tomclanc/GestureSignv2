using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GestureSign.WinUI;

/// <summary>
/// Sharing library actions: export to a .gsaction file or a share code, and
/// import with a review of what the actions can do and their settings.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ExportActionToFileAsync(RadialMenuAction action)
    {
        EnsureRadialConfig();
        var package = ActionPackage.Create(_radialConfig!, new[] { action });
        var safeName = string.Concat((action.Name ?? "action").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = await PickSaveFileAsync(safeName + ActionPackage.FileExtension, ActionPackage.FileExtension);
        if (string.IsNullOrWhiteSpace(path))
            return;
        await File.WriteAllTextAsync(path, package.ToJson());
    }

    private async Task CopyActionShareCodeAsync(RadialMenuAction action)
    {
        EnsureRadialConfig();
        var package = ActionPackage.Create(_radialConfig!, new[] { action });
        var data = new DataPackage();
        data.SetText(package.ToShareCode());
        Clipboard.SetContent(data);
        await ShowInfoDialog(T("复制分享代码", "Copy share code"),
            T("把剪贴板中的分享代码发给别人，对方在动作库中点「导入」即可。API 密钥和本机文件路径的图标不会被分享。",
              "Send the share code on the clipboard to someone; they choose Import on the Action library page. API keys and icons that are local file paths are not shared."));
    }

    private async Task ImportActionsFromFileAsync()
    {
        var path = await PickOpenFileAsync(new[] { ActionPackage.FileExtension });
        if (string.IsNullOrWhiteSpace(path))
            return;
        await ImportActionPackageTextAsync(await File.ReadAllTextAsync(path));
    }

    private async Task ImportActionsFromShareCodeAsync()
    {
        var current = "";
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (ActionPackage.LooksLikeShareCode(text))
                    current = text.Trim();
            }
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
        var box = new TextBox
        {
            Text = current,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas")
        };
        if (!await ConfirmDialogAsync(T("导入分享代码…", "From a share code…").TrimEnd('…'), box, T("导入", "Import")))
            return;
        await ImportActionPackageTextAsync(box.Text);
    }

    private async Task ImportActionPackageTextAsync(string text)
    {
        ActionPackage package;
        try
        {
            package = ActionPackage.Parse(text);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException)
        {
            await ShowInfoDialog(T("导入动作", "Import actions"), T("这不是有效的 GestureSign 动作文件或分享代码。", "This is not a valid GestureSign action file or share code."));
            return;
        }
        if (!await ReviewActionPackageAsync(package))
            return;
        EnsureRadialConfig();
        var added = package.ImportInto(_radialConfig!);
        SaveRadialConfig();
        RefreshActionLibraryViews();
        await ShowInfoDialog(T("导入动作", "Import actions"), F("已导入 {0} 个动作：{1}", "Imported {0} actions: {1}", added.Count, string.Join(", ", added.Select(a => a.Name))));
    }

    private string RiskText(ActionRiskKind kind) => kind switch
    {
        ActionRiskKind.RunProgram => T("运行程序或命令", "Runs a program or command"),
        ActionRiskKind.Script => T("运行脚本", "Runs a script"),
        ActionRiskKind.Network => T("发送网络请求", "Sends a network request"),
        ActionRiskKind.Ai => T("把文字发送给 AI 服务", "Sends text to an AI service"),
        _ => T("使用第三方插件", "Uses a third-party plugin")
    };

    /// <summary>
    /// Shows what the package contains, what its actions can do, and lets the
    /// user adjust their settings (declared variables) before importing.
    /// </summary>
    private async Task<bool> ReviewActionPackageAsync(ActionPackage package)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock
        {
            Text = F("将导入 {0} 个动作：{1}", "{0} actions will be imported: {1}", package.Actions.Count, string.Join(", ", package.Actions.Select(a => a.Name))),
            TextWrapping = TextWrapping.Wrap
        });

        var risks = ActionPackage.Review(package.Actions);
        if (risks.Count > 0)
        {
            var warning = new StackPanel { Spacing = 4 };
            warning.Children.Add(new TextBlock
            {
                Text = T("⚠ 安全提示：这些动作会执行以下操作。只导入你信任的来源。", "⚠ Security: these actions do the following. Only import from sources you trust."),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var risk in risks.Take(30))
            {
                var detail = string.IsNullOrWhiteSpace(risk.Detail) ? "" : "：" + risk.Detail;
                warning.Children.Add(new TextBlock
                {
                    Text = $"• {risk.ActionName} · {RiskText(risk.Kind)}{detail}",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    IsTextSelectionEnabled = true
                });
            }
            if (risks.Count > 30)
                warning.Children.Add(new TextBlock { Text = "…", FontSize = 12 });
            panel.Children.Add(new Border
            {
                Child = warning,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 140, 40)),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(IsDark ? Windows.UI.Color.FromArgb(40, 220, 140, 40) : Windows.UI.Color.FromArgb(255, 255, 246, 230))
            });
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = T("没有发现运行程序、脚本、网络请求或第三方插件。", "No programs, scripts, network requests or third-party plugins found."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }

        // Settings of the actions (declared variables), e.g. a search address or a target language.
        var edits = new List<(RadialMenuCommand Command, StepProgram Program, StepVariableDef Variable, TextBox Box)>();
        foreach (var action in package.Actions)
        {
            foreach (var command in action.Commands.Where(c => c.PluginClass == ActionPackage.StepsPluginClass))
            {
                var program = StepProgram.FromJson(command.CommandSettings);
                if (program.Variables.Count == 0)
                    continue;
                panel.Children.Add(new TextBlock { Text = F("{0} 的设置", "Settings of {0}", action.Name), Style = BodyStrongTextBlockStyle, Margin = new Thickness(0, 6, 0, 0) });
                foreach (var variable in program.Variables)
                {
                    var box = new TextBox
                    {
                        Header = string.IsNullOrWhiteSpace(variable.Description) ? variable.Name : $"{variable.Description} ({variable.Name})",
                        Text = variable.Default ?? "",
                        AcceptsReturn = variable.Kind == "list",
                        TextWrapping = TextWrapping.Wrap
                    };
                    panel.Children.Add(box);
                    edits.Add((command, program, variable, box));
                }
            }
        }

        if (!await ConfirmDialogAsync(T("导入动作", "Import actions"), panel, T("导入", "Import")))
            return false;
        foreach (var group in edits.GroupBy(e => e.Command))
        {
            foreach (var edit in group)
                edit.Variable.Default = edit.Box.Text;
            group.Key.CommandSettings = group.First().Program.ToJson();
        }
        return true;
    }
}
