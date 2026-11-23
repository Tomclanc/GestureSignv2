using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    /// <summary>Text prefix marking a drag that moves a wheel slot instead of placing a library action.</summary>
    private const string SlotDragPrefix = "radial-slot:";

    /// <summary>DataPackage property carrying the source slot key of a slot drag.</summary>
    private const string SlotDragKeyProperty = "GestureSign.RadialSlotKey";

    // ----- wheel (center) -----------------------------------------------

    private void RefreshRadialWheel()
    {
        if (_radialWheelHost is null || _radialConfig is null)
            return;
        var config = _radialConfig;
        var menu = SelectedRadialMenu();
        var isGlobal = menu.Scope == RadialMenuScope.Global;
        var isSubmenu = menu.Scope == RadialMenuScope.Submenu;
        // Only top-level application/taskbar/desktop wheels inherit empty slots from the global wheel.
        var inherits = !isGlobal && !isSubmenu;
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };

        var path = new[] { RadialMenuTitle(TopRadialMenu()) }
            .Concat(_radialSubmenuPath.Select(id => config.FindMenuById(id)).Where(m => m is not null).Select(m => RadialMenuTitle(m!)));
        panel.Children.Add(new TextBlock { Text = string.Join("  ›  ", path), Style = BodyStrongTextBlockStyle, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (isSubmenu)
        {
            var back = NewPillButton(T("返回上级", "Back"), false);
            back.HorizontalAlignment = HorizontalAlignment.Center;
            back.Click += (_, _) =>
            {
                if (_radialSubmenuPath.Count > 0)
                    _radialSubmenuPath.RemoveAt(_radialSubmenuPath.Count - 1);
                RefreshRadialWheel();
            };
            panel.Children.Add(back);
            panel.Children.Add(new TextBlock
            {
                Text = T("使用时：在文件夹格子上停一下打开子菜单，在中心停一下返回上级。", "In use: rest on a folder slot to open it; rest in the center to go back."),
                Opacity = 0.6,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        if (inherits)
        {
            var disable = new CheckBox { Content = T("在此程序中禁用轮盘", "Disable the radial menu in this app"), IsChecked = menu.Disabled, HorizontalAlignment = HorizontalAlignment.Center };
            disable.Click += (_, _) =>
            {
                menu.Disabled = disable.IsChecked == true;
                SaveRadialConfig();
                RefreshRadialMenuList();
                RefreshRadialWheel();
            };
            panel.Children.Add(disable);
        }

        var canvas = new Canvas { Width = WheelSize, Height = WheelSize, Opacity = menu.Disabled ? 0.4 : 1 };
        var accent = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
        var ownFill = new SolidColorBrush(IsDark ? Color.FromArgb(255, 45, 55, 70) : Color.FromArgb(255, 232, 241, 252));
        var inheritedFill = new SolidColorBrush(IsDark ? Color.FromArgb(255, 58, 52, 40) : Color.FromArgb(255, 253, 248, 235));
        var emptyFill = SubtleBrush();
        var stroke = BorderBrush();
        var radii = WheelRadii(_radialRingCount);

        for (var ring = 0; ring < radii.Length - 1; ring++)
        {
            for (var index = 0; index < RadialMenuConfig.SlotsPerRing; index++)
            {
                var key = RadialMenuConfig.SlotKey(ring, index);
                menu.Slots.TryGetValue(key, out var ownValue);
                string? inheritedValue = null;
                if (ownValue is null && inherits)
                    config.GlobalMenu.Slots.TryGetValue(key, out inheritedValue);
                var own = config.FindAction(ownValue ?? "");
                var ownFolder = RadialMenuConfig.TryGetSubmenuId(ownValue, out var ownSubmenuId) ? config.FindMenuById(ownSubmenuId) : null;
                var inherited = config.FindAction(inheritedValue ?? "");
                var inheritedFolder = RadialMenuConfig.TryGetSubmenuId(inheritedValue, out var inheritedSubmenuId) ? config.FindMenuById(inheritedSubmenuId) : null;
                var hasOwn = own is not null || ownFolder is not null;
                var hasInherited = inherited is not null || inheritedFolder is not null;
                var r1 = radii[ring];
                var r2 = radii[ring + 1];
                // Computed before the sector so the drag ghost can show the slot's name.
                var label = own?.Name
                            ?? (ownFolder is not null ? "▸ " + RadialMenuTitle(ownFolder) : null)
                            ?? inherited?.Name
                            ?? (inheritedFolder is not null ? "▸ " + RadialMenuTitle(inheritedFolder) : null);
                var sector = new Path
                {
                    Data = WheelSectorGeometry(r1, r2, index),
                    Fill = hasOwn ? ownFill : hasInherited ? inheritedFill : emptyFill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                    AllowDrop = true,
                    Tag = key
                };
                var capturedKey = key;
                // Drag an occupied slot onto another slot to move (or swap) its action.
                // Empty and inherited slots stay drop targets only.
                if (hasOwn)
                {
                    // The drag ghost is a small label chip: snapshotting the sector would drag
                    // the whole pie slice around.
                    SlotDragChip? dragChip = null;
                    // Start the drag only after the pointer moves clearly, so a plain
                    // tap still opens the slot menu and a double tap still opens folders.
                    var dragOrigin = new Point();
                    var dragTracking = false;
                    sector.PointerPressed += (_, e) =>
                    {
                        var point = e.GetCurrentPoint(sector);
                        if (!point.Properties.IsLeftButtonPressed)
                            return;
                        dragOrigin = point.Position;
                        dragTracking = true;
                    };
                    sector.PointerMoved += async (_, e) =>
                    {
                        if (!dragTracking)
                            return;
                        var point = e.GetCurrentPoint(sector);
                        if (!point.Properties.IsLeftButtonPressed)
                        {
                            dragTracking = false;
                            return;
                        }
                        var dx = point.Position.X - dragOrigin.X;
                        var dy = point.Position.Y - dragOrigin.Y;
                        if (dx * dx + dy * dy < 100)
                            return;
                        dragTracking = false;
                        if (!string.IsNullOrWhiteSpace(label))
                            dragChip = await TryRenderDragChipAsync(label);
                        try
                        {
                            await sector.StartDragAsync(point);
                        }
                        catch
                        {
                            // The pointer was released mid-gesture; nothing to drag.
                        }
                        if (dragChip is not null)
                        {
                            try
                            {
                                dragChip.Bitmap.Dispose();
                            }
                            catch
                            {
                                // The drag surface may still hold the bitmap; the finalizer will get it.
                            }
                            dragChip = null;
                        }
                    };
                    sector.PointerReleased += (_, _) => dragTracking = false;
                    sector.PointerCaptureLost += (_, _) => dragTracking = false;
                    sector.DragStarting += (_, e) =>
                    {
                        e.Data.SetText(SlotDragPrefix + capturedKey);
                        e.Data.Properties[SlotDragKeyProperty] = capturedKey;
                        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
                        if (dragChip is not null && e.DragUI is not null)
                            e.DragUI.SetContentFromSoftwareBitmap(dragChip.Bitmap, dragChip.Anchor);
                    };
                }
                sector.Tapped += (_, e) => ShowRadialSlotMenu(sector, capturedKey, e.GetPosition(sector));
                sector.RightTapped += (_, e) => ShowRadialSlotMenu(sector, capturedKey, e.GetPosition(sector));
                var openFolder = ownFolder ?? inheritedFolder;
                if (openFolder is not null)
                {
                    // Double-click a folder slot to edit its submenu.
                    sector.DoubleTapped += (_, _) =>
                    {
                        _radialSubmenuPath.Add(openFolder.Id);
                        RefreshRadialWheel();
                    };
                }
                sector.PointerEntered += (_, _) => sector.Stroke = accent;
                sector.PointerExited += (_, _) => sector.Stroke = stroke;
                sector.DragOver += (_, e) =>
                {
                    // A slot dragged from another slot shows the move cursor.
                    if (e.DataView.Properties.ContainsKey(SlotDragKeyProperty))
                    {
                        e.AcceptedOperation = DataPackageOperation.Move;
                        return;
                    }
                    if (e.DataView.Contains(StandardDataFormats.Text))
                        e.AcceptedOperation = DataPackageOperation.Copy;
                };
                sector.Drop += async (_, e) =>
                {
                    // Slot-to-slot drag: move the action, swapping when the target is occupied.
                    if (e.DataView.Properties.ContainsKey(SlotDragKeyProperty) &&
                        e.DataView.Properties[SlotDragKeyProperty] is string sourceKey)
                    {
                        DropRadialSlotOnSlot(menu, sourceKey, capturedKey, config);
                        return;
                    }
                    if (!e.DataView.Contains(StandardDataFormats.Text)) return;
                    var payload = await e.DataView.GetTextAsync();
                    // Fallback for slot drags whose custom property did not survive the drag.
                    if (payload.StartsWith(SlotDragPrefix, StringComparison.Ordinal))
                    {
                        DropRadialSlotOnSlot(menu, payload.Substring(SlotDragPrefix.Length), capturedKey, config);
                        return;
                    }
                    if (config.FindAction(payload) is null) return;
                    menu.Slots[capturedKey] = payload;
                    SaveRadialConfig();
                    RefreshRadialWheel();
                };
                canvas.Children.Add(sector);

                if (string.IsNullOrWhiteSpace(label))
                    continue;
                var mid = WheelPolar((r1 + r2) / 2, index);
                var width = ring == 0 ? 76 : ring == 1 ? 88 : 100;
                var text = new TextBlock
                {
                    Text = label,
                    Width = width,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    FontSize = 13,
                    Opacity = hasOwn ? 1 : 0.55,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(text, mid.X - width / 2.0);
                Canvas.SetTop(text, mid.Y - 10);
                canvas.Children.Add(text);
            }
        }
        var center = new Ellipse { Width = WheelDeadZone * 2, Height = WheelDeadZone * 2, Fill = SubtleBrush(), Stroke = stroke, StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(center, WheelSize / 2 - WheelDeadZone);
        Canvas.SetTop(center, WheelSize / 2 - WheelDeadZone);
        canvas.Children.Add(center);
        // Shrink (never enlarge) the wheel to the column width, so a narrow
        // window does not push it under the action library.
        panel.Children.Add(new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = WheelSize,
            MaxHeight = WheelSize
        });

        if (inherits)
            panel.Children.Add(new TextBlock { Text = T("空格子沿用全局轮盘的动作。", "Empty slots use the global wheel."), Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center });

        var copy = NewPillButton(L("复制", "Copy", "複製", "복사"), false);
        copy.Click += (_, _) => _radialCopiedSlots = new Dictionary<string, string>(menu.Slots, StringComparer.OrdinalIgnoreCase);
        var paste = NewPillButton(L("粘贴", "Paste", "貼上", "붙여넣기"), false);
        paste.Click += (_, _) =>
        {
            if (_radialCopiedSlots is null) return;
            menu.Slots = new Dictionary<string, string>(_radialCopiedSlots, StringComparer.OrdinalIgnoreCase);
            SaveRadialConfig();
            RefreshRadialWheel();
        };
        var clear = NewPillButton(L("清除", "Clear", "清除", "지우기"), false);
        clear.Click += async (_, _) => await RunUiActionAsync(async () =>
        {
            if (menu.Slots.Count == 0) return;
            if (!await ConfirmDialogAsync(L("清除", "Clear", "清除", "지우기"), RadialMenuTitle(menu), L("清除", "Clear", "清除", "지우기")))
                return;
            menu.Slots.Clear();
            PruneOrphanSubmenus(config);
            SaveRadialConfig();
            RefreshRadialWheel();
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(copy);
        buttons.Children.Add(paste);
        buttons.Children.Add(clear);
        panel.Children.Add(buttons);

        _radialWheelHost.Children.Clear();
        _radialWheelHost.Children.Add(panel);
    }

    /// <summary>Applies a slot-to-slot drag: moves the action, or swaps when the target is occupied.</summary>
    private void DropRadialSlotOnSlot(RadialMenuDefinition menu, string sourceKey, string targetKey, RadialMenuConfig config)
    {
        if (!config.MoveSlot(menu, sourceKey, targetKey))
            return;
        PruneOrphanSubmenus(config);
        SaveRadialConfig();
        RefreshRadialWheel();
    }

    /// <summary>A drag ghost: the rendered label chip plus the point that follows the pointer.</summary>
    private sealed record SlotDragChip(SoftwareBitmap Bitmap, Point Anchor);

    /// <summary>
    /// Renders a small rounded chip holding the slot's name, used as the drag visual so moving a
    /// slot does not drag the whole sector geometry around.
    /// </summary>
    private async Task<SlotDragChip?> TryRenderDragChipAsync(string label)
    {
        Brush? background = HighContrast
            ? SystemBrush("ApplicationPageBackgroundThemeBrush")
            : IsDark ? new SolidColorBrush(Color.FromArgb(255, 44, 44, 44)) : new SolidColorBrush(Color.FromArgb(255, 250, 252, 255));
        Brush? foreground = HighContrast
            ? null
            : IsDark ? new SolidColorBrush(Color.FromArgb(255, 244, 244, 244)) : new SolidColorBrush(Color.FromArgb(255, 28, 28, 28));
        var chip = new Border
        {
            Background = background,
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 7, 14, 7),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 13,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = foreground
            }
        };
        // RenderTargetBitmap only captures elements that live in the visual tree, so park the
        // chip just outside the window, lay it out, snapshot it, then drop it again.
        var stage = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
        Canvas.SetLeft(chip, -4000);
        Canvas.SetTop(chip, -4000);
        stage.Children.Add(chip);
        Root.Children.Add(stage);
        try
        {
            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            chip.Arrange(new Rect(0, 0, chip.DesiredSize.Width, chip.DesiredSize.Height));
            chip.UpdateLayout();
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(chip);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                return null;
            var pixels = await bitmap.GetPixelsAsync();
            var software = SoftwareBitmap.CreateCopyFromBuffer(
                pixels, BitmapPixelFormat.Bgra8, bitmap.PixelWidth, bitmap.PixelHeight, BitmapAlphaMode.Premultiplied);
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            if (scale <= 0)
                scale = 1;
            // Centre the chip on the pointer.
            return new SlotDragChip(software, new Point(bitmap.PixelWidth / scale / 2, bitmap.PixelHeight / scale / 2));
        }
        catch
        {
            // Fall back to the default drag visual rather than failing the drag.
            return null;
        }
        finally
        {
            Root.Children.Remove(stage);
        }
    }

    private static Point WheelPolar(double radius, int index, double angleOffset = 0)
    {
        var angle = index * 2 * Math.PI / RadialMenuConfig.SlotsPerRing + angleOffset;
        return new Point(WheelSize / 2 + radius * Math.Sin(angle), WheelSize / 2 - radius * Math.Cos(angle));
    }

    private static Geometry WheelSectorGeometry(double r1, double r2, int index)
    {
        var half = Math.PI / RadialMenuConfig.SlotsPerRing;
        var figure = new PathFigure { StartPoint = WheelPolar(r1, index, -half), IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = WheelPolar(r2, index, -half) });
        figure.Segments.Add(new ArcSegment { Point = WheelPolar(r2, index, half), Size = new Size(r2, r2), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = WheelPolar(r1, index, half) });
        figure.Segments.Add(new ArcSegment { Point = WheelPolar(r1, index, -half), Size = new Size(r1, r1), SweepDirection = SweepDirection.Counterclockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private async Task<string?> AskRadialSubmenuNameAsync(string title, string current)
    {
        var box = new TextBox { Header = L("名称", "Name", "名稱", "이름"), Text = current };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        if (!await ConfirmDialogAsync(title, box, L("保存", "Save", "儲存", "저장")))
            return null;
        return string.IsNullOrWhiteSpace(box.Text) ? current : box.Text.Trim();
    }

    private void ShowRadialSlotMenu(FrameworkElement target, string slotKey, Point position)
    {
        var config = _radialConfig!;
        var menu = SelectedRadialMenu();
        menu.Slots.TryGetValue(slotKey, out var ownValue);
        var own = config.FindAction(ownValue ?? "");
        var ownFolder = RadialMenuConfig.TryGetSubmenuId(ownValue, out var ownSubmenuId) ? config.FindMenuById(ownSubmenuId) : null;
        bool CanPaste()
        {
            var value = _radialCopiedActionId ?? "";
            if (RadialMenuConfig.TryGetSubmenuId(value, out var copiedSubmenu))
                return config.FindMenuById(copiedSubmenu) is not null && !string.Equals(copiedSubmenu, menu.Id, StringComparison.OrdinalIgnoreCase);
            return config.FindAction(value) is not null;
        }

        var flyout = new MenuFlyout();
        void Add(string text, Action onClick, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => onClick();
            flyout.Items.Add(item);
        }

        Add(L("选择", "Select", "選擇", "선택"), () => _ = RunUiActionAsync(async () =>
        {
            var id = await PickRadialLibraryActionAsync();
            if (id is null) return;
            menu.Slots[slotKey] = id;
            SaveRadialConfig();
            RefreshRadialWheel();
        }), config.Actions.Count > 0);
        Add(T("新建动作", "New action"), () => _ = RunUiActionAsync(async () =>
        {
            var created = await EditRadialActionAsync(null);
            if (created is null) return;
            menu.Slots[slotKey] = created.Id;
            SaveRadialConfig();
            RefreshRadialWheel();
            RefreshRadialLibrary();
        }));
        Add(L("编辑", "Edit", "編輯", "편집"), () => _ = RunUiActionAsync(async () =>
        {
            if (own is null || await EditRadialActionAsync(own) is null) return;
            SaveRadialConfig();
            RefreshRadialWheel();
            RefreshRadialLibrary();
        }), own is not null);
        flyout.Items.Add(new MenuFlyoutSeparator());
        if (ownFolder is not null)
        {
            Add(T("打开子菜单", "Open submenu"), () =>
            {
                _radialSubmenuPath.Add(ownFolder.Id);
                RefreshRadialWheel();
            });
            Add(T("重命名子菜单", "Rename submenu"), () => _ = RunUiActionAsync(async () =>
            {
                var newName = await AskRadialSubmenuNameAsync(T("重命名子菜单", "Rename submenu"), RadialMenuTitle(ownFolder));
                if (newName is null) return;
                ownFolder.Name = newName;
                SaveRadialConfig();
                RefreshRadialWheel();
            }));
        }
        else
        {
            Add(T("新建子菜单", "New submenu"), () => _ = RunUiActionAsync(async () =>
            {
                var newName = await AskRadialSubmenuNameAsync(T("新建子菜单", "New submenu"), T("子菜单", "Submenu"));
                if (newName is null) return;
                var submenu = new RadialMenuDefinition { Id = RadialMenuConfig.NewId(), Scope = RadialMenuScope.Submenu, Name = newName };
                config.Menus.Add(submenu);
                menu.Slots[slotKey] = RadialMenuConfig.SubmenuRef(submenu.Id);
                SaveRadialConfig();
                _radialSubmenuPath.Add(submenu.Id);
                RefreshRadialWheel();
            }));
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        // Copying a folder copies a reference: both slots open the same submenu.
        Add(L("复制", "Copy", "複製", "복사"), () => _radialCopiedActionId = ownValue, own is not null || ownFolder is not null);
        Add(L("粘贴", "Paste", "貼上", "붙여넣기"), () =>
        {
            if (!CanPaste()) return;
            menu.Slots[slotKey] = _radialCopiedActionId!;
            PruneOrphanSubmenus(config);
            SaveRadialConfig();
            RefreshRadialWheel();
        }, CanPaste());
        Add(L("清除", "Clear", "清除", "지우기"), () =>
        {
            menu.Slots.Remove(slotKey);
            PruneOrphanSubmenus(config);
            SaveRadialConfig();
            RefreshRadialWheel();
        }, own is not null || ownFolder is not null);

        flyout.ShowAt(target, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
    }

    // ----- action library (right) ---------------------------------------

    private void RefreshRadialLibrary()
    {
        if (_radialLibraryHost is null || _radialConfig is null)
            return;
        var config = _radialConfig;
        var filter = new TextBox { PlaceholderText = T("筛选", "Filter"), Text = _radialLibraryFilter };
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true,
            MaxHeight = 460
        };

        void Fill()
        {
            list.Items.Clear();
            var query = filter.Text.Trim();
            foreach (var action in config.Actions
                         .Where(a => query.Length == 0 || (a.Name ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = new StackPanel { Tag = action.Id, Padding = new Thickness(0, 4, 0, 4), Opacity = action.IsEnabled ? 1 : 0.5 };
                row.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(action.Name) ? "?" : action.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                var command = action.Commands.FirstOrDefault();
                if (command is not null)
                    row.Children.Add(new TextBlock { Text = PluginName(command.PluginClass ?? ""), FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
                row.DoubleTapped += async (_, _) => await RunUiActionAsync(async () =>
                {
                    if (await EditRadialActionAsync(action) is null) return;
                    SaveRadialConfig();
                    RefreshRadialWheel();
                    RefreshRadialLibrary();
                });
                list.Items.Add(row);
            }
        }

        filter.TextChanged += (_, _) =>
        {
            _radialLibraryFilter = filter.Text;
            Fill();
        };
        list.DragItemsStarting += (_, e) =>
        {
            if (e.Items.FirstOrDefault() is FrameworkElement { Tag: string id })
            {
                e.Data.SetText(id);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            else
            {
                e.Cancel = true;
            }
        };
        Fill();

        string? SelectedActionId() => list.SelectedItem is FrameworkElement { Tag: string id } ? id : null;

        var add = NewPillButton(L("添加", "Add", "新增", "추가"), false);
        add.Click += async (_, _) => await RunUiActionAsync(async () =>
        {
            if (await EditRadialActionAsync(null) is null) return;
            SaveRadialConfig();
            RefreshRadialLibrary();
        });
        var edit = NewPillButton(L("编辑", "Edit", "編輯", "편집"), false);
        edit.Click += async (_, _) => await RunUiActionAsync(async () =>
        {
            var action = config.FindAction(SelectedActionId() ?? "");
            if (action is null || await EditRadialActionAsync(action) is null) return;
            SaveRadialConfig();
            RefreshRadialWheel();
            RefreshRadialLibrary();
        });
        var delete = NewPillButton(L("删除", "Delete", "刪除", "삭제"), false);
        delete.Click += async (_, _) => await RunUiActionAsync(async () =>
        {
            var action = config.FindAction(SelectedActionId() ?? "");
            if (action is null) return;
            if (!await ConfirmDialogAsync(DeleteConfirmationTitle(),
                    F("确定删除动作 {0}？", "Delete action {0}?", action.Name), DeleteButtonText()))
                return;
            config.Actions.Remove(action);
            config.Normalize(); // drops slot references to the deleted action
            SaveRadialConfig();
            RefreshRadialWheel();
            RefreshRadialLibrary();
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(add);
        buttons.Children.Add(edit);
        buttons.Children.Add(delete);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = T("动作库", "Action library"), Style = BodyStrongTextBlockStyle });
        panel.Children.Add(filter);
        panel.Children.Add(list);
        panel.Children.Add(buttons);
        _radialLibraryHost.Children.Clear();
        _radialLibraryHost.Children.Add(panel);
    }

    private async Task<string?> PickRadialLibraryActionAsync()
    {
        var config = _radialConfig!;
        var filter = new TextBox { PlaceholderText = T("筛选", "Filter") };
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        void Fill()
        {
            list.Items.Clear();
            var query = filter.Text.Trim();
            foreach (var action in config.Actions
                         .Where(a => query.Length == 0 || (a.Name ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
                list.Items.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(action.Name) ? "?" : action.Name, Tag = action.Id });
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(filter);
        panel.Children.Add(list);
        if (!await ConfirmDialogAsync(L("选择", "Select", "選擇", "선택"), panel, L("确定", "OK", "確定", "확인"), () => list.SelectedItem is not null))
            return null;
        return list.SelectedItem is FrameworkElement { Tag: string id } ? id : null;
    }
}
