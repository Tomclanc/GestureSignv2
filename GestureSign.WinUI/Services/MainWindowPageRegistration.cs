using System;
using GestureSign.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace GestureSign.WinUI.Services;

internal sealed record MainWindowPageRegistration(MainWindowPageViewModel ViewModel, Func<UIElement> Build);
