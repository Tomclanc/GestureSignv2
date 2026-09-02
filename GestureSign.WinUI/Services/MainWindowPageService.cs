using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.WinUI.ViewModels;

namespace GestureSign.WinUI.Services;

internal sealed class MainWindowPageService
{
    private readonly Dictionary<string, MainWindowPageRegistration> _pages;
    private readonly MainWindowPageRegistration _defaultPage;

    public MainWindowPageService(IEnumerable<MainWindowPageRegistration> pages, string defaultTag)
    {
        _pages = pages.ToDictionary(page => page.ViewModel.Tag, StringComparer.Ordinal);
        _defaultPage = _pages[defaultTag];
    }

    public MainWindowPageRegistration Resolve(string tag) => _pages.TryGetValue(tag, out var page) ? page : _defaultPage;
}
