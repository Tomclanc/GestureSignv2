using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace GestureSign.PieMenu;

internal sealed class MenuWindow : Window
{
    private const double DesiredSize = 350;
    private const double DeadZone = 50;
    private const int ParentIndex = -2;
    private readonly Grid _root = new() { AllowFocusOnInteraction = true, IsTabStop = true };
    private readonly Canvas _surface = new() { IsHitTestVisible = false };
    private readonly TextBlock _centerText = new()
    {
        FontSize = 29, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 84
    };
    private readonly Stack<(string Name, string Glyph, IReadOnlyList<PieItem> Items, double X, double Y, double? ParentAngle)> _parents = new();
    private readonly List<Border> _nodes = [];
    private readonly List<Point> _positions = [];
    private readonly List<(Point Point, double Radius)> _details = [];
    private readonly PieMenu _menu;
    private readonly MenuDocument _document;
    private readonly Func<PieItem, nint, Task> _execute;
    private readonly nint _previousWindow;
    private IReadOnlyList<PieItem> _items;
    private double[] _angles = [];
    private double _cx, _cy, _diameter, _scale;
    private double _radius;
    private double? _parentAngle;
    private Point? _parentPosition;
    private Point? _strokeStart;
    private Point _strokeEnd;
    private int _strokeEpoch;
    private Line? _connector;
    private Border? _currentParent;
    private int _active = -1;
    private int _hoverEpoch;
    private bool _pressed, _enteredDuringPress, _closing;
    private string _page;
    private string _glyph;
    private readonly Color _accent;
    private readonly Color _panel;
    private readonly Color _idle;
    private readonly Color _ink;

    internal MenuWindow(PieMenu menu, MenuDocument document, Func<PieItem, nint, Task> execute, bool preview)
    {
        _menu = menu; _document = document; _execute = execute;
        _items = menu.Items; _page = menu.Name; _glyph = menu.Glyph;
        _previousWindow = Desktop.GetForegroundWindow();
        // Geometry and default colors follow Kando's CC0 default theme.
        // https://github.com/kando-menu/kando/tree/6df468a66695ac504afebfff2c581a42296511b4/assets/menu-themes/default
        (_panel, _accent, _idle, _ink) = document.Palette switch
        {
            "Dark Night" or "Midnight" => (Rgb(36, 39, 47), Rgb(82, 64, 87), Rgb(62, 21, 39), Rgb(255, 255, 255)),
            "Blue Glass" => (Rgb(225, 239, 255), Rgb(103, 134, 168), Rgb(171, 215, 255), Rgb(39, 57, 81)),
            _ => (Rgb(255, 255, 255), Rgb(109, 109, 109), Rgb(255, 200, 200), Rgb(60, 60, 60))
        };
        Title = "GestureSign · Pie Menu";
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = preview;
        _root.Background = Brush(_accent);
        _root.Children.Add(_surface);
        _centerText.Foreground = Brush(_ink);
        Content = _root;
        _root.Loaded += (_, _) => { ApplyShape(); _root.Focus(FocusState.Programmatic); };
        _root.PointerMoved += MovePointer;
        _root.PointerPressed += PressPointer;
        _root.PointerReleased += ReleasePointer;
        _root.PointerCaptureLost += (_, _) => { _pressed = false; EndStroke(); };
        _root.PointerExited += (_, _) => { if (!_pressed) SetActive(-1); };
        _root.KeyDown += KeyPressed;
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !_closing)
                _ = Dismiss(restore: false);
        };
        Position();
        Draw();
    }

    private void Position()
    {
        Desktop.GetCursorPos(out var cursor);
        var (area, scale) = Desktop.MonitorAt(cursor);
        _scale = scale;
        var size = Math.Max(1, Math.Min((int)Math.Round(DesiredSize * scale), Math.Min(area.Width, area.Height)));
        _diameter = size / scale;
        _cx = _cy = _diameter / 2;
        var targetX = _menu.FixedX is { } x && double.IsFinite(x) ? area.X + Math.Clamp(x, 0, 1) * area.Width : cursor.X;
        var targetY = _menu.FixedY is { } y && double.IsFinite(y) ? area.Y + Math.Clamp(y, 0, 1) * area.Height : cursor.Y;
        var left = Math.Clamp((int)Math.Round(targetX - size / 2d), area.X, area.X + area.Width - size);
        var top = Math.Clamp((int)Math.Round(targetY - size / 2d), area.Y, area.Y + area.Height - size);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, size, size));
    }

    private void Draw()
    {
        _surface.Children.Clear();
        _nodes.Clear();
        _positions.Clear();
        _details.Clear();
        _active = -1;
        _hoverEpoch++;
        _angles = PieLayout.Arrange(_items, _parentAngle);
        _radius = Math.Max(55, Math.Min(Math.Max(100, 10 * _items.Count), _diameter / 2 - 45));
        _connector = new Line { Stroke = Brush(_idle), StrokeThickness = 10, Visibility = Visibility.Collapsed };
        _surface.Children.Add(_connector);
        _currentParent = null;
        _parentPosition = null;
        if (_parentAngle is { } parentAngle && _parents.TryPeek(out var parent))
        {
            _parentPosition = RadialPoint(parentAngle, _radius);
            _currentParent = new Border
            {
                Width = 50, Height = 50, CornerRadius = new CornerRadius(25),
                Background = Brush(_panel), BorderBrush = Brush(_accent), BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = parent.Glyph, FontSize = 23, FontFamily = IconFont(parent.Glyph), Foreground = Brush(_ink),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Place(_currentParent, _parentPosition.Value.X - 25, _parentPosition.Value.Y - 25);
        }
        var side = _items.Count > 10 ? Math.Min(50, 550d / _items.Count) : 50;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var theta = _angles[i] * Math.PI / 180;
            var x = _cx + Math.Sin(theta) * _radius;
            var y = _cy - Math.Cos(theta) * _radius;
            _positions.Add(new Point(x, y));
            if (item.Children.Count > 0)
            {
                var count = Math.Min(6, item.Children.Count);
                for (var tip = 0; tip < count; tip++)
                {
                    var direction = theta + (tip - (count - 1) / 2d) * .28;
                    var dot = new Point(x + Math.Sin(direction) * 26, y - Math.Cos(direction) * 26);
                    var marker = new Ellipse { Width = 15, Height = 15, Fill = Brush(_accent) };
                    Place(marker, dot.X - 7.5, dot.Y - 7.5);
                    _details.Add((dot, 7.5));
                }
            }
            var tile = new Border
            {
                Width = side, Height = side, CornerRadius = new CornerRadius(side / 2),
                Background = Brush(_panel), BorderBrush = Brush(_accent), BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = item.Glyph, FontSize = 23, Foreground = Brush(_ink),
                    FontFamily = IconFont(item.Glyph),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, item.Name);
            Place(tile, x - side / 2d, y - side / 2d);
            _nodes.Add(tile);
            if (_document.Animations) Animate(tile, .70f, 1, Math.Min(i, 8) * 10, fade: true);
            if (item.Key.Length > 0)
            {
                var keyPosition = new Point(x - Math.Sin(theta) * 26, y + Math.Cos(theta) * 26);
                var badge = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                    Background = Brush(_idle), BorderBrush = Brush(_accent), BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = item.Key[..Math.Min(2, item.Key.Length)], FontSize = 12, Foreground = Brush(_ink),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Place(badge, keyPosition.X - 10, keyPosition.Y - 10);
                _details.Add((keyPosition, 10));
            }
        }
        if (_centerText.Parent is Border oldCenter) oldCenter.Child = null;
        _centerText.Text = _glyph;
        _centerText.FontFamily = IconFont(_glyph);
        _centerText.FontSize = 32;
        var center = new Border
        {
            Width = 100, Height = 100, CornerRadius = new CornerRadius(50), Background = Brush(_panel),
            BorderBrush = Brush(_accent), BorderThickness = new Thickness(1), Child = _centerText
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(center, _page);
        Place(center, _cx - 50, _cy - 50);
        if (_document.Animations) Animate(center, .82f, 1, 0, fade: true);
        ApplyShape();
    }

    private void ApplyShape()
    {
        var center = new Point(_cx, _cy);
        IEnumerable<(Point, double)> circles = _positions.Select((p, i) => (p, _nodes[i].Width / 2)).Append((center, 50d)).Concat(_details);
        if (_parentPosition is { } parent) circles = circles.Append((parent, 25d));
        var target = _active >= 0 ? _positions[_active] : _active == ParentIndex ? _parentPosition : null;
        var links = target is { } end ? new[] { (center, end, 10d) } : [];
        Desktop.ShapeMenu(WinRT.Interop.WindowNative.GetWindowHandle(this), _scale, circles, links);
    }

    private Point RadialPoint(double angle, double radius)
    {
        var radians = angle * Math.PI / 180;
        return new Point(_cx + Math.Sin(radians) * radius, _cy - Math.Cos(radians) * radius);
    }

    private static FontFamily IconFont(string glyph) => new(glyph.Any(c => c >= '\uE000' && c <= '\uF8FF')
        ? "Segoe Fluent Icons" : "Segoe UI");

    private void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y); _surface.Children.Add(element);
    }

    private static void Animate(FrameworkElement element, float from, float to, int delay, bool fade)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.Width / 2, (float)element.Height / 2, 0);
        var duration = TimeSpan.FromMilliseconds(fade ? 250 : 145);
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.775f, 1.325f), new Vector2(.535f, 1));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(from, from, 1));
        scale.InsertKeyFrame(1, new Vector3(to, to, 1), ease);
        scale.Duration = duration;
        scale.DelayTime = TimeSpan.FromMilliseconds(delay);
        visual.StartAnimation("Scale", scale);
        if (!fade) return;
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1, ease);
        opacity.Duration = duration;
        opacity.DelayTime = scale.DelayTime;
        visual.StartAnimation("Opacity", opacity);
    }

    private int Hit(Point p, bool directional)
    {
        var dxCenter = p.X - _cx;
        var dyCenter = p.Y - _cy;
        if (directional)
        {
            if (dxCenter * dxCenter + dyCenter * dyCenter < DeadZone * DeadZone) return -1;
            var index = PieLayout.Hit(dxCenter, dyCenter, _angles, DeadZone);
            if (_parentAngle is { } parent)
            {
                var direction = PieLayout.Normalize(Math.Atan2(dxCenter, -dyCenter) * 180 / Math.PI);
                if (index < 0 || PieLayout.Distance(direction, parent) < PieLayout.Distance(direction, _angles[index]))
                    return ParentIndex;
            }
            return index;
        }
        if (_parentPosition is { } back && SquaredDistance(p, back) <= 25 * 25) return ParentIndex;
        for (var i = 0; i < _nodes.Count; i++)
        {
            var node = _positions[i];
            var dx = p.X - node.X;
            var dy = p.Y - node.Y;
            if (dx * dx + dy * dy <= Math.Pow(_nodes[i].Width / 2, 2))
                return i;
        }
        return -1;
    }

    private void PressPointer(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(_root).Properties.IsRightButtonPressed) { Back(); return; }
        _pressed = true;
        _enteredDuringPress = false;
        EndStroke(e.GetCurrentPoint(_root).Position);
        _root.CapturePointer(e.Pointer);
    }

    private void MovePointer(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_root).Position;
        var index = Hit(p, directional: true);
        SetActive(index);
        if (!_document.Marking || !_pressed) return;
        var start = _strokeStart ?? p;
        _strokeStart ??= p;
        var length = Math.Sqrt(SquaredDistance(start, _strokeEnd));
        var step = Math.Sqrt(SquaredDistance(p, _strokeEnd));
        if (length > 150 && step > 10)
        {
            var dot = ((p.X - _strokeEnd.X) * (_strokeEnd.X - start.X) + (p.Y - _strokeEnd.Y) * (_strokeEnd.Y - start.Y)) / (step * length);
            if (Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI > 20 &&
                TryMarkSubmenu(_strokeEnd)) return;
        }
        if (step <= 10) return;
        _strokeEnd = p;
        var epoch = ++_strokeEpoch;
        if (SquaredDistance(p, start) > 150 * 150) _ = MarkAfterPause(epoch, p);
    }

    private async void ReleasePointer(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        var p = e.GetCurrentPoint(_root).Position;
        var index = Hit(p, _document.Marking);
        _pressed = false;
        EndStroke();
        _root.ReleasePointerCapture(e.Pointer);
        if (_enteredDuringPress && index == -1) return;
        if (index >= 0) await Choose(index);
        else Back();
    }

    // Kando's marking detector selects intermediate nodes after a sharp turn or pause.
    private async Task MarkAfterPause(int epoch, Point p)
    {
        await Task.Delay(100);
        if (_pressed && !_closing && epoch == _strokeEpoch) TryMarkSubmenu(p);
    }

    private bool TryMarkSubmenu(Point p)
    {
        var index = Hit(p, directional: true);
        if (index < 0 || !(_items[index].Submenu || _items[index].Children.Count > 0)) return false;
        _enteredDuringPress = true;
        EndStroke(p);
        _ = Choose(index, p);
        return true;
    }

    private void EndStroke(Point? p = null)
    {
        _strokeEpoch++;
        _strokeStart = p;
        _strokeEnd = p ?? default;
    }

    private static double SquaredDistance(Point first, Point second) =>
        Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2);

    private void SetActive(int index)
    {
        if (index == _active) return;
        if (_active >= 0) Highlight(_active, false);
        _active = index;
        var epoch = ++_hoverEpoch;
        _centerText.Text = index >= 0 ? _items[index].Name
            : index == ParentIndex && _parents.TryPeek(out var parent) ? parent.Name : _glyph;
        _centerText.FontFamily = IconFont(_centerText.Text);
        _centerText.FontSize = index == -1 ? 32 : 16;
        if (_currentParent is not null) _currentParent.Background = Brush(index == ParentIndex ? _idle : _panel);
        if (_connector is not null)
        {
            var target = index >= 0 ? _positions[index] : index == ParentIndex ? _parentPosition : null;
            _connector.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
            if (target is { } p)
            {
                _connector.X1 = _cx; _connector.Y1 = _cy; _connector.X2 = p.X; _connector.Y2 = p.Y;
            }
        }
        ApplyShape();
        if (index < 0) return;
        Highlight(index, true);
        if (_menu.Hover && !_pressed) _ = HoverSelect(epoch, index);
    }

    private async Task HoverSelect(int epoch, int index)
    {
        await Task.Delay(430);
        // Pointer-only selection is limited to navigation. Actions require an explicit selection.
        if (!_closing && !_pressed && epoch == _hoverEpoch && _items[index].Children.Count > 0)
            await Choose(index);
    }

    private void Highlight(int index, bool on)
    {
        var tile = _nodes[index];
        tile.Background = Brush(on ? _idle : _panel);
        if (_document.Animations) Animate(tile, on ? 1 : 1.11f, on ? 1.11f : 1, 0, fade: false);
    }

    private async void KeyPressed(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (e.Key == VirtualKey.Escape) { await Dismiss(); return; }
        if (e.Key == VirtualKey.Back) { Back(); return; }
        if (_items.Count == 0) return;
        if (e.Key is VirtualKey.Right or VirtualKey.Down or VirtualKey.Tab)
            SetActive((_active + 1) % _items.Count);
        else if (e.Key is VirtualKey.Left or VirtualKey.Up)
            SetActive((_active < 0 ? 0 : _active - 1 + _items.Count) % _items.Count);
        else if (e.Key is VirtualKey.Enter or VirtualKey.Space)
            await Choose(_active < 0 ? 0 : _active);
        else
        {
            var name = e.Key.ToString();
            var index = _items.ToList().FindIndex(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) await Choose(index);
            else e.Handled = false;
        }
    }

    private async Task Choose(int index, Point? drag = null)
    {
        if (_closing || index < 0 || index >= _items.Count) return;
        var item = _items[index];
        if (item.Submenu || item.Children.Count > 0)
        {
            _parents.Push((_page, _glyph, _items, _cx, _cy, _parentAngle));
            _parentAngle = PieLayout.Normalize(_angles[index] + 180);
            if (!_menu.Anchored && drag is { } point)
            {
                var margin = Math.Min(136, _diameter / 2);
                _cx = Math.Clamp(point.X, margin, _diameter - margin);
                _cy = Math.Clamp(point.Y, margin, _diameter - margin);
            }
            _page = item.Name;
            _glyph = item.Glyph;
            _items = item.Children;
            Draw();
            return;
        }
        if (item.Actions.All(a => a.Type == "close-submenu") && item.Actions.Count > 0)
        {
            Back();
            return;
        }
        await Dismiss();
        await _execute(item, _previousWindow);
    }

    private void Back()
    {
        if (!_parents.TryPop(out var page)) { _ = Dismiss(); return; }
        (_page, _glyph, _items, _cx, _cy, _parentAngle) = page;
        Draw();
    }

    private async Task Dismiss(bool restore = true)
    {
        if (_closing) return;
        _closing = true;
        if (restore && _document.Animations)
        {
            foreach (var node in _nodes) Animate(node, 1, .64f, 0, fade: false);
            await Task.Delay(100);
        }
        Close();
        if (restore && _previousWindow != 0) Desktop.SetForegroundWindow(_previousWindow);
    }

    private static SolidColorBrush Brush(Color color) => new(color);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
