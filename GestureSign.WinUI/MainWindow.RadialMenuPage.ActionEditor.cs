using System;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    /// <summary>
    /// Creates (existing == null) or edits a library action. A new action is
    /// added to the library; returns null when the dialog is cancelled.
    /// </summary>
    private const string StepsPluginClass = "GestureSign.CorePlugins.Steps.StepsPlugin";

    private static StepProgram ParseStepProgram(string? json)
    {
        try { return StepProgram.FromJson(json ?? string.Empty); }
        catch (System.Text.Json.JsonException) { return new StepProgram(); }
    }

    private async Task<RadialMenuAction?> EditRadialActionAsync(RadialMenuAction? existing)
    {
        var existingCommand = existing?.Commands.FirstOrDefault();
        var existingIsSteps = string.Equals(existingCommand?.PluginClass, StepsPluginClass, StringComparison.Ordinal);
        var stepProgram = existingIsSteps ? ParseStepProgram(existingCommand!.CommandSettings) : new StepProgram();
        if (existingIsSteps)
            existingCommand = null; // the single-command editor starts blank
        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add(T("单条命令", "Single command"));
        mode.Items.Add(T("步骤", "Steps"));
        mode.SelectedIndex = existingIsSteps ? 1 : 0;
        var stepsSummary = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        stepsSummary.Text = F("{0} 个步骤", "{0} steps", stepProgram.Steps.Count) + " · " + T("编辑步骤", "Edit steps") + " →";
        var name = new TextBox
        {
            Header = L("名称", "Name", "名稱", "이름"),
            PlaceholderText = L("命令名称", "Command name", "命令名稱", "명령 이름"),
            Text = existing?.Name ?? ""
        };
        var plugin = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        AddPluginItems(plugin);
        plugin.SelectedIndex = existingCommand is null ? 0 : PluginIndex(existingCommand.PluginClass ?? "");
        var pluginDescription = NewPluginDescriptionTextBlock();
        var pluginClass = new TextBox
        {
            PlaceholderText = T("自定义插件类名", "Custom plugin class name"),
            Text = existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var settings = new TextBox
        {
            Text = existingCommand?.CommandSettings ?? PluginSettingsTemplate(existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex)),
            PlaceholderText = T("命令设置 JSON，可留空", "Command settings JSON (optional)"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 80
        };
        var hotkey = NewHotKeyCommandEditor(settings, settings.Text, includeClear: true);
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var typedSettings = NewTypedCommandSettingsEditor(pluginClass, settings);
        var enabled = new CheckBox
        {
            Content = L("启用", "Enable", "啟用", "사용"),
            IsChecked = existing?.IsEnabled ?? true,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Keep the chosen operation independently of the controls; WinUI may
        // update the ComboBox and the hidden class field at different times
        // while the dialog closes (same approach as the edge command editor).
        var selectedPluginClassValue = existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex);
        string SelectedPluginClass() => string.IsNullOrWhiteSpace(selectedPluginClassValue) ? pluginClass.Text.Trim() : selectedPluginClassValue;

        void UpdateEditor(bool resetSettings)
        {
            var selectedClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(selectedClass))
            {
                selectedPluginClassValue = selectedClass;
                pluginClass.Text = selectedClass;
            }
            else
            {
                selectedPluginClassValue = "";
            }
            if (resetSettings)
                settings.Text = PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings, appPicker);
            UpdatePluginDescription(pluginDescription, pluginClass.Text);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClass.Text, settings.Text);
        }
        plugin.SelectionChanged += (_, _) => UpdateEditor(true);

        var panel = NewCardPanel(8);
        panel.Children.Add(name);
        panel.Children.Add(mode);
        // Single-command controls; hidden in steps mode.
        var commandPanel = NewCardPanel(8);
        commandPanel.Children.Add(plugin);
        commandPanel.Children.Add(pluginDescription);
        commandPanel.Children.Add(hotkey);
        commandPanel.Children.Add(appPicker);
        commandPanel.Children.Add(typedSettings);
        var advanced = NewCardPanel(8);
        advanced.Children.Add(pluginClass);
        advanced.Children.Add(settings);
        commandPanel.Children.Add(new Expander { Header = T("高级选项", "Advanced options"), Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(commandPanel);
        panel.Children.Add(stepsSummary);
        panel.Children.Add(enabled);
        void UpdateMode()
        {
            var steps = mode.SelectedIndex == 1;
            commandPanel.Visibility = steps ? Visibility.Collapsed : Visibility.Visible;
            stepsSummary.Visibility = steps ? Visibility.Visible : Visibility.Collapsed;
        }
        mode.SelectionChanged += (_, _) => UpdateMode();
        UpdateMode();
        UpdateEditor(false);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = existing is null ? T("新建动作", "New action") : L("编辑", "Edit", "編輯", "편집"),
            Content = NewDialogScrollContent(panel),
            PrimaryButtonText = L("保存", "Save", "儲存", "저장"),
            CloseButtonText = L("取消", "Cancel", "取消", "취소"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return null;

        if (mode.SelectedIndex == 1)
        {
            // Steps mode: the step editor opens once this dialog has closed.
            var edited = await EditStepProgramAsync(stepProgram);
            if (edited is null && existing is null)
                return null;
            stepProgram = edited ?? stepProgram;
            var stepsAction = existing ?? new RadialMenuAction { Id = RadialMenuConfig.NewId() };
            var firstType = stepProgram.Steps.Select(s => StepCatalog.Find(s.Type)).FirstOrDefault(i => i is not null);
            stepsAction.Name = string.IsNullOrWhiteSpace(name.Text)
                ? (string.IsNullOrWhiteSpace(stepsAction.Name) ? (firstType is null ? T("步骤", "Steps") : StepTypeName(firstType)) : stepsAction.Name)
                : name.Text.Trim();
            stepsAction.IsEnabled = enabled.IsChecked ?? true;
            var stepsCommand = new RadialMenuCommand
            {
                Name = stepsAction.Name,
                PluginClass = StepsPluginClass,
                CommandSettings = stepProgram.ToJson(),
                IsEnabled = true
            };
            if (stepsAction.Commands.Count == 0) stepsAction.Commands.Add(stepsCommand);
            else stepsAction.Commands[0] = stepsCommand;
            if (existing is null)
                _radialConfig!.Actions.Add(stepsAction);
            return stepsAction;
        }

        CommitSelectedAppCommandChoice(appPicker, plugin, pluginClass, settings);
        var pluginClassValue = SelectedPluginClass();
        if (string.IsNullOrWhiteSpace(pluginClassValue))
        {
            await ShowInfoDialog(T("插件类名为空", "Missing plugin class name"), T("请选择一个操作，或填写自定义插件类名。", "Choose an operation or enter a custom plugin class name."));
            return null;
        }

        var label = string.IsNullOrWhiteSpace(name.Text) ? RadialMenuAutoLabel(pluginClassValue, settings.Text) : name.Text.Trim();
        var command = new RadialMenuCommand
        {
            Name = label,
            PluginClass = pluginClassValue,
            CommandSettings = settings.Text,
            IsEnabled = true
        };
        var action = existing ?? new RadialMenuAction { Id = RadialMenuConfig.NewId() };
        action.Name = label;
        action.IsEnabled = enabled.IsChecked ?? true;
        // Phase A edits the first command; further commands (if any) are kept.
        if (action.Commands.Count == 0)
            action.Commands.Add(command);
        else
            action.Commands[0] = command;
        if (existing is null)
            _radialConfig!.Actions.Add(action);
        return action;
    }
}
