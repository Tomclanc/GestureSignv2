using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace GestureSign.WinUI;

/// <summary>
/// Quicker-style action editor in its own window: step palette on the left
/// (categories, filter, click or drag to add), the step tree in the middle
/// (groups, If/Else/End and loops drawn as indented blocks) and variables,
/// step properties and appearance on the right.
/// </summary>
public sealed partial class MainWindow
{
    private const string StepsPluginClass = "GestureSign.CorePlugins.Steps.StepsPlugin";
    private const string LibraryActionPluginClass = "GestureSign.CorePlugins.Library.LibraryActionPlugin";
    private const string SymbolFont = "Segoe Fluent Icons,Segoe MDL2 Assets";

    private enum EditorRowKind { Step, Else, End }

    private sealed record EditorRow(EditorRowKind Kind, Step Step, List<Step> Owner, int Depth);

    private static readonly string[] ActionIconGlyphs =
    {
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", ""
    };

    private string StepTypeName(StepTypeInfo info) => T(info.NameZh, info.NameEn);

    private static FontIcon Glyph(string glyph, double size = 16)
        => new() { Glyph = string.IsNullOrEmpty(glyph) ? "" : glyph, FontSize = size, FontFamily = new FontFamily(SymbolFont) };

    private static Brush? ThemeBrush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }

    private bool IsChineseUi => ResolveUiCultureName(_uiCultureName).StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static StepProgram ParseStepProgram(string? json)
    {
        try { return StepProgram.FromJson(json ?? string.Empty); }
        catch (System.Text.Json.JsonException) { return new StepProgram(); }
    }

    /// <summary>The step program an action runs: its steps, or one "Run command" step per plain command.</summary>
    private static StepProgram ProgramOf(RadialMenuAction action)
    {
        var first = action.Commands.FirstOrDefault();
        if (first is not null && string.Equals(first.PluginClass, StepsPluginClass, StringComparison.Ordinal))
            return ParseStepProgram(first.CommandSettings);
        var program = new StepProgram();
        foreach (var command in action.Commands)
        {
            var step = Step.Create(StepCatalog.RunCommand);
            step.Args["PluginClass"] = command.PluginClass ?? "";
            step.Args["Settings"] = command.CommandSettings ?? "";
            step.Disabled = !command.IsEnabled;
            program.Steps.Add(step);
        }
        return program;
    }

    private void EnsureRadialConfig() => _radialConfig ??= LoadRadialConfigForEditing();

    /// <summary>Creates (existing == null: after picking a template) or edits a library action.</summary>
    private async Task<RadialMenuAction?> EditRadialActionAsync(RadialMenuAction? existing)
    {
        if (existing is not null)
            return await OpenActionEditorAsync(existing, null);
        var (picked, template) = await PickActionTemplateAsync();
        return picked ? await OpenActionEditorAsync(null, template) : null;
    }

    /// <summary>Opens the editor window; completes with the saved action, or null when closed without saving.</summary>
    private Task<RadialMenuAction?> OpenActionEditorAsync(RadialMenuAction? existing, ActionTemplate? template)
    {
        EnsureRadialConfig();
        var completion = new TaskCompletionSource<RadialMenuAction?>();
        var zh = IsChineseUi;

        // ----- working copy -------------------------------------------------
        var program = existing is not null ? ProgramOf(existing).Clone() : template?.Build(zh) ?? new StepProgram();
        var name = existing?.Name ?? (template is null ? "" : T(template.NameZh, template.NameEn));
        var description = existing?.Description ?? (template is null ? "" : T(template.DescriptionZh, template.DescriptionEn));
        var category = existing?.Category ?? (template is null ? "" : T(template.CategoryZh, template.CategoryEn));
        var icon = existing?.Icon ?? template?.Glyph ?? "";
        var enabled = existing?.IsEnabled ?? true;
        var collapsed = new HashSet<Step>();
        EditorRow? selected = null;
        var summaryBlocks = new Dictionary<Step, TextBlock>();
        var undoStack = new Stack<string>();
        var redoStack = new Stack<string>();
        var suppressSelection = false;
        var rows = new List<EditorRow>();

        var window = new Window { Title = T("编辑动作", "Edit action") + (string.IsNullOrWhiteSpace(name) ? "" : " - " + name) };
        try { window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base }; } catch { }
        try
        {
            window.AppWindow.Resize(ScaleLogicalSize(1320, 840));
            window.AppWindow.SetIcon("Assets/logo.ico");
        }
        catch { }

        var root = new Grid { RequestedTheme = Root.ActualTheme, Padding = new Thickness(12), ColumnSpacing = 12, RowSpacing = 10 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Border Framed(UIElement child) => new()
        {
            Child = child,
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8)
        };

        // ----- variables known to the program -----------------------------
        IEnumerable<string> VariableNames()
        {
            var names = new List<string>();
            names.AddRange(program.Variables.Select(v => v.Name ?? ""));
            void Walk(IEnumerable<Step>? steps)
            {
                foreach (var step in steps ?? Enumerable.Empty<Step>())
                {
                    foreach (var key in new[] { "Output", "Item", "Index", "Name" })
                    {
                        var info = StepCatalog.Find(step.Type);
                        if (info?.Parameters.Any(p => p.Key == key && p.Kind == StepParameterKind.Variable) == true)
                            names.Add(step.Arg(key).Trim());
                    }
                    Walk(step.Body);
                    Walk(step.Else);
                }
            }
            Walk(program.Steps);
            names.Add("windowTitle");
            names.Add("processName");
            return names.Where(StepRunner.IsVariableName).Distinct(StringComparer.Ordinal);
        }

        // ----- middle: step tree -------------------------------------------
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            AllowDrop = true,
            CanDragItems = true,
            Padding = new Thickness(0, 4, 0, 4)
        };
        var emptyHint = new TextBlock
        {
            Text = T("从左侧点击或拖入步骤。", "Click or drag a step from the left to add it."),
            Opacity = 0.55,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false
        };

        string Summary(Step step)
        {
            var info = StepCatalog.Find(step.Type);
            if (info is null) return "";
            if (step.Type == StepCatalog.Group || step.Type == StepCatalog.Comment)
                return (step.Arg(step.Type == StepCatalog.Group ? "Title" : "Text")).Replace("\r", " ").Replace("\n", " ");
            var main = info.Parameters.FirstOrDefault(p => p.Key != "Output" && p.Kind != StepParameterKind.Boolean && !string.IsNullOrWhiteSpace(step.Arg(p.Key)));
            var text = "";
            if (main is not null)
            {
                text = step.Arg(main.Key);
                if (main.Kind == StepParameterKind.Choice && main.Choices is not null)
                {
                    var index = Array.IndexOf(main.Choices, text);
                    var names = IsChineseUi ? main.ChoiceNamesZh : main.ChoiceNamesEn;
                    if (index >= 0 && names is not null && index < names.Length) text = names[index];
                }
                if (step.Type == StepCatalog.RunCommand && main.Key == "PluginClass")
                    text = PluginName(text);
            }
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 90) text = text[..90] + "…";
            var output = step.Arg("Output").Trim();
            if (info.Parameters.Any(p => p.Key == "Output") && output.Length > 0)
                text += (text.Length > 0 ? "  " : "") + "⇒ {" + output + "}";
            return text;
        }

        void Flatten(List<Step> owner, int depth)
        {
            foreach (var step in owner)
            {
                rows.Add(new EditorRow(EditorRowKind.Step, step, owner, depth));
                var info = StepCatalog.Find(step.Type);
                if (info?.HasBody != true || collapsed.Contains(step))
                    continue;
                step.Body ??= new List<Step>();
                Flatten(step.Body, depth + 1);
                if (info.HasElse)
                {
                    step.Else ??= new List<Step>();
                    rows.Add(new EditorRow(EditorRowKind.Else, step, step.Else, depth));
                    Flatten(step.Else, depth + 1);
                }
                rows.Add(new EditorRow(EditorRowKind.End, step, owner, depth));
            }
        }

        var accent = ThemeBrush("AccentTextFillColorPrimaryBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        var guide = BorderBrush();

        FrameworkElement RowVisual(EditorRow row)
        {
            var grid = new Grid { MinHeight = 34, Tag = row, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(row.Depth * 22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Indent guides, one per nesting level.
            var guides = new StackPanel { Orientation = Orientation.Horizontal };
            for (var level = 0; level < row.Depth; level++)
                guides.Children.Add(new Border { Width = 22, BorderBrush = guide, BorderThickness = new Thickness(2, 0, 0, 0), Margin = new Thickness(9, -4, 0, -4) });
            grid.Children.Add(guides);

            var info = row.Step is null ? null : StepCatalog.Find(row.Step.Type);
            var stepName = info is null ? row.Step?.Type ?? "?" : StepTypeName(info);
            if (row.Kind != EditorRowKind.Step)
            {
                var label = new TextBlock
                {
                    Text = row.Kind == EditorRowKind.Else ? T("否则", "Else") : F("结束（{0}）", "End ({0})", stepName),
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 1);
                Grid.SetColumnSpan(label, 3);
                grid.Children.Add(label);
                return grid;
            }

            var step = row.Step!;
            if (info?.HasBody == true)
            {
                var chevron = new Button
                {
                    Content = Glyph(collapsed.Contains(step) ? "" : "", 10),
                    Padding = new Thickness(0),
                    Width = 20,
                    Height = 20,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chevron.Click += (_, _) =>
                {
                    if (!collapsed.Remove(step)) collapsed.Add(step);
                    Refresh(step);
                };
                Grid.SetColumn(chevron, 1);
                grid.Children.Add(chevron);
            }
            var iconElement = Glyph(step.Disabled ? "" : info?.Glyph ?? "");
            if (!step.Disabled) iconElement.Foreground = accent;
            iconElement.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(iconElement, 2);
            grid.Children.Add(iconElement);

            var texts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = stepName, FontWeight = info?.HasBody == true ? FontWeights.SemiBold : FontWeights.Normal };
            if (step.Disabled) title.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            texts.Children.Add(title);
            var summaryBlock = new TextBlock { Text = Summary(step), Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 640 };
            texts.Children.Add(summaryBlock);
            summaryBlocks[step] = summaryBlock;
            if (collapsed.Contains(step))
                texts.Children.Add(new TextBlock { Text = "…", Opacity = 0.6 });
            if (!string.IsNullOrWhiteSpace(step.Note))
                ToolTipService.SetToolTip(grid, step.Note);
            if (step.Disabled) texts.Opacity = 0.5;
            Grid.SetColumn(texts, 3);
            grid.Children.Add(texts);
            return grid;
        }

        // ----- right: properties, variables, appearance ---------------------
        var propertiesHost = new StackPanel { Spacing = 10 };
        var appearanceHost = new StackPanel { Spacing = 10 };
        var variablesList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 220 };
        var tabProperties = new ToggleButton { Content = T("步骤属性", "Step properties"), IsChecked = true };
        var tabAppearance = new ToggleButton { Content = T("外观", "Appearance") };
        var tabContent = new ScrollViewer { Content = propertiesHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };

        void ShowTab(bool properties)
        {
            tabProperties.IsChecked = properties;
            tabAppearance.IsChecked = !properties;
            tabContent.Content = properties ? propertiesHost : appearanceHost;
        }
        tabProperties.Click += (_, _) => ShowTab(true);
        tabAppearance.Click += (_, _) => ShowTab(false);

        void Refresh(Step? keep = null)
        {
            rows.Clear();
            summaryBlocks.Clear();
            Flatten(program.Steps, 0);
            list.Items.Clear();
            object? toSelect = null;
            foreach (var row in rows)
            {
                var visual = RowVisual(row);
                list.Items.Add(visual);
                if (keep is not null && row.Kind == EditorRowKind.Step && row.Step == keep)
                    toSelect = visual;
            }
            emptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            list.SelectedItem = toSelect;
            if (toSelect is null)
            {
                selected = null;
                ShowProperties();
            }
        }

        void RefreshRowOf(Step step)
        {
            // Usual case: only the summary changed; update it without touching the list.
            if (summaryBlocks.TryGetValue(step, out var block))
            {
                block.Text = Summary(step);
                return;
            }
            for (var index = 0; index < list.Items.Count; index++)
            {
                if (list.Items[index] is FrameworkElement { Tag: EditorRow row } && row.Kind == EditorRowKind.Step && row.Step == step)
                {
                    var wasSelected = list.SelectedIndex == index;
                    // Replacing the item drops and restores the selection; keep
                    // the properties form as it is while that happens.
                    suppressSelection = true;
                    try
                    {
                        list.Items[index] = RowVisual(row);
                        if (wasSelected) list.SelectedIndex = index;
                    }
                    finally
                    {
                        suppressSelection = false;
                    }
                    return;
                }
            }
        }

        void ShowProperties()
        {
            propertiesHost.Children.Clear();
            if (selected?.Kind != EditorRowKind.Step || selected.Step is not Step step)
            {
                propertiesHost.Children.Add(new TextBlock { Text = T("选择一个步骤进行编辑。", "Select a step to edit it."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
                return;
            }
            var info = StepCatalog.Find(step.Type);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var headerIcon = Glyph(info?.Glyph ?? "", 20);
            headerIcon.Foreground = accent;
            header.Children.Add(headerIcon);
            header.Children.Add(new TextBlock { Text = info is null ? step.Type ?? "?" : StepTypeName(info), Style = BodyStrongTextBlockStyle, VerticalAlignment = VerticalAlignment.Center });
            propertiesHost.Children.Add(header);
            if (info?.DescriptionZh is not null)
                propertiesHost.Children.Add(new TextBlock { Text = T(info.DescriptionZh, info.DescriptionEn ?? info.DescriptionZh), Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            step.Args ??= new Dictionary<string, string>();
            if (step.Type == StepCatalog.RunCommand)
                propertiesHost.Children.Add(NewStepCommandEditor(step, () => RefreshRowOf(step)));
            else
                foreach (var parameter in info?.Parameters ?? Array.Empty<StepParameter>())
                    propertiesHost.Children.Add(NewStepParameterEditor(step, parameter, VariableNames, () => RefreshRowOf(step)));
            var note = new TextBox { Header = T("备注", "Note"), Text = step.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
            note.TextChanged += (_, _) => step.Note = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text;
            propertiesHost.Children.Add(note);
        }

        list.SelectionChanged += (_, _) =>
        {
            if (suppressSelection) return;
            var row = list.SelectedItem is FrameworkElement { Tag: EditorRow r } ? r : null;
            // Same step still selected (row redrawn): keep the form and its focus.
            if (row is not null && selected is not null && row.Kind == selected.Kind && row.Step == selected.Step)
            {
                selected = row;
                return;
            }
            selected = row;
            if (selected?.Kind == EditorRowKind.Step) ShowTab(true);
            ShowProperties();
        };

        // ----- editing operations -------------------------------------------
        bool Contains(Step parent, Step candidate)
            => (parent.Body ?? new()).Concat(parent.Else ?? new()).Any(child => child == candidate || Contains(child, candidate));

        void InsertAt(Step step, EditorRow? target, bool after, bool checkpoint = true)
        {
            if (checkpoint) Checkpoint();
            if (target is null)
                program.Steps.Add(step);
            else if (target.Kind == EditorRowKind.Else)
                target.Owner.Insert(0, step);
            else if (target.Kind == EditorRowKind.End)
                target.Owner.Insert(target.Owner.IndexOf(target.Step) + 1, step);
            else if (after && StepCatalog.Find(target.Step.Type)?.HasBody == true && !collapsed.Contains(target.Step))
                (target.Step.Body ??= new List<Step>()).Insert(0, step);
            else
            {
                var index = target.Owner.IndexOf(target.Step);
                target.Owner.Insert(after ? index + 1 : index, step);
            }
            Refresh(step);
        }

        void AddStep(string type) => InsertAt(Step.Create(type), selected, after: true);

        void Move(int delta)
        {
            if (selected?.Kind != EditorRowKind.Step) return;
            var step = selected.Step;
            var owner = selected.Owner;
            var index = owner.IndexOf(step);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= owner.Count) return;
            Checkpoint();
            owner.RemoveAt(index);
            owner.Insert(target, step);
            Refresh(step);
        }

        void Duplicate()
        {
            if (selected?.Kind != EditorRowKind.Step) return;
            var step = selected.Step;
            Checkpoint();
            var clone = new StepProgram { Steps = { step } }.Clone().Steps[0];
            selected.Owner.Insert(selected.Owner.IndexOf(step) + 1, clone);
            Refresh(clone);
        }

        void ToggleDisabled()
        {
            if (selected?.Kind != EditorRowKind.Step) return;
            Checkpoint();
            selected.Step.Disabled = !selected.Step.Disabled;
            Refresh(selected.Step);
        }

        void Delete()
        {
            if (selected?.Kind != EditorRowKind.Step) return;
            Checkpoint();
            var owner = selected.Owner;
            var index = owner.IndexOf(selected.Step);
            owner.Remove(selected.Step);
            Refresh(owner.Count == 0 ? null : owner[Math.Min(index, owner.Count - 1)]);
        }

        // ----- undo / redo: snapshots of the whole program ------------------
        void Checkpoint()
        {
            var snapshot = program.ToJson();
            if (undoStack.Count == 0 || undoStack.Peek() != snapshot)
            {
                undoStack.Push(snapshot);
                redoStack.Clear();
            }
        }

        void Restore(string json)
        {
            var restored = StepProgram.FromJson(json);
            program.Steps = restored.Steps;
            program.Variables = restored.Variables;
            collapsed.Clear();
            var index = list.SelectedIndex;
            Refresh();
            FillVariables();
            if (index >= 0 && index < list.Items.Count)
                list.SelectedIndex = index;
        }

        void Undo()
        {
            var current = program.ToJson();
            while (undoStack.Count > 0)
            {
                var previous = undoStack.Pop();
                if (previous == current) continue;
                redoStack.Push(current);
                Restore(previous);
                return;
            }
        }

        void Redo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Push(program.ToJson());
            Restore(redoStack.Pop());
        }

        // ----- copy / paste steps between actions via the clipboard ----------
        const string StepClipboardPrefix = "GestureSign.Steps:";

        void CopySteps()
        {
            if (selected?.Kind != EditorRowKind.Step) return;
            var package = new DataPackage();
            package.SetText(StepClipboardPrefix + new StepProgram { Steps = { selected.Step } }.ToJson());
            Clipboard.SetContent(package);
        }

        async Task PasteStepsAsync()
        {
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text)) return;
                var text = await content.GetTextAsync();
                if (!text.StartsWith(StepClipboardPrefix, StringComparison.Ordinal)) return;
                var pasted = StepProgram.FromJson(text[StepClipboardPrefix.Length..]).Steps;
                if (pasted.Count == 0) return;
                Checkpoint();
                var anchor = selected;
                foreach (var step in pasted)
                {
                    InsertAt(step, anchor, after: true, checkpoint: false);
                    anchor = rows.FirstOrDefault(r => r.Kind == EditorRowKind.Step && r.Step == step) ?? anchor;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        Button ToolButton(string glyph, string tip, Action onClick)
        {
            var button = new Button { Content = Glyph(glyph, 14), Padding = new Thickness(8, 6, 8, 6), MinWidth = 36 };
            ToolTipService.SetToolTip(button, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => onClick();
            return button;
        }

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        toolbar.Children.Add(ToolButton("", T("上移", "Move up") + " (Ctrl+↑)", () => Move(-1)));
        toolbar.Children.Add(ToolButton("", T("下移", "Move down") + " (Ctrl+↓)", () => Move(1)));
        toolbar.Children.Add(ToolButton("", L("复制", "Copy", "複製", "복사") + " (Ctrl+D)", Duplicate));
        toolbar.Children.Add(ToolButton("", L("停用", "Disable", "停用", "사용 안 함") + " / " + L("启用", "Enable", "啟用", "사용"), ToggleDisabled));
        toolbar.Children.Add(ToolButton("", L("删除", "Delete", "刪除", "삭제") + " (Del)", Delete));

        toolbar.Children.Insert(0, ToolButton("\uE7A6", T("重做", "Redo") + " (Ctrl+Y)", Redo));
        toolbar.Children.Insert(0, ToolButton("\uE7A7", T("撤销", "Undo") + " (Ctrl+Z)", Undo));

        var stepMenu = new MenuFlyout();
        void MenuItem(string text, Action onClick)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => onClick();
            stepMenu.Items.Add(item);
        }
        MenuItem(L("复制", "Copy", "複製", "복사") + "\tCtrl+C", CopySteps);
        MenuItem(L("粘贴", "Paste", "貼上", "붙여넣기") + "\tCtrl+V", () => _ = PasteStepsAsync());
        MenuItem(T("创建副本", "Duplicate") + "\tCtrl+D", Duplicate);
        MenuItem(L("停用", "Disable", "停用", "사용 안 함") + " / " + L("启用", "Enable", "啟用", "사용"), ToggleDisabled);
        MenuItem(L("删除", "Delete", "刪除", "삭제") + "\tDel", Delete);
        list.ContextFlyout = stepMenu;

        list.KeyDown += (_, e) =>
        {
            var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (e.Key == VirtualKey.Delete) { Delete(); e.Handled = true; }
            else if (control && e.Key == VirtualKey.Up) { Move(-1); e.Handled = true; }
            else if (control && e.Key == VirtualKey.Down) { Move(1); e.Handled = true; }
            else if (control && e.Key == VirtualKey.D) { Duplicate(); e.Handled = true; }
            else if (control && e.Key == VirtualKey.C) { CopySteps(); e.Handled = true; }
            else if (control && e.Key == VirtualKey.V) { _ = PasteStepsAsync(); e.Handled = true; }
            else if (control && e.Key == VirtualKey.Z) { Undo(); e.Handled = true; }
            else if (control && e.Key == VirtualKey.Y) { Redo(); e.Handled = true; }
        };
        // Editing a field of a step: remember the state before the edit.
        propertiesHost.GotFocus += (_, _) => Checkpoint();

        // Drag and drop: new steps from the palette ("new:Type") and moves within the tree ("move").
        Step? dragging = null;
        list.DragItemsStarting += (_, e) =>
        {
            if (e.Items.FirstOrDefault() is FrameworkElement { Tag: EditorRow { Kind: EditorRowKind.Step } row })
            {
                dragging = row.Step;
                e.Data.SetText("move");
                e.Data.RequestedOperation = DataPackageOperation.Move;
            }
            else
            {
                e.Cancel = true;
            }
        };
        list.DragItemsCompleted += (_, _) => dragging = null;
        list.DragOver += (_, e) =>
        {
            e.AcceptedOperation = dragging is not null ? DataPackageOperation.Move : DataPackageOperation.Copy;
        };

        (EditorRow? Row, bool After) DropTarget(Windows.Foundation.Point position)
        {
            for (var index = 0; index < list.Items.Count; index++)
            {
                if (list.ContainerFromIndex(index) is not ListViewItem container || list.Items[index] is not FrameworkElement { Tag: EditorRow row })
                    continue;
                var top = container.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (position.Y >= top && position.Y < top + container.ActualHeight)
                    return (row, position.Y >= top + container.ActualHeight / 2);
            }
            return (null, true);
        }

        list.Drop += async (_, e) =>
        {
            var position = e.GetPosition(list);
            var (target, after) = DropTarget(position);
            if (dragging is not null)
            {
                var moving = dragging;
                dragging = null;
                if (target?.Step == moving || (target?.Step is not null && Contains(moving, target.Step)))
                    return;
                Checkpoint();
                RemoveFromTree(program.Steps, moving);
                InsertAt(moving, target, after, checkpoint: false);
                return;
            }
            if (!e.DataView.Contains(StandardDataFormats.Text)) return;
            var text = await e.DataView.GetTextAsync();
            if (text.StartsWith("new:", StringComparison.Ordinal))
                InsertAt(Step.Create(text[4..]), target, after);
        };

        static bool RemoveFromTree(List<Step> steps, Step step)
        {
            if (steps.Remove(step)) return true;
            foreach (var child in steps)
                if ((child.Body is not null && RemoveFromTree(child.Body, step)) || (child.Else is not null && RemoveFromTree(child.Else, step)))
                    return true;
            return false;
        }

        // ----- left: palette ------------------------------------------------
        var paletteFilter = new TextBox { PlaceholderText = T("筛选", "Filter") };
        var palette = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, CanDragItems = true };
        string? paletteCategory = null;
        var rail = new StackPanel { Spacing = 4 };

        void FillPalette()
        {
            palette.Items.Clear();
            var query = paletteFilter.Text.Trim();
            foreach (var info in StepCatalog.Types)
            {
                if (query.Length == 0 && paletteCategory is not null && info.Category != paletteCategory) continue;
                var label = StepTypeName(info);
                if (query.Length > 0 && !label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    && !info.Type.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Tag = info.Type, Padding = new Thickness(0, 4, 0, 4) };
                var paletteIcon = Glyph(info.Glyph);
                paletteIcon.Foreground = accent;
                row.Children.Add(paletteIcon);
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                if (info.DescriptionZh is not null)
                    ToolTipService.SetToolTip(row, T(info.DescriptionZh, info.DescriptionEn ?? info.DescriptionZh));
                palette.Items.Add(row);
            }
        }

        void SelectCategory(string? id)
        {
            paletteCategory = id;
            foreach (var child in rail.Children.OfType<ToggleButton>())
                child.IsChecked = Equals(child.Tag, id ?? "");
            FillPalette();
        }

        ToggleButton RailButton(string glyph, string tip, string? id)
        {
            var button = new ToggleButton { Content = Glyph(glyph, 16), Tag = id ?? "", Width = 40, Height = 36, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(button, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => SelectCategory(id);
            return button;
        }
        rail.Children.Add(RailButton("", T("全部", "All"), null));
        foreach (var stepCategory in StepCatalog.Categories)
            rail.Children.Add(RailButton(stepCategory.Glyph, T(stepCategory.NameZh, stepCategory.NameEn), stepCategory.Id));

        palette.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: string type }) AddStep(type);
        };
        palette.DragItemsStarting += (_, e) =>
        {
            if (e.Items.FirstOrDefault() is FrameworkElement { Tag: string type })
            {
                e.Data.SetText("new:" + type);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            else
            {
                e.Cancel = true;
            }
        };
        paletteFilter.TextChanged += (_, _) => FillPalette();
        SelectCategory(null);

        var paletteGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        paletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        paletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        paletteGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRowSpan(rail, 2);
        paletteGrid.Children.Add(rail);
        Grid.SetColumn(paletteFilter, 1);
        paletteGrid.Children.Add(paletteFilter);
        Grid.SetColumn(palette, 1);
        Grid.SetRow(palette, 1);
        paletteGrid.Children.Add(palette);
        root.Children.Add(Framed(paletteGrid));

        // ----- middle layout --------------------------------------------------
        var middle = new Grid { RowSpacing = 8 };
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var middleHeader = new Grid();
        middleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middleHeader.Children.Add(new TextBlock { Text = T("步骤", "Steps"), Style = BodyStrongTextBlockStyle, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toolbar, 1);
        middleHeader.Children.Add(toolbar);
        middle.Children.Add(middleHeader);
        var listHost = new Grid();
        listHost.Children.Add(list);
        listHost.Children.Add(emptyHint);
        Grid.SetRow(listHost, 1);
        middle.Children.Add(listHost);
        var middleBorder = Framed(middle);
        Grid.SetColumn(middleBorder, 1);
        root.Children.Add(middleBorder);

        // ----- variables ------------------------------------------------------
        string KindLabel(string? kind) => (kind ?? "text") switch
        {
            "list" => T("列表", "List"),
            "number" => T("数字", "Number"),
            "bool" => T("真假", "Yes/No"),
            _ => T("文本", "Text")
        };

        void FillVariables()
        {
            variablesList.Items.Clear();
            foreach (var variable in program.Variables)
            {
                var row = new Grid { Tag = variable, ColumnSpacing = 8, Padding = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = KindLabel(variable.Kind), Foreground = accent, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                var texts = new StackPanel();
                texts.Children.Add(new TextBlock { Text = variable.Name ?? "", FontFamily = new FontFamily("Cascadia Mono, Consolas") });
                var detail = string.IsNullOrWhiteSpace(variable.Description) ? variable.Default ?? "" : variable.Description!;
                if (detail.Length > 0)
                    texts.Children.Add(new TextBlock { Text = detail.Replace("\n", " "), FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);
                variablesList.Items.Add(row);
            }
        }

        void EditVariable(StepVariableDef variable, FrameworkElement anchor)
        {
            var variableNameBox = new TextBox { Header = L("名称", "Name", "名稱", "이름"), Text = variable.Name ?? "", FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var variableKindBox = new ComboBox { Header = T("类型", "Type"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var kinds = new[] { "text", "list", "number", "bool" };
            foreach (var kind in kinds) variableKindBox.Items.Add(KindLabel(kind));
            variableKindBox.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, variable.Kind ?? "text"));
            var variableDefaultBox = new TextBox { Header = T("默认值", "Default value"), Text = variable.Default ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
            var variableDescriptionBox = new TextBox { Header = T("说明", "Description"), Text = variable.Description ?? "" };
            var panel = new StackPanel { Spacing = 8, Width = 320 };
            panel.Children.Add(variableNameBox);
            panel.Children.Add(variableKindBox);
            panel.Children.Add(variableDefaultBox);
            panel.Children.Add(variableDescriptionBox);
            var flyout = new Flyout { Content = panel };
            flyout.Opened += (_, _) => Checkpoint();
            flyout.Closed += (_, _) =>
            {
                var newName = variableNameBox.Text.Trim();
                if (StepRunner.IsVariableName(newName)) variable.Name = newName;
                variable.Kind = kinds[Math.Max(0, variableKindBox.SelectedIndex)];
                variable.Default = variableDefaultBox.Text;
                variable.Description = string.IsNullOrWhiteSpace(variableDescriptionBox.Text) ? null : variableDescriptionBox.Text;
                FillVariables();
            };
            flyout.ShowAt(anchor);
        }

        var addVariable = ToolButton("", T("添加变量", "Add variable"), () => { });
        addVariable.Click += (_, _) =>
        {
            var index = 1;
            while (program.Variables.Any(v => v.Name == "var" + index)) index++;
            var variable = new StepVariableDef { Name = "var" + index, Kind = "text", Default = "" };
            Checkpoint();
            program.Variables.Add(variable);
            FillVariables();
            EditVariable(variable, addVariable);
        };
        var deleteVariable = ToolButton("", L("删除", "Delete", "刪除", "삭제"), () =>
        {
            if (variablesList.SelectedItem is FrameworkElement { Tag: StepVariableDef variable })
            {
                Checkpoint();
                program.Variables.Remove(variable);
                FillVariables();
            }
        });
        variablesList.IsItemClickEnabled = true;
        variablesList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: StepVariableDef variable } element)
                EditVariable(variable, element);
        };
        FillVariables();

        var variablesHeader = new Grid();
        variablesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        variablesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        variablesHeader.Children.Add(new TextBlock { Text = T("变量", "Variables"), Style = BodyStrongTextBlockStyle, VerticalAlignment = VerticalAlignment.Center });
        var variableButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        variableButtons.Children.Add(addVariable);
        variableButtons.Children.Add(deleteVariable);
        Grid.SetColumn(variableButtons, 1);
        variablesHeader.Children.Add(variableButtons);

        // ----- appearance -----------------------------------------------------
        var nameBox = new TextBox { Header = T("标题", "Title"), Text = name };
        nameBox.TextChanged += (_, _) =>
        {
            name = nameBox.Text;
            window.Title = T("编辑动作", "Edit action") + (string.IsNullOrWhiteSpace(name) ? "" : " - " + name);
        };
        var descriptionBox = new TextBox { Header = T("说明", "Description"), Text = description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        descriptionBox.TextChanged += (_, _) => description = descriptionBox.Text;
        var categoryBox = new AutoSuggestBox { Header = T("分类", "Category"), Text = category };
        var categories = _radialConfig!.Actions.Select(a => a.Category ?? "").Concat(ActionTemplates.All.Select(t => T(t.CategoryZh, t.CategoryEn)))
            .Where(c => c.Length > 0).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        categoryBox.ItemsSource = categories;
        categoryBox.GotFocus += (_, _) => categoryBox.IsSuggestionListOpen = true;
        categoryBox.TextChanged += (_, args) =>
        {
            category = categoryBox.Text;
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                categoryBox.ItemsSource = categories.Where(c => c.Contains(categoryBox.Text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        };
        var iconPreview = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(8), BorderBrush = BorderBrush(), BorderThickness = new Thickness(1) };
        void UpdateIconPreview()
        {
            if (icon.Length > 1)
            {
                // A program or image path: show its file name.
                iconPreview.Child = new TextBlock
                {
                    Text = System.IO.Path.GetFileName(icon),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2)
                };
                ToolTipService.SetToolTip(iconPreview, icon);
                return;
            }
            var shown = Glyph(icon.Length == 1 ? icon : "", 26);
            shown.Opacity = icon.Length == 1 ? 1 : 0.35;
            shown.HorizontalAlignment = HorizontalAlignment.Center;
            shown.VerticalAlignment = VerticalAlignment.Center;
            iconPreview.Child = shown;
        }
        UpdateIconPreview();
        var iconGrid = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Width = 320, MaxHeight = 280 };
        foreach (var glyph in ActionIconGlyphs)
            iconGrid.Items.Add(new Border { Width = 36, Height = 36, Tag = glyph, Child = Glyph(glyph, 18) });
        var iconFlyout = new Flyout { Content = iconGrid };
        iconGrid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: string glyph })
            {
                icon = glyph;
                UpdateIconPreview();
            }
            iconFlyout.Hide();
        };
        var chooseIcon = NewPillButton(T("选择图标", "Choose icon"), false);
        chooseIcon.Flyout = iconFlyout;
        var pickIconFile = NewPillButton(T("图片或程序…", "Image or program…"), false);
        pickIconFile.Click += async (_, _) =>
        {
            var path = await PickOpenFileAsync(new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".exe" });
            if (string.IsNullOrWhiteSpace(path)) return;
            icon = path;
            UpdateIconPreview();
        };
        var clearIcon = NewPillButton(L("清除", "Clear", "清除", "지우기"), false);
        clearIcon.Click += (_, _) => { icon = ""; UpdateIconPreview(); };
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        iconRow.Children.Add(iconPreview);
        var iconButtons = new StackPanel { Spacing = 6 };
        var iconButtonsTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        iconButtonsTop.Children.Add(chooseIcon);
        iconButtonsTop.Children.Add(clearIcon);
        iconButtons.Children.Add(iconButtonsTop);
        iconButtons.Children.Add(pickIconFile);
        iconRow.Children.Add(iconButtons);
        var enabledBox = new CheckBox { Content = L("启用", "Enable", "啟用", "사용"), IsChecked = enabled };
        enabledBox.Click += (_, _) => enabled = enabledBox.IsChecked == true;
        appearanceHost.Children.Add(iconRow);
        appearanceHost.Children.Add(nameBox);
        appearanceHost.Children.Add(descriptionBox);
        appearanceHost.Children.Add(categoryBox);
        appearanceHost.Children.Add(enabledBox);

        var right = new Grid { RowSpacing = 8 };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.Children.Add(variablesHeader);
        Grid.SetRow(variablesList, 1);
        right.Children.Add(variablesList);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        tabs.Children.Add(tabProperties);
        tabs.Children.Add(tabAppearance);
        Grid.SetRow(tabs, 2);
        right.Children.Add(tabs);
        Grid.SetRow(tabContent, 3);
        right.Children.Add(tabContent);
        var rightBorder = Framed(right);
        Grid.SetColumn(rightBorder, 2);
        root.Children.Add(rightBorder);

        // ----- save / cancel ----------------------------------------------------
        var saved = false;
        var save = new Button { Content = L("保存", "Save", "儲存", "저장"), MinWidth = 110 };
        if (ThemeStyle("AccentButtonStyle") is { } accentStyle) save.Style = accentStyle;
        var cancel = new Button { Content = L("取消", "Cancel", "取消", "취소"), MinWidth = 110 };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(save);
        bottom.Children.Add(cancel);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 3);
        root.Children.Add(bottom);

        // Test run: the background service runs the unsaved action in the
        // active window after 3 seconds, so there is time to switch to it.
        var testRun = new Button { Content = T("测试运行", "Test run"), MinWidth = 110 };
        var testStatus = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        testRun.Click += async (_, _) =>
        {
            try
            {
                var probe = new RadialMenuAction { Name = string.IsNullOrWhiteSpace(name) ? "Test" : name.Trim(), Commands = CommandsOf(name, program) };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_legacyData.RoamingPath, "TestAction.json"), System.Text.Json.JsonSerializer.Serialize(probe));
                var sent = await NotifyDaemonAsync(DaemonCommand.RunTestAction);
                testStatus.Text = sent
                    ? T("3 秒后在当前活动窗口中运行，请切换到目标窗口。", "Runs in the active window in 3 seconds; switch to the target window.")
                    : T("后台服务没有运行，无法测试。", "The background service is not running.");
            }
            catch (Exception ex)
            {
                testStatus.Text = ex.Message;
            }
        };
        var testPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        testPanel.Children.Add(testRun);
        testPanel.Children.Add(testStatus);
        Grid.SetRow(testPanel, 1);
        Grid.SetColumnSpan(testPanel, 2);
        root.Children.Add(testPanel);

        cancel.Click += (_, _) => window.Close();
        save.Click += (_, _) =>
        {
            var action = existing ?? new RadialMenuAction { Id = RadialMenuConfig.NewId(), TemplateId = template?.Id };
            var firstType = program.Steps.Select(s => StepCatalog.Find(s.Type)).FirstOrDefault(i => i is not null);
            action.Name = !string.IsNullOrWhiteSpace(name) ? name.Trim()
                : firstType is null ? T("新建动作", "New action") : StepTypeName(firstType);
            action.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            action.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            action.Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
            action.IsEnabled = enabled;
            action.Commands = CommandsOf(action.Name, program);
            if (existing is null)
                _radialConfig!.Actions.Add(action);
            SaveRadialConfig();
            saved = true;
            completion.TrySetResult(action);
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (!saved) completion.TrySetResult(null);
        };

        window.Content = root;
        Refresh(program.Steps.FirstOrDefault());
        // A blank new action starts on Appearance so it gets a title first.
        ShowTab(existing is not null || template is not null);
        // Owned by the main window: it always stays above it (and minimizes with it).
        var editorHwnd = IntPtr.Zero;
        try
        {
            editorHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            EditorSetWindowLongPtr(editorHwnd, GwlpHwndParent, ownerHwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        window.Activate();
        // A double click that opened the editor finishes afterwards and gives
        // focus back to the list; bring the editor forward once it has.
        window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            window.Activate();
            if (editorHwnd != IntPtr.Zero)
                EditorSetForegroundWindow(editorHwnd);
        });
        return completion.Task;
    }

    private const int GwlpHwndParent = -8;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr EditorSetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EditorSetForegroundWindow(IntPtr hWnd);

    private static Style? ThemeStyle(string key)
    {
        try { return Application.Current.Resources[key] as Style; }
        catch { return null; }
    }

    /// <summary>
    /// A program that is a single "Run command" step with no variables is stored
    /// as that plain command (so older builds and icons keep working); anything
    /// else is stored as a step program.
    /// </summary>
    private static List<RadialMenuCommand> CommandsOf(string name, StepProgram program)
    {
        if (program.Variables.Count == 0 && program.Steps.Count == 1
            && program.Steps[0] is { Type: StepCatalog.RunCommand, Disabled: false } only)
        {
            return new List<RadialMenuCommand>
            {
                new() { Name = name, PluginClass = only.Arg("PluginClass").Trim(), CommandSettings = only.Arg("Settings"), IsEnabled = true }
            };
        }
        return new List<RadialMenuCommand>
        {
            new() { Name = name, PluginClass = StepsPluginClass, PluginFilename = "GestureSign.CorePlugins.dll", CommandSettings = program.ToJson(), IsEnabled = true }
        };
    }

    /// <summary>The GestureSign command editor (hotkey recorder, app picker, typed settings) for a "Run command" step.</summary>
    private FrameworkElement NewStepCommandEditor(Step step, Action changed)
    {
        var initialClass = step.Arg("PluginClass");
        var plugin = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AddPluginItems(plugin);
        plugin.SelectedIndex = PluginIndex(initialClass);
        var pluginDescription = NewPluginDescriptionTextBlock();
        var pluginClass = new TextBox { PlaceholderText = T("自定义插件类名", "Custom plugin class name"), Text = initialClass };
        var settings = new TextBox
        {
            Text = step.Arg("Settings"),
            PlaceholderText = T("命令设置 JSON，可留空", "Command settings JSON (optional)"),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 80
        };
        var hotkey = NewHotKeyCommandEditor(settings, settings.Text, includeClear: true);
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var typedSettings = NewTypedCommandSettingsEditor(pluginClass, settings);

        void Update(bool resetSettings)
        {
            var selectedClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(selectedClass))
                pluginClass.Text = selectedClass;
            if (resetSettings)
                settings.Text = PluginSettingsTemplate(pluginClass.Text);
            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings, appPicker);
            UpdatePluginDescription(pluginDescription, pluginClass.Text);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClass.Text, settings.Text);
        }
        plugin.SelectionChanged += (_, _) => Update(true);
        pluginClass.TextChanged += (_, _) =>
        {
            if (step.Arg("PluginClass") == pluginClass.Text.Trim()) return;
            step.Args!["PluginClass"] = pluginClass.Text.Trim();
            changed();
        };
        settings.TextChanged += (_, _) =>
        {
            if (step.Arg("Settings") == settings.Text) return;
            step.Args!["Settings"] = settings.Text;
            changed();
        };
        Update(false);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(plugin);
        panel.Children.Add(pluginDescription);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(pluginClass);
        advanced.Children.Add(settings);
        panel.Children.Add(new Expander { Header = T("高级选项", "Advanced options"), Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return panel;
    }

    /// <summary>An editor for one step parameter; text fields get a button that inserts {variable}.</summary>
    private FrameworkElement NewStepParameterEditor(Step step, StepParameter parameter, Func<IEnumerable<string>> variables, Action changed)
    {
        var header = T(parameter.NameZh, parameter.NameEn);
        var hint = parameter.HintZh is null ? null : T(parameter.HintZh, parameter.HintEn ?? parameter.HintZh);
        var value = step.Arg(parameter.Key);
        void Set(string text)
        {
            // TextChanged also fires when the form is built; ignore no-op updates.
            if (string.Equals(step.Arg(parameter.Key), text, StringComparison.Ordinal))
                return;
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
                var choices = parameter.Choices ?? Array.Empty<string>();
                var names = (IsChineseUi ? parameter.ChoiceNamesZh : parameter.ChoiceNamesEn) ?? choices;
                for (var index = 0; index < choices.Length; index++)
                    combo.Items.Add(index < names.Length ? names[index] : choices[index]);
                combo.SelectedIndex = Array.FindIndex(choices, c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) Set(choices[combo.SelectedIndex]); };
                editor = combo;
                break;
            case StepParameterKind.Variable:
                var suggest = new AutoSuggestBox { Header = header, Text = value, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                suggest.GotFocus += (_, _) =>
                {
                    suggest.ItemsSource = variables().ToList();
                    suggest.IsSuggestionListOpen = true;
                };
                suggest.TextChanged += (_, args) =>
                {
                    Set(suggest.Text.Trim());
                    if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                        suggest.ItemsSource = variables().Where(n => n.Contains(suggest.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                };
                editor = suggest;
                break;
            default:
                var multiline = parameter.Kind is StepParameterKind.MultilineText or StepParameterKind.Code;
                var box = new TextBox
                {
                    Text = value,
                    AcceptsReturn = multiline,
                    TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = parameter.Kind == StepParameterKind.Code ? 160 : multiline ? 72 : 0
                };
                if (parameter.Kind == StepParameterKind.Code)
                {
                    box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                    box.IsSpellCheckEnabled = false;
                }
                box.TextChanged += (_, _) => Set(box.Text);

                var insert = new Button { Content = "{ }", Padding = new Thickness(6, 0, 6, 0), MinHeight = 24, FontSize = 12 };
                ToolTipService.SetToolTip(insert, T("插入变量", "Insert variable"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(insert, T("插入变量", "Insert variable"));
                insert.Click += (_, _) =>
                {
                    var flyout = new MenuFlyout();
                    foreach (var name in variables())
                    {
                        var item = new MenuFlyoutItem { Text = "{" + name + "}" };
                        item.Click += (_, _) =>
                        {
                            var caret = box.SelectionStart;
                            var token = "{" + name + "}";
                            box.Text = box.Text.Remove(caret, box.SelectionLength).Insert(caret, token);
                            box.SelectionStart = caret + token.Length;
                            box.Focus(FocusState.Programmatic);
                        };
                        flyout.Items.Add(item);
                    }
                    flyout.ShowAt(insert);
                };
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Bottom });
                Grid.SetColumn(insert, 1);
                head.Children.Add(insert);
                var stack = new StackPanel { Spacing = 4 };
                stack.Children.Add(head);
                stack.Children.Add(box);
                editor = stack;
                break;
        }

        if (hint is null)
            return editor;
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(editor);
        panel.Children.Add(new TextBlock { Text = hint, FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    /// <summary>Template gallery. Returns (true, null) for a blank action, (true, template), or (false, null) when cancelled.</summary>
    private async Task<(bool Picked, ActionTemplate? Template)> PickActionTemplateAsync()
    {
        var picked = false;
        ActionTemplate? result = null;
        ContentDialog? dialog = null;

        FrameworkElement Card(string glyph, string title, string description, Action onClick)
        {
            var content = new Grid { ColumnSpacing = 12, Width = 260, Height = 64 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph, 22);
            icon.VerticalAlignment = VerticalAlignment.Center;
            if (ThemeBrush("AccentTextFillColorPrimaryBrush") is { } cardAccent) icon.Foreground = cardAccent;
            content.Children.Add(icon);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            content.Children.Add(texts);
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8) };
            ToolTipService.SetToolTip(button, description);
            button.Click += (_, _) => { onClick(); dialog?.Hide(); };
            return button;
        }

        var panel = new StackPanel { Spacing = 6 };
        var blankWrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal };
        blankWrap.Children.Add(Card("", T("空白动作", "Blank action"), T("从空白开始添加步骤。", "Start with no steps."), () => picked = true));
        panel.Children.Add(blankWrap);
        foreach (var group in ActionTemplates.All.GroupBy(t => T(t.CategoryZh, t.CategoryEn)))
        {
            panel.Children.Add(new TextBlock { Text = group.Key, Style = BodyStrongTextBlockStyle, Margin = new Thickness(0, 8, 0, 0) });
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal };
            foreach (var template in group)
            {
                var chosen = template;
                wrap.Children.Add(Card(template.Glyph, T(template.NameZh, template.NameEn), T(template.DescriptionZh, template.DescriptionEn), () =>
                {
                    picked = true;
                    result = chosen;
                }));
            }
            panel.Children.Add(wrap);
        }

        dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = T("新建动作", "New action"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560, Padding = new Thickness(0, 0, 12, 0) },
            CloseButtonText = L("取消", "Cancel", "取消", "취소")
        };
        dialog.Resources["ContentDialogMaxWidth"] = 920.0;
        await ShowDialogAsync(dialog);
        return (picked, result);
    }
}
