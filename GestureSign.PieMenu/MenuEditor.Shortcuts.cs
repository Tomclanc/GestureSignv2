using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private FrameworkElement ShortcutPicker(string header, string initialValue, bool global, Action<string> update,
        bool physical = false, bool showHeader = true, Func<string, Task>? afterCommit = null)
    {
        var saved = initialValue;
        var capturing = false;
        var recordingHint = L(physical ? "menu-actions.simulate-hotkey.recording-placeholder" :
            global ? "settings.shortcut-recording" : "settings.quick-select-key-recording");
        var physicalModifiers = new List<string>();
        uint recordedModifiers = 0;
        var picker = new TextBox { Text = saved, PlaceholderText = L("settings.not-bound"), MaxLength = 120, MinWidth = 130, MaxWidth = 280 };
        var recordLabel = L("native.shortcut-record");
        var record = Button("●");
        record.Click += (_, _) => StartOrStop();
        var clear = Button("×", () => Finish("", commit: true));
        record.VerticalAlignment = clear.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(clear, L("native.shortcut-clear"));
        ToolTipService.SetToolTip(clear, L("native.shortcut-clear"));
        AutomationProperties.SetName(picker, header);
        SetRecordLabel(recordLabel);

        var row = new Grid { ColumnSpacing = 5 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(picker);
        Grid.SetColumn(record, 1);
        row.Children.Add(record);
        Grid.SetColumn(clear, 2);
        row.Children.Add(clear);
        var panel = new StackPanel { Spacing = 5 };
        if (showHeader) panel.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(row);
        if (global && !physical) ToolTipService.SetToolTip(panel, L("settings.shortcut-info"));

        picker.PreviewKeyDown += (_, e) =>
        {
            if (!capturing)
            {
                if ((uint)e.Key != 13) return;
                e.Handled = true;
                Finish(picker.Text, commit: true);
                return;
            }
            e.Handled = true;
            var modifiers = MenuShortcut.CurrentModifiers();
            if ((uint)e.Key == 27 && modifiers == 0) { Finish(saved, commit: false); return; }
            if (physical)
            {
                var name = PhysicalKeys.GetName(e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey, (uint)e.OriginalKey);
                if (name is null) return;
                if (PhysicalKeys.IsModifier(name))
                {
                    if (!physicalModifiers.Contains(name)) physicalModifiers.Add(name);
                    picker.Text = string.Join("+", physicalModifiers);
                    return;
                }
                Finish(string.Join("+", physicalModifiers.Append(name)), commit: true);
                return;
            }
            if (global) recordedModifiers |= modifiers;
            var key = MenuShortcut.FromVirtualKey((uint)e.OriginalKey);
            if (key is null || (!global && modifiers != 0))
            {
                if (global && recordedModifiers != 0)
                    picker.Text = MenuShortcut.Format(recordedModifiers, "").TrimEnd('+');
                return;
            }
            Finish(MenuShortcut.Format(recordedModifiers, key), commit: true);
        };
        picker.LostFocus += (_, _) => Finish(picker.Text, commit: !capturing);
        picker.Unloaded += (_, _) =>
        {
            if (capturing) Finish(saved, commit: false);
        };
        return panel;

        void SetRecordLabel(string label)
        {
            AutomationProperties.SetName(record, label);
            ToolTipService.SetToolTip(record, label);
        }

        async void StartOrStop()
        {
            if (capturing) { Finish(saved, commit: false); return; }
            capturing = true;
            physicalModifiers.Clear();
            recordedModifiers = 0;
            record.Content = "■";
            SetRecordLabel(L("native.shortcut-stop"));
            picker.Text = "";
            picker.IsReadOnly = true;
            picker.PlaceholderText = recordingHint;
            try { await _setShortcutRecording(true); }
            catch (Exception error) { SetStatus(error.Message); Finish(saved, commit: false); }
            if (!capturing) return;
            if (!picker.Focus(FocusState.Programmatic)) Finish(saved, commit: false);
        }

        void Finish(string candidate, bool commit)
        {
            var wasCapturing = capturing;
            if (capturing)
            {
                capturing = false;
                record.Content = "●";
                SetRecordLabel(recordLabel);
                picker.PlaceholderText = L("settings.not-bound");
                picker.IsReadOnly = false;
            }
            string? applied = null;
            if (commit && (wasCapturing || candidate != saved))
            {
                string canonical;
                var valid = physical ? PhysicalKeys.TryNormalize(candidate, out canonical) :
                    MenuShortcut.TryParse(candidate, out _, out canonical, allowModifiers: global);
                if (valid)
                {
                    if (saved != canonical)
                    {
                        saved = canonical;
                        update(canonical);
                        applied = canonical;
                    }
                    else if (wasCapturing) applied = canonical;
                }
                else SetStatus(L("native.shortcut-invalid"));
            }
            picker.Text = saved;
            if (wasCapturing || applied is not null && afterCommit is not null)
                _ = CompleteEditAsync(wasCapturing, applied);
        }

        async Task CompleteEditAsync(bool resume, string? shortcut)
        {
            try
            {
                if (resume) await _setShortcutRecording(false);
                if (shortcut is not null && afterCommit is not null) await afterCommit(shortcut);
            }
            catch (Exception error) { SetStatus(error.Message); }
        }
    }

    private Expander ConditionFields(PieMenu menu)
    {
        var conditions = menu.Conditions ??= new MenuConditions();
        return PropertySection(L("settings.menu-conditions"),
            Condition("settings.app-condition", conditions.AppName, value => conditions.AppName = value.Trim()),
            Condition("settings.window-condition", conditions.WindowName, value => conditions.WindowName = value.Trim()),
            Condition("settings.area-condition-left-placeholder", FullNumber(conditions.XMin), value => conditions.XMin = ParseNumber(value)),
            Condition("settings.area-condition-top-placeholder", FullNumber(conditions.YMin), value => conditions.YMin = ParseNumber(value)),
            Condition("settings.area-condition-right-placeholder", FullNumber(conditions.XMax), value => conditions.XMax = ParseNumber(value)),
            Condition("settings.area-condition-bottom-placeholder", FullNumber(conditions.YMax), value => conditions.YMax = ParseNumber(value)));

        FrameworkElement Condition(string key, string value, Action<string> update)
        {
            var title = L(key);
            return Property(title, Field(null, value, update, title));
        }
    }
}
