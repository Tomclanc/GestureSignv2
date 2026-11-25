using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.PieMenu;

internal sealed class MenuEditSession
{
    private readonly Dictionary<PieMenu, PieMenu> _savedMenus = [];
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private MenuDocument _saved = null!;
    private string? _expectedContents;

    internal MenuEditSession(MenuDocument document)
    {
        Document = document;
        CaptureSavedMenus();
        _expectedContents = ReadCurrentContents();
    }

    internal MenuDocument Document { get; }

    internal Task<string> SaveAllAsync(Func<Task<string>> reload) => SaveAsync(null, null, null, reload);

    internal Task<string> SaveShortcutAsync(PieMenu menu, string shortcut, MenuLanguage language, Func<Task<string>> reload)
        => SaveAsync(menu, shortcut, language, reload);

    private async Task<string> SaveAsync(PieMenu? menu, string? shortcut, MenuLanguage? language, Func<Task<string>> reload)
    {
        await _saveLock.WaitAsync();
        try
        {
            if (menu is null)
            {
                _expectedContents = MenuStorage.Save(Document);
                CaptureSavedMenus();
            }
            else
            {
                if (!_savedMenus.TryGetValue(menu, out var committed))
                    return language!.Text("native.shortcut-save-new-menu");
                if (!string.Equals(ReadCurrentContents(), _expectedContents, StringComparison.Ordinal))
                    return language!.Text("native.shortcut-config-changed");
                var previous = committed.Shortcut;
                committed.Shortcut = shortcut!;
                try { _expectedContents = MenuStorage.Save(_saved); }
                catch { committed.Shortcut = previous; throw; }
            }
            return await reload();
        }
        finally { _saveLock.Release(); }
    }

    private void CaptureSavedMenus()
    {
        _saved = MenuStorage.Clone(Document);
        _savedMenus.Clear();
        for (var i = 0; i < Document.Menus.Count; i++)
            _savedMenus.Add(Document.Menus[i], _saved.Menus[i]);
    }

    private static string? ReadCurrentContents() =>
        File.Exists(MenuStorage.PathName) ? MenuStorage.ReadBounded(MenuStorage.PathName) : null;
}
