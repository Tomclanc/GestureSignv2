# GestureSign.WinUI

WinUI 3 front-end prototype for GestureSign.

This project is intentionally separate from the original WPF control panel and daemon. It is a first pass at a Windows 11 Settings-style shell:

- `NavigationView` left rail for Actions, Ignored apps, Gestures, Options, and About.
- `MicaBackdrop` with `BaseAlt` where supported.
- Rounded card surfaces and rounded command controls.
- Per-monitor DPI manifest for high-DPI and high-refresh displays.
- Uses the same transparent `logo.png` asset as the modernized taskbar/tray icon work.

Build requirement:

- Visual Studio 2022 Build Tools with `Microsoft.VisualStudio.Workload.VCTools`.
- Windows App SDK package restore from NuGet.

Build command:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe' .\GestureSign.WinUI.sln /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /restore
```

The current machine has Windows App SDK packages restored, but the C++/MSVC workload is not present at `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC`, so the WinUI XAML compiler cannot finish here yet.
