using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace GestureSign.WinUI;

/// <summary>
/// Shows an action's icon in the settings: the picture of an image file, the icon
/// of the program, shortcut or document its commands mention, or the plugin's
/// glyph when there is neither. Icons are read through <see cref="ActionIcons"/>
/// and cached per file, so the same program is only read once.
/// </summary>
internal static class ActionIconPresenter
{
    private const string SymbolFont = "Segoe Fluent Icons,Segoe MDL2 Assets";

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon for a library action; falls back to the first command's plugin glyph.</summary>
    public static FrameworkElement For(RadialMenuAction? action, double size, Brush? glyphBrush = null)
        => For(ActionIcons.Resolve(action), FirstPluginClass(action), size, glyphBrush);

    /// <summary>Icon for a path: an image, program, shortcut or document (null shows the plugin glyph).</summary>
    public static FrameworkElement ForPath(string? path, double size, string? pluginClass = null, Brush? glyphBrush = null)
        => For(ActionIcons.ForPath(path), pluginClass, size, glyphBrush);

    public static FrameworkElement For(ActionIconSource? source, string? pluginClass, double size, Brush? glyphBrush = null)
    {
        switch (source?.Kind)
        {
            case ActionIconKind.Glyph:
                return GlyphVisual(source.Value, size, glyphBrush);
            case ActionIconKind.Image:
                return Picture(source, size, pluginClass);
            case ActionIconKind.Program:
                return Picture(source, size, pluginClass);
            default:
                return GlyphVisual(ActionIcons.PluginGlyph(pluginClass), size, glyphBrush);
        }
    }

    /// <summary>The plugin class of the first command; decides the glyph an iconless action shows.</summary>
    public static string? FirstPluginClass(RadialMenuAction? action)
        => action?.Commands is { Count: > 0 } commands ? commands[0]?.PluginClass : null;

    /// <summary>A one-line list row: the icon plus the action name, tagged with the action id.</summary>
    public static Grid Row(RadialMenuAction action, Brush? glyphBrush = null)
    {
        var row = new Grid { Tag = action.Id, ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = For(action, 18, glyphBrush);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(action.Name) ? "?" : action.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static FontIcon GlyphVisual(string glyph, double size, Brush? brush)
    {
        var icon = new FontIcon
        {
            Glyph = string.IsNullOrEmpty(glyph) ? "\uE945" : glyph,
            FontSize = size,
            FontFamily = new FontFamily(SymbolFont)
        };
        if (brush is not null)
            icon.Foreground = brush;
        return icon;
    }

    private static Image Picture(ActionIconSource source, double size, string? pluginClass)
    {
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = null
        };
        _ = LoadAsync(image, source, size, pluginClass);
        return image;
    }

    private static async Task LoadAsync(Image target, ActionIconSource source, double size, string? pluginClass)
    {
        var key = source.CacheKey;
        if (Cache.TryGetValue(key, out var cached))
        {
            target.Source = cached;
            return;
        }
        try
        {
            var image = source.Kind == ActionIconKind.Image
                ? await FromFileAsync(source.Value)
                : await FromPixelsAsync(source);
            if (image is null)
            {
                ShowFallbackGlyph(target, size, pluginClass);
                return;
            }
            Cache[key] = image;
            target.Source = image;
        }
        catch
        {
            // A file may disappear or be locked: show the plugin glyph instead of a hole.
            ShowFallbackGlyph(target, size, pluginClass);
        }
    }

    /// <summary>Replaces a failed picture with the plugin glyph, so a slot never looks empty.</summary>
    private static void ShowFallbackGlyph(Image target, double size, string? pluginClass)
    {
        var glyph = GlyphVisual(ActionIcons.PluginGlyph(pluginClass), size * 0.88, null);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        try
        {
            switch (target.Parent)
            {
                case Panel panel:
                    var index = panel.Children.IndexOf(target);
                    if (index < 0)
                        return;
                    panel.Children.RemoveAt(index);
                    panel.Children.Insert(index, glyph);
                    break;
                case Border border:
                    border.Child = glyph;
                    break;
                case ContentControl content:
                    content.Content = glyph;
                    break;
            }
        }
        catch
        {
            // The row may already be gone; nothing to replace.
        }
    }

    private static async Task<ImageSource?> FromFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length == 0)
            return null;
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static async Task<ImageSource?> FromPixelsAsync(ActionIconSource source)
    {
        var pixels = await Task.Run(() => ActionIcons.TryGetIconPixels(source.Value, source.IconIndex, 48, out var found) ? found : default(IconPixels));
        if (pixels.Bgra is null || pixels.Width <= 0)
            return null;

        // XAML wants premultiplied alpha; the shell gives straight alpha.
        var bgra = pixels.Bgra;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            if (alpha == 255)
                continue;
            if (alpha == 0)
            {
                bgra[i] = 0;
                bgra[i + 1] = 0;
                bgra[i + 2] = 0;
                continue;
            }
            bgra[i] = (byte)(bgra[i] * alpha / 255);
            bgra[i + 1] = (byte)(bgra[i + 1] * alpha / 255);
            bgra[i + 2] = (byte)(bgra[i + 2] * alpha / 255);
        }

        using var software = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(bgra), BitmapPixelFormat.Bgra8, pixels.Width, pixels.Height, BitmapAlphaMode.Premultiplied);
        var bitmapSource = new SoftwareBitmapSource();
        await bitmapSource.SetBitmapAsync(software);
        return bitmapSource;
    }
}
