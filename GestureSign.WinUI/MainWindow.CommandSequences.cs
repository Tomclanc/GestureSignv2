using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private async Task ManageCommandsAsync(LegacyAction action)
    {
        var location = _legacyData.Applications
            .SelectMany((app, appIndex) => app.Actions.Select((item, actionIndex) => (item, appIndex, actionIndex)))
            .FirstOrDefault(entry => ReferenceEquals(entry.item.Source, action.Source));
        if (location.item is null)
            return;

        while (true)
        {
            // Editors reload the store, so never reuse JSON nodes from a previous dialog.
            action = _legacyData.Applications[location.appIndex].Actions[location.actionIndex];
            Func<Task>? next = null;
            var panel = NewCardPanel(8);
            panel.MinWidth = 480;
            panel.Children.Add(new TextBlock
            {
                Text = IntentText("按列表顺序执行；可在命令之间添加延迟等待。", "Commands run in list order. Insert a delay between commands if needed."),
                TextWrapping = TextWrapping.Wrap
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = IntentText("设置命令", "Set Command") + " · " + action.Name,
                CloseButtonText = IntentText("完成", "Close"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 500 }
            };
            Task Choose(Func<Task> operation)
            {
                next = operation;
                dialog.Hide();
                return Task.CompletedTask;
            }
            Task Move(LegacyCommand command, int offset)
            {
                _legacyData.MoveCommand(action, command, offset);
                ReloadActionDataOnly();
                return Task.CompletedTask;
            }

            for (var index = 0; index < action.Commands.Count; index++)
            {
                var command = action.Commands[index];
                var row = NewCardPanel(4);
                row.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}. {DisplayName(command.Name)} · {PluginName(command.PluginClass)}" +
                        (command.IsEnabled ? "" : $" · {IntentText("停用", "Disabled")}"),
                    TextWrapping = TextWrapping.Wrap
                });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                void AddButton(string text, Func<Task> operation, bool enabled = true)
                {
                    var button = new Button { Content = text, IsEnabled = enabled };
                    button.Click += async (_, _) => await Choose(operation);
                    buttons.Children.Add(button);
                }
                AddButton(IntentText("编辑", "Edit"), () => command.PluginClass == "GestureSign.CorePlugins.Delay.Delay"
                    ? EditSequenceDelayAsync(action, command) : EditCommandAsync(command));
                AddButton(IntentText("上移", "Move up"), () => Move(command, -1), index > 0);
                AddButton(IntentText("下移", "Move down"), () => Move(command, 1), index + 1 < action.Commands.Count);
                AddButton(IntentText("删除", "Delete"), () => DeleteCommandAsync(action, command));
                row.Children.Add(buttons);
                panel.Children.Add(NewCard(row, new Thickness(8)));
            }
            panel.Children.Add(NewInlineButtonsWithContext(
                (IntentText("添加命令", "Add command"), _ => Choose(() => AddCommandAsync(action))),
                (IntentText("添加延时", "Add delay"), _ => Choose(() => EditSequenceDelayAsync(action, null)))));
            await dialog.ShowAsync();
            if (next is null)
                return;
            await next();
        }
    }

    private async Task EditSequenceDelayAsync(LegacyAction action, LegacyCommand? command)
    {
        var timeout = 500d;
        if (command is not null)
        {
            try
            {
                using var settings = JsonDocument.Parse(command.Settings);
                // Preserve imported waits for window/menu events in the full editor.
                if (settings.RootElement.TryGetProperty("WaitType", out var wait) && wait.TryGetInt32(out var waitType) && waitType != 0)
                {
                    await EditCommandAsync(command);
                    return;
                }
                if (settings.RootElement.TryGetProperty("Timeout", out var value))
                    timeout = value.TryGetDouble(out var durationValue) ? Math.Clamp(durationValue, 0, 600000) : 500;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        var milliseconds = new NumberBox
        {
            Header = IntentText("等待时间（毫秒）", "Wait (milliseconds)"),
            Minimum = 0, Maximum = 600000, Value = timeout,
            SmallChange = 100, LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var editor = NewCardPanel(8);
        editor.Children.Add(milliseconds);
        var enabled = new CheckBox { Content = IntentText("启用", "Enabled"), IsChecked = command?.IsEnabled ?? true };
        editor.Children.Add(enabled);
        if (!await ConfirmDialogAsync(IntentText("延迟等待", "Delay"), editor, IntentText("保存", "Save")))
            return;
        if (!double.IsFinite(milliseconds.Value) || milliseconds.Value < 0 || milliseconds.Value > 600000)
            return;
        var duration = (int)Math.Round(milliseconds.Value);
        var json = JsonSerializer.Serialize(new { WaitType = 0, Timeout = duration });
        var name = $"{IntentText("延迟等待", "Delay")} · {duration} ms";
        if (command is null)
            _legacyData.AddCommand(action, name, "GestureSign.CorePlugins.Delay.Delay", json, enabled.IsChecked == true);
        else
            _legacyData.UpdateCommand(command, name, command.PluginClass, json, enabled.IsChecked == true);
        ReloadActionDataOnly();
    }
}
