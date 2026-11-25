using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private readonly string _iconRoot = PieMenuAssets.Root;
    private readonly Dictionary<(string Theme, string Id, string Color, int Pixels), Task<SvgImageSource?>> _iconSources = [];
    private static readonly SemaphoreSlim AppIconWorkers = new(4, 4);
    private readonly Dictionary<string, Task<BitmapImage?>> _systemIconSources = new(StringComparer.OrdinalIgnoreCase);

    private void WarmSystemIcons()
    {
        var ids = _document.Menus.SelectMany(menu =>
            new[] { (menu.SourceIconTheme, menu.SourceIcon) }.Concat(
                menu.Items.Select(item => (item.SourceIconTheme, item.SourceIcon))));
        foreach (var id in ids.Where(icon => icon.SourceIconTheme == MenuIcons.System && !string.IsNullOrEmpty(icon.SourceIcon))
                     .Select(icon => icon.SourceIcon!).Distinct(StringComparer.OrdinalIgnoreCase).Take(32))
            _ = GetSystemIconSource(id);
    }

    private Task<BitmapImage?> GetSystemIconSource(string id)
    {
        if (!_systemIconSources.TryGetValue(id, out var source))
        {
            if (_systemIconSources.Count >= 128) _systemIconSources.Clear();
            _systemIconSources[id] = source = LoadAsync(id);
        }
        return source;

        static async Task<BitmapImage?> LoadAsync(string icon)
        {
            try
            {
                if (!MenuIcons.TryImage(await WindowsApplicationIcons.ResolveImportedAsync(icon), out var pixels))
                    return null;
                var bitmap = new BitmapImage();
                return await ReadIconAsync(bitmap, pixels) ? bitmap : null;
            }
            catch (Exception error) when (error is ArgumentException or IOException or COMException)
            {
                return null;
            }
        }
    }

    private FrameworkElement RenderIcon(string? theme, string? id, string glyph, double size)
    {
        if (theme == MenuIcons.System && id is not null)
        {
            var pending = GetSystemIconSource(id);
            if (pending.IsCompletedSuccessfully && pending.Result is { } cached)
                return IconImage(cached, size);

            var image = IconImage(null, size);
            var systemFallback = new FontIcon
            {
                Glyph = "\uE71D", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size * .75,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var host = new Grid { Width = size, Height = size };
            host.Children.Add(systemFallback);
            host.Children.Add(image);
            _ = ShowImportedIconAsync();
            return host;

            async Task ShowImportedIconAsync()
            {
                if (await pending is not { } source) return;
                image.Source = source;
                systemFallback.Visibility = Visibility.Collapsed;
            }
        }
        if (theme == MenuIcons.Image && MenuIcons.TryImage(id, out var bytes))
        {
            var image = new BitmapImage();
            _ = ReadIconAsync(image, bytes);
            return IconImage(image, size);
        }
        if (theme == MenuIcons.Emoji || theme is null || id is null)
        {
            var label = Label(theme == MenuIcons.Emoji && id is not null ? id : glyph, size * .84);
            if (label.Text.Any(character => character is >= '\uE000' and <= '\uF8FF'))
                label.FontFamily = new FontFamily("Segoe Fluent Icons");
            return label;
        }
        var graphic = new ContentControl();
        var fallback = Label(glyph, size * .84);
        var container = new Grid { Width = size, Height = size };
        container.Children.Add(fallback);
        container.Children.Add(graphic);
        var revision = 0;
        bool nativeVector = false;
        XamlRoot? root = null;
        double scale = 1;
        container.Loaded += async (_, _) =>
        {
            root = container.XamlRoot;
            if (root is not null) root.Changed += RootChanged;
            await ApplyVectorAsync();
        };
        container.Unloaded += (_, _) =>
        {
            revision++;
            if (root is not null) root.Changed -= RootChanged;
            root = null;
        };
        container.ActualThemeChanged += async (_, _) =>
        {
            if (container.IsLoaded) await ApplyVectorAsync();
        };
        return container;

        async void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            if (!nativeVector && scale != sender.RasterizationScale)
                await ApplyVectorAsync();
        }

        async Task ApplyVectorAsync()
        {
            var current = ++revision;
            scale = root?.RasterizationScale ?? 1;
            var appearance = container.ActualTheme;
            var color = appearance == ElementTheme.Dark ? "#FFFFFF" : "#202124";
            FrameworkElement? rendered = null;
            var fileIcon = theme == MenuIcons.Kando;
            var needsSvgDecoder = fileIcon;
            if (theme is MenuIcons.Material or MenuIcons.Simple or MenuIcons.Colored)
            {
                try
                {
                    var catalog = await MenuIcons.GetAsync(_iconRoot);
                    if (current != revision) return;
                    if (catalog.Find(theme, id) is { Path.Length: > 0 } entry)
                    {
                        rendered = CreateIconThumbnail(catalog, theme, entry, appearance, size);
                        needsSvgDecoder = rendered is null;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            }
            nativeVector = !needsSvgDecoder;
            if (needsSvgDecoder)
            {
                var source = await GetVectorSource(theme, id, color, RasterPixels(size, scale));
                if (current != revision) return;
                if (source is not null) rendered = IconImage(source, size);
            }
            if (current != revision) return;
            graphic.Content = rendered;
            fallback.Visibility = rendered is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Keep a little extra detail for diagonal edges when the compositor scales the icon.
    private static int RasterPixels(double size, double scale) =>
        Math.Clamp((int)Math.Ceiling(size * scale * 1.5 / 8) * 8, 16, 512);

    private Task<SvgImageSource?> GetVectorSource(string theme, string id, string color, int pixels)
    {
        var key = (theme, id, color, pixels);
        if (!_iconSources.TryGetValue(key, out var pending))
        {
            if (_iconSources.Count >= 512) _iconSources.Clear();
            _iconSources[key] = pending = LoadVectorAsync(theme, id, color, pixels);
        }
        return pending;
    }

    private async Task<SvgImageSource?> LoadVectorAsync(string theme, string id, string color, int pixels)
    {
        try
        {
            var svg = await MenuIcons.SvgAsync(_iconRoot, theme, id, color);
            if (svg is null) return null;
            var vector = new SvgImageSource { RasterizePixelWidth = pixels };
            using var memory = new MemoryStream(Encoding.UTF8.GetBytes(svg));
            using var stream = memory.AsRandomAccessStream();
            return await vector.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success ? vector : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or COMException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static Image IconImage(ImageSource? source, double size) => new()
    {
        Width = size, Height = size, Stretch = Stretch.Uniform, Source = source
    };

    private static async Task<bool> ReadIconAsync(BitmapImage image, byte[] bytes)
    {
        try
        {
            using var memory = new MemoryStream(bytes);
            using var stream = memory.AsRandomAccessStream();
            await image.SetSourceAsync(stream);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException) { return false; }
    }

    private FrameworkElement RootIcon(PieMenu menu, double size) =>
        menu.SourceIcon is null && menu.Glyph == "✿" ? RenderIcon(MenuIcons.Kando, "logo-medium.svg", menu.Glyph, size)
            : RenderIcon(menu.SourceIconTheme, menu.SourceIcon, menu.Glyph, size);

    private Button IconPickerButton(FrameworkElement icon, string? theme, string? id, Action<string, string> select)
    {
        var button = Button(icon);
        button.CornerRadius = new CornerRadius(12);
        button.Padding = new Thickness(7);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        var title = L("native.choose-icon");
        ToolTipService.SetToolTip(button, title);
        AutomationProperties.SetName(button, title);
        button.Click += async (_, _) =>
        {
            try { await PickIconAsync(theme, id, select); }
            catch (Exception error) { SetStatus(error.Message); }
        };
        return button;
    }

    private async Task PickIconAsync(string? initialTheme, string? initialId, Action<string, string> select)
    {
        const int pageSize = 56;
        var dialog = new ContentDialog
        {
            Title = L("native.choose-icon"), CloseButtonText = L("settings.cancel"), XamlRoot = _page.XamlRoot,
            RequestedTheme = _page.ActualTheme,
            FlowDirection = GestureSign.WinUI.UiTranslationCatalog.IsRightToLeft(_language.Tag)
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        var libraryOptions = MenuIcons.Themes.Select(theme => theme with
        {
            Name = GestureSign.WinUI.UiTranslationCatalog.TranslateComponent(_language.Tag, theme.Name)
        }).ToArray();
        var themes = new ComboBox
        {
            ItemsSource = libraryOptions,
            DisplayMemberPath = nameof(MenuIcons.Theme.Name),
            SelectedItem = libraryOptions.FirstOrDefault(option => option.Id == initialTheme) ?? libraryOptions[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(themes, L("native.icon-library"));
        var search = new TextBox { PlaceholderText = L("settings.icon-picker-dialog.search-placeholder") };
        AutomationProperties.SetName(search, search.PlaceholderText);
        var icons = new VariableSizedWrapGrid
        {
            ItemWidth = 48, ItemHeight = 48, MaximumRowsOrColumns = 8, Orientation = Orientation.Horizontal
        };
        var gallery = new ScrollViewer { Content = icons, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tiles = new List<Button>(pageSize);
        var thumbnails = new Dictionary<(string Theme, string Id), Task<FrameworkElement>>();
        string? lastTheme = null;
        string? displayedTheme = null;
        string? lastQuery = null;
        MenuIcons.Icon[] lastMatches = [];
        var counter = new TextBlock { Width = 76, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var page = 0;
        var pageCount = 1;
        var previous = Button("‹");
        var next = Button("›");
        AutomationProperties.SetName(previous, L("native.icon-previous"));
        AutomationProperties.SetName(next, L("native.icon-next"));
        var pageProgress = new ProgressRing { Width = 16, Height = 16, Visibility = Visibility.Collapsed };
        var pageProgressSlot = new Grid { Width = 16, VerticalAlignment = VerticalAlignment.Center };
        pageProgressSlot.Children.Add(pageProgress);
        var paging = Row(previous, counter, next, pageProgressSlot);
        previous.IsEnabled = next.IsEnabled = false;
        var empty = new TextBlock { Text = L("native.icon-no-results"), Visibility = Visibility.Collapsed };
        var image = new TextBox { PlaceholderText = "data:image/png;base64,…", Text = initialTheme == MenuIcons.Image ? initialId ?? "" : "" };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var appStatus = new ProgressRing { IsActive = false, Visibility = Visibility.Collapsed };
        Task<WindowsApplicationIcons.Entry[]>? installedApps = null;
        var appImages = new Dictionary<string, Task<(BitmapImage? Bitmap, string? Data)>>(StringComparer.OrdinalIgnoreCase);
        var searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        var refreshRevision = 0;
        var closed = false;
        (string Theme, string Id)? selected = null;
        var useImage = Button(L("native.icon-use-image"), () =>
        {
            if (MenuIcons.TryImage(image.Text.Trim(), out _)) Choose(MenuIcons.Image, image.Text.Trim());
            else feedback.Text = L("native.icon-invalid-image");
        });
        var pickImage = Button(L("native.icon-pick-file"), async () =>
        {
            try
            {
                var picker = new FileOpenPicker(_owner);
                foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".exe" })
                    picker.FileTypeFilter.Add(extension);
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                image.Text = await ReadImageDataAsync(file.Path) ?? "";
                if (MenuIcons.TryImage(image.Text, out _))
                    Choose(MenuIcons.Image, image.Text);
                else feedback.Text = L("native.icon-invalid-image");
            }
            catch (Exception error) { feedback.Text = error.Message; }
        });
        var custom = new StackPanel { Spacing = 8 };
        custom.Children.Add(image);
        custom.Children.Add(Row(pickImage, useImage));
        custom.Children.Add(feedback);
        var body = new StackPanel { Spacing = 9, MaxWidth = 410 };
        foreach (var child in new UIElement[] { themes, search, appStatus, gallery, empty, paging, custom }) body.Children.Add(child);
        dialog.Content = body;
        previous.Click += (_, _) => ChangePage(-1);
        next.Click += (_, _) => ChangePage(1);
        search.TextChanged += (_, _) =>
        {
            page = 0;
            refreshRevision++;
            searchTimer.Stop();
            searchTimer.Start();
        };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await RefreshAsync(); };
        themes.SelectionChanged += async (_, _) => { page = 0; await RefreshAsync(); };
        _ = RefreshAsync();
        await dialog.ShowAsync();
        searchTimer.Stop();
        closed = true;
        if (selected is { } result) select(result.Theme, result.Id);

        void Choose(string theme, string id)
        {
            selected = (theme, id);
            dialog.Hide();
        }

        void SetPageLoading(bool loading)
        {
            pageProgress.IsActive = loading;
            pageProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }

        void ChangePage(int delta)
        {
            var requested = Math.Clamp(page + delta, 0, pageCount - 1);
            if (requested == page) return;
            page = requested;
            UpdatePageControls();
            SetPageLoading(true);
            _ = RefreshAsync();
        }

        async Task RefreshAsync()
        {
            searchTimer.Stop();
            var revision = ++refreshRevision;
            var theme = ((MenuIcons.Theme)themes.SelectedItem).Id;
            var customImage = theme == MenuIcons.Image;
            var systemApps = theme == MenuIcons.WindowsApps;
            custom.Visibility = customImage ? Visibility.Visible : Visibility.Collapsed;
            search.Visibility = customImage ? Visibility.Collapsed : Visibility.Visible;
            gallery.Visibility = paging.Visibility = customImage ? Visibility.Collapsed : Visibility.Visible;
            appStatus.IsActive = systemApps;
            appStatus.Visibility = systemApps ? Visibility.Visible : Visibility.Collapsed;
            foreach (var tile in tiles) tile.Tag = null;
            if (displayedTheme != theme) ResetTiles(0);
            empty.Visibility = Visibility.Collapsed;
            empty.Text = L("native.icon-no-results");
            if (customImage)
            {
                SetPageLoading(false);
                return;
            }
            MenuIcons catalog;
            MenuIcons.Icon[] matches;
            try
            {
                if (systemApps)
                {
                    var apps = await (installedApps ??= WindowsApplicationIcons.ListAsync());
                    if (closed || revision != refreshRevision) return;
                    appStatus.IsActive = false;
                    appStatus.Visibility = Visibility.Collapsed;
                    var appQuery = search.Text.Trim();
                    var filtered = apps.Where(app => app.Name.Contains(appQuery, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                    const int appsPerPage = 32;
                    SetPaging(filtered.Length, appsPerPage);
                    SetPageLoading(false);
                    var visibleApps = filtered.Skip(page * appsPerPage).Take(appsPerPage).ToArray();
                    ResetTiles(visibleApps.Length);
                    gallery.ChangeView(null, 0, null, true);
                    displayedTheme = theme;
                    for (var i = 0; i < visibleApps.Length; i++)
                    {
                        var app = visibleApps[i];
                        var choice = tiles[i];
                        UpdateTile(choice, app.Name, new SymbolIcon(Symbol.FourBars), null);
                        choice.IsEnabled = false;
                        _ = PopulateAppTileAsync(app, choice, revision);
                    }
                    return;
                }
                catalog = await MenuIcons.GetAsync(_iconRoot);
                if (closed || revision != refreshRevision) return;
                var query = search.Text;
                if (lastTheme != theme || lastQuery != query)
                {
                    lastTheme = theme;
                    lastQuery = query;
                    lastMatches = catalog.Search(theme, query).ToArray();
                }
                matches = lastMatches;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                if (!closed && revision == refreshRevision)
                {
                    SetPageLoading(false);
                    SetStatus(error.Message);
                }
                return;
            }
            SetPaging(matches.Length, pageSize);
            var visible = matches.Skip(page * pageSize).Take(pageSize).ToArray();
            var pending = new Task<FrameworkElement>[visible.Length];
            for (var i = 0; i < visible.Length; i++)
            {
                if (thumbnails.TryGetValue((theme, visible[i].Id), out var cached))
                    pending[i] = cached;
            }

            if (pending.Any(task => task is null || !task.IsCompleted))
            {
                SetPageLoading(true);
                const int batchSize = 8;
                for (var start = 0; start < visible.Length; start += batchSize)
                {
                    var end = Math.Min(start + batchSize, visible.Length);
                    if (!pending[start..end].Any(task => task is null)) continue;
                    if (!await YieldToInputAsync() || closed || revision != refreshRevision) return;
                    for (var i = start; i < end; i++)
                        pending[i] ??= Thumbnail(catalog, theme, visible[i]);
                }
            }
            var images = await Task.WhenAll(pending);
            if (closed || revision != refreshRevision) return;

            SetPageLoading(false);
            ResetTiles(visible.Length);
            gallery.ChangeView(null, 0, null, true);
            displayedTheme = theme;
            for (var i = 0; i < visible.Length; i++)
                UpdateTile(tiles[i], visible[i].Name, images[i], (theme, visible[i].Id));

            var adjacent = page + 1 < pageCount ? page + 1 : page - 1;
            if (adjacent >= 0) _ = WarmPageAsync(catalog, theme, matches, adjacent, revision);
        }

        Task<bool> YieldToInputAsync()
        {
            var resumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dialog.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => resumed.TrySetResult(true)))
                resumed.TrySetResult(false);
            return resumed.Task;
        }

        async Task WarmPageAsync(MenuIcons catalog, string theme, MenuIcons.Icon[] matches, int adjacent, int revision)
        {
            const int batchSize = 8;
            int end = Math.Min((adjacent + 1) * pageSize, matches.Length);
            for (var start = adjacent * pageSize; start < end; start += batchSize)
            {
                if (!await YieldToInputAsync() || closed || revision != refreshRevision) return;
                for (var i = start; i < Math.Min(start + batchSize, end); i++)
                    _ = Thumbnail(catalog, theme, matches[i]);
            }
        }

        Task<FrameworkElement> Thumbnail(MenuIcons catalog, string theme, MenuIcons.Icon icon)
        {
            var key = (theme, icon.Id);
            if (!thumbnails.TryGetValue(key, out var task))
            {
                if (thumbnails.Count >= pageSize * 8) thumbnails.Clear();
                thumbnails[key] = task = LoadThumbnailAsync(catalog, theme, icon);
            }
            return task;
        }

        async Task<FrameworkElement> LoadThumbnailAsync(MenuIcons catalog, string theme, MenuIcons.Icon icon)
        {
            if (CreateIconThumbnail(catalog, theme, icon, dialog.RequestedTheme) is { } native) return native;
            if (theme == MenuIcons.Emoji) return RenderIcon(theme, icon.Id, icon.Id, 26);
            var color = dialog.RequestedTheme == ElementTheme.Dark ? "#FFFFFF" : "#202124";
            var source = await GetVectorSource(theme, icon.Id, color, RasterPixels(26, dialog.XamlRoot.RasterizationScale));
            return source is null ? new SymbolIcon(Symbol.FourBars) : IconImage(source, 26);
        }

        void ResetTiles(int count)
        {
            // A search can move a cached thumbnail to a different button.
            // Detach all old content before assigning the new page.
            foreach (var tile in tiles) tile.Content = null;
            while (tiles.Count < count)
            {
                var tile = NewIconTile();
                tile.Click += (_, _) =>
                {
                    if (tile.Tag is ValueTuple<string, string> icon) Choose(icon.Item1, icon.Item2);
                };
                tiles.Add(tile);
                icons.Children.Add(tile);
            }
            for (var i = 0; i < tiles.Count; i++)
                tiles[i].Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateTile(Button tile, string name, FrameworkElement content, (string Theme, string Id)? icon)
        {
            tile.Content = content;
            tile.IsEnabled = true;
            tile.Tag = icon;
            tile.ClearValue(Control.BorderBrushProperty);
            if (icon is { } choice && choice.Theme == initialTheme && choice.Id == initialId)
                tile.BorderBrush = Brush(237, 207, 214);
            AutomationProperties.SetName(tile, name);
            ToolTipService.SetToolTip(tile, name);
        }

        void SetPaging(int count, int perPage)
        {
            pageCount = Math.Max(1, (count + perPage - 1) / perPage);
            page = Math.Clamp(page, 0, pageCount - 1);
            UpdatePageControls();
            empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdatePageControls()
        {
            previous.IsEnabled = page > 0;
            next.IsEnabled = page + 1 < pageCount;
            counter.Text = $"{page + 1} / {pageCount}";
        }

        async Task PopulateAppTileAsync(WindowsApplicationIcons.Entry app, Button tile, int revision)
        {
            await AppIconWorkers.WaitAsync();
            try
            {
                if (closed || revision != refreshRevision) return;
                if (!appImages.TryGetValue(app.ParsingName, out var pending))
                    appImages[app.ParsingName] = pending = LoadAppThumbnailAsync(app.ParsingName);
                var (bitmap, data) = await pending;
                if (closed || revision != refreshRevision || bitmap is null || data is null) return;
                tile.Content = IconImage(bitmap, 30);
                tile.IsEnabled = true;
                tile.Tag = (MenuIcons.Image, data);
            }
            finally { AppIconWorkers.Release(); }
        }
    }

    private static async Task<(BitmapImage? Bitmap, string? Data)> LoadAppThumbnailAsync(string name)
    {
        var data = await WindowsApplicationIcons.GetImageAsync(name);
        if (!MenuIcons.TryImage(data, out var bytes)) return (null, null);
        var bitmap = new BitmapImage();
        return await ReadIconAsync(bitmap, bytes) && bitmap.PixelWidth > 0 ? (bitmap, data) : (null, null);
    }

    // SVG permits extra coordinate pairs after Move. WinUI expects a separate Line command.
    private const string PathNumber = @"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[Ee][+-]?[0-9]+)?";
    [GeneratedRegex(@"([Mm])([,\s]*(?>" + PathNumber + @")[,\s]*(?>" + PathNumber + @"))[,\s]*(?=[+.\-0-9])")]
    private static partial Regex ImplicitMoveLines();

    private static FrameworkElement? CreateIconThumbnail(MenuIcons catalog, string theme, MenuIcons.Icon icon,
        ElementTheme appearance, double size = 26)
    {
        if (string.IsNullOrEmpty(icon.Path)) return null;
        try
        {
            var path = new ShapePath();
            // SVG uses the nonzero winding rule by default.
            var pathData = ImplicitMoveLines().Replace(icon.Path, match =>
                match.Groups[1].Value + match.Groups[2].Value + (match.Value[0] == 'M' ? "L" : "l"));
            XamlBindingHelper.SetPropertyFromString(path, ShapePath.DataProperty, "F1 " + pathData);
            if (path.Data is null) return null;

            if (theme == MenuIcons.Colored && uint.TryParse(icon.Color?.TrimStart('#'),
                    System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var color))
                path.Fill = Brush((byte)(color >> 16), (byte)(color >> 8), (byte)color);
            else
                path.Fill = appearance == ElementTheme.Dark ? Brush(255, 255, 255) : Brush(32, 33, 36);

            var units = catalog.ViewportSize(theme);
            var viewport = new Canvas { Width = units, Height = units };
            viewport.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = viewport };
        }
        catch (Exception error) when (error is ArgumentException or COMException)
        {
            return null;
        }
    }

    private static Button NewIconTile() => new()
    {
        Width = 44, Height = 44, Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static async Task<string?> ReadImageDataAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (file.FileType.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return await WindowsApplicationIcons.GetImageAsync(file.Path);
        if (new FileInfo(path).Length > MenuIcons.ImageLimit) return null;
        var contentType = file.FileType.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
            ".webp" => "image/webp", ".ico" => "image/x-icon", ".png" => "image/png", _ => null
        };
        return contentType is null ? null : $"data:{contentType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path))}";
    }
}
