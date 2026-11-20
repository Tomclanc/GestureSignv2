using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GestureSign.WinUI;

/// <summary>
/// Visual step editor (Quicker-style): a step tree on the left, where If,
/// Repeat and For each hold indented children, and a parameter form for the
/// selected step on the right, generated from <see cref="StepCatalog"/>.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record StepRow(Step? Step, List<Step> Owner, int Depth, bool IsElseHeader, Step? ElseOf);

    private string StepTypeName(StepTypeInfo info) => T(info.NameZh, info.NameEn);
    private string StepCategoryName(StepTypeInfo info) => T(info.CategoryZh, info.CategoryEn);

    /// <summary>Edits a copy of the program; returns the edited program, or null when cancelled.</summary>
    private async Task<StepProgram?> EditStepProgramAsync(StepProgram initial)
    {
        var program = initial.Clone();
        StepRow? selected = null;

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MinHeight = 380, MaxHeight = 520 };
        var form = new StackPanel { Spacing = 8 };
        var rows = new List<StepRow>();

        void Flatten(List<Step> owner, int depth)
        {
            foreach (var step in owner)
            {
                rows.Add(new StepRow(step, owner, depth, false, null));
                var info = StepCatalog.Find(step.Type);
                if (info?.HasBody == true)
                {
                    step.Body ??= new List<Step>();
                    Flatten(step.Body, depth + 1);
                }
                if (info?.HasElse == true)
                {
                    step.Else ??= new List<Step>();
                    rows.Add(new StepRow(null, step.Else, depth, true, step));
                    Flatten(step.Else, depth + 1);
                }
            }
        }

        string Summary(Step step)
        {
            var info = StepCatalog.Find(step.Type);
            if (info is null) return step.Type ?? "?";
            var first = info.Parameters.FirstOrDefault(p => p.Kind != StepParameterKind.Boolean && !string.IsNullOrWhiteSpace(step.Arg(p.Key)));
            var detail = first is null ? "" : step.Arg(first.Key).Replace("\r", " ").Replace("\n", " ");
            if (detail.Length > 48) detail = detail[..48] + "…";
            return detail;
        }

        void Refresh(Step? keepSelected = null, Step? keepElseOf = null)
        {
            rows.Clear();
            Flatten(program.Steps, 0);
            list.Items.Clear();
            object? toSelect = null;
            foreach (var row in rows)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(row.Depth * 22, 0, 0, 0) };
                if (row.IsElseHeader)
                {
                    panel.Children.Add(new TextBlock { Text = T("否则", "Else"), FontWeight = FontWeights.SemiBold, Opacity = 0.8 });
                }
                else
                {
                    var info = StepCatalog.Find(row.Step!.Type);
                    var name = new TextBlock { Text = info is null ? row.Step.Type ?? "?" : StepTypeName(info), FontWeight = info?.HasBody == true ? FontWeights.SemiBold : FontWeights.Normal };
                    panel.Children.Add(name);
                    var summary = Summary(row.Step);
                    if (summary.Length > 0)
                        panel.Children.Add(new TextBlock { Text = summary, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 });
                    if (row.Step.Disabled)
                        panel.Opacity = 0.45;
                }
                panel.Tag = row;
                list.Items.Add(panel);
                if ((keepSelected is not null && row.Step == keepSelected) || (keepElseOf is not null && row.ElseOf == keepElseOf))
                    toSelect = panel;
            }
            if (rows.Count == 0)
                list.Items.Add(new TextBlock { Text = T("添加步骤", "Add step") + " …", Opacity = 0.5, IsHitTestVisible = false });
            list.SelectedItem = toSelect;
            if (toSelect is null)
            {
                selected = null;
                ShowForm();
            }
        }

        void ShowForm()
        {
            form.Children.Clear();
            if (selected?.Step is not Step step)
            {
                form.Children.Add(new TextBlock { Text = T("选择一个步骤进行编辑。", "Select a step to edit it."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
                return;
            }
            var info = StepCatalog.Find(step.Type);
            form.Children.Add(new TextBlock { Text = info is null ? step.Type ?? "?" : StepTypeName(info), Style = BodyStrongTextBlockStyle });
            step.Args ??= new Dictionary<string, string>();
            foreach (var parameter in info?.Parameters ?? Array.Empty<StepParameter>())
                form.Children.Add(NewStepParameterEditor(step, parameter, () => UpdateRowSummary(step)));
            var note = new TextBox { Header = T("备注", "Note"), Text = step.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            note.TextChanged += (_, _) => step.Note = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text;
            form.Children.Add(note);
        }

        void UpdateRowSummary(Step step)
        {
            foreach (var item in list.Items.OfType<StackPanel>())
            {
                if (item.Tag is StepRow { Step: var s } && s == step && item.Children.Count > 0)
                {
                    var summary = Summary(step);
                    if (item.Children.Count > 1 && item.Children[1] is TextBlock existing)
                        existing.Text = summary;
                    else if (summary.Length > 0)
                        item.Children.Add(new TextBlock { Text = summary, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 });
                }
            }
        }

        list.SelectionChanged += (_, _) =>
        {
            selected = list.SelectedItem is FrameworkElement { Tag: StepRow row } ? row : null;
            ShowForm();
        };

        // Where a new step goes: into a selected block or Else branch, else after the selection.
        void Insert(Step step)
        {
            if (selected is null)
                program.Steps.Add(step);
            else if (selected.IsElseHeader)
                selected.Owner.Insert(0, step);
            else if (StepCatalog.Find(selected.Step!.Type)?.HasBody == true)
                (selected.Step.Body ??= new List<Step>()).Insert(0, step);
            else
                selected.Owner.Insert(selected.Owner.IndexOf(selected.Step) + 1, step);
            Refresh(step);
        }

        var addFlyout = new MenuFlyout();
        foreach (var category in StepCatalog.Types.GroupBy(t => StepCategoryName(t)))
        {
            var sub = new MenuFlyoutSubItem { Text = category.Key };
            foreach (var info in category)
            {
                var item = new MenuFlyoutItem { Text = StepTypeName(info) };
                var type = info.Type;
                item.Click += (_, _) => Insert(Step.Create(type));
                sub.Items.Add(item);
            }
            addFlyout.Items.Add(sub);
        }
        var add = new DropDownButton { Content = T("添加步骤", "Add step"), Flyout = addFlyout };

        Button Tool(string text, Action onClick)
        {
            var button = NewPillButton(text, false);
            button.Click += (_, _) => onClick();
            return button;
        }

        void Move(int delta)
        {
            if (selected?.Step is not Step step) return;
            var owner = selected.Owner;
            var index = owner.IndexOf(step);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= owner.Count) return;
            owner.RemoveAt(index);
            owner.Insert(target, step);
            Refresh(step);
        }

        var up = Tool(T("上移", "Move up"), () => Move(-1));
        var down = Tool(T("下移", "Move down"), () => Move(1));
        var copy = Tool(L("复制", "Copy", "複製", "복사"), () =>
        {
            if (selected?.Step is not Step step) return;
            var clone = StepProgram.FromJson(new StepProgram { Steps = { step } }.ToJson()).Steps[0];
            selected.Owner.Insert(selected.Owner.IndexOf(step) + 1, clone);
            Refresh(clone);
        });
        var disable = Tool(L("停用", "Disable", "停用", "사용 안 함") + " / " + L("启用", "Enable", "啟用", "사용"), () =>
        {
            if (selected?.Step is not Step step) return;
            step.Disabled = !step.Disabled;
            Refresh(step);
        });
        var delete = Tool(L("删除", "Delete", "刪除", "삭제"), () =>
        {
            if (selected?.Step is not Step step) return;
            var owner = selected.Owner;
            var index = owner.IndexOf(step);
            owner.Remove(step);
            Refresh(owner.Count == 0 ? null : owner[Math.Min(index, owner.Count - 1)]);
        });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var element in new FrameworkElement[] { add, up, down, copy, disable, delete })
            toolbar.Children.Add(element);

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(toolbar);
        left.Children.Add(new Border { BorderBrush = BorderBrush(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = list });
        var grid = new Grid { ColumnSpacing = 16, MinWidth = 900 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var right = new ScrollViewer { Content = form, MaxHeight = 560 };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        Refresh();

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = T("编辑步骤", "Edit steps"),
            Content = grid,
            PrimaryButtonText = L("保存", "Save", "儲存", "저장"),
            CloseButtonText = L("取消", "Cancel", "取消", "취소"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? program : null;
    }

    private FrameworkElement NewStepParameterEditor(Step step, StepParameter parameter, Action changed)
    {
        var header = T(parameter.NameZh, parameter.NameEn);
        var hint = parameter.HintZh is null ? null : T(parameter.HintZh, parameter.HintEn ?? parameter.HintZh);
        var value = step.Arg(parameter.Key);
        void Set(string text)
        {
            step.Args![parameter.Key] = text;
            changed();
        }

        FrameworkElement editor;
        switch (parameter.Kind)
        {
            case StepParameterKind.Boolean:
                var check = new CheckBox { Content = header, IsChecked = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) };
                check.Click += (_, _) => Set(check.IsChecked == true ? "true" : "false");
                editor = check;
                break;
            case StepParameterKind.Choice:
                var combo = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var choice in parameter.Choices ?? Array.Empty<string>())
                    combo.Items.Add(choice);
                combo.SelectedItem = (parameter.Choices ?? Array.Empty<string>()).FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
                combo.SelectionChanged += (_, _) => Set(combo.SelectedItem as string ?? "");
                editor = combo;
                break;
            default:
                var multiline = parameter.Kind is StepParameterKind.MultilineText or StepParameterKind.Code;
                var box = new TextBox
                {
                    Header = header,
                    Text = value,
                    AcceptsReturn = multiline,
                    TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = parameter.Kind == StepParameterKind.Code ? 140 : multiline ? 72 : 0,

                };
                if (parameter.Kind == StepParameterKind.Code)
                {
                    box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                    box.IsSpellCheckEnabled = false;
                }
                box.TextChanged += (_, _) => Set(box.Text);
                editor = box;
                break;
        }

        if (hint is null)
            return editor;
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(editor);
        panel.Children.Add(new TextBlock { Text = hint, FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
