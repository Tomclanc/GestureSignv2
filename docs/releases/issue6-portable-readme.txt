GestureSign V2 18.3.2 - issue #6 test build (2026-09-30)

ARM64: native build for Surface Pro 11 / Snapdragon.
x64: optional comparison build, runs under Windows on ARM emulation.
Both contain their own .NET Desktop Runtime and Windows App SDK. No separate
runtime installation or AI component is required. Extract the entire ZIP.

Changes:
- Keep the daemon, hook libraries and plugins on the same processor architecture.
- Initialize input and tray after the STA message loop starts, in a defined order.
- Backup, Restore and Open config folder work independently of the display language.
- The log view includes startup stages and input registration status.

To test:
1. Back up your existing configuration. Fully exit the installed V2, including
   its settings window and background/tray processes. If it will not exit, end
   GestureSign.exe / GestureSign.Daemon and GestureSign.WinUI in Task Manager.
   Test one build at a time; an already-running daemon is shared by V2 windows.
2. Extract this ZIP to a new folder and run GestureSign.WinUI.exe.
   This is an unpackaged portable application; it uses the same existing V2
   configuration in %APPDATA%\GestureSign V2 (or your active OneDrive folder).
3. Keep your preferred UI language. Check that the tray icon appears and test
   familiar touch, mouse and pen gestures with the appropriate input options enabled.
4. Your converted backup is in Sample-config\GestureSign-Sample-V2.ges.
   It contains 59 action rules and 30 gesture definitions. If already imported,
   you do not need to restore it again. Restoring replaces actions and gestures.
5. If the daemon still fails to initialize, check Options -> View logs, or open
   %LOCALAPPDATA%\GestureSign V2\GestureSign.log. Run Collect-SupportInfo.ps1
   in PowerShell to copy the live logs and OS/process information into a ZIP in
   %TEMP%. Review that ZIP and attach it to issue #6; it contains no config files.
6. To roll back, fully exit this portable build and launch your previous V2.
   The test build does not install or replace the installed application.

Validation: x64 startup tested with the real published daemon (mouse hook, tray,
IPC), configuration backup/restore roundtrip tested, converted sample checked,
and ARM64 executable/library architecture checked. No Surface Pro 11 hardware
was available; native touch/pen behavior on that device remains to be confirmed.

Issue: https://github.com/Tomclanc/GestureSignv2/issues/6

中文说明：
这是针对 issue #6 的测试修复版。ARM64 包用于 Surface Pro 11，x64 包可用于模拟运行对比。
已包含 .NET 桌面运行时和 Windows App SDK，无需另装运行时或 AI 组件。
请先完全退出已安装的 V2，再将整个 ZIP 解压到新目录，运行 GestureSign.WinUI.exe。
便携版会继续使用已有 V2 配置；已导入过转换配置的话，不需要再次恢复。
备份、恢复和打开配置文件夹按钮已使用固定命令，不需要切换为简体中文。
如果仍然无托盘或手势无效，请运行 Collect-SupportInfo.ps1，将生成的日志 ZIP 回复到 issue。
x64 已通过后台实际启动检查；ARM64 已检查构建和所有应用依赖架构，Surface 实机效果尚待确认。
Follow-up build issue6.2:
- Pen: legacy button-only settings now imply tip/contact drawing; eraser and inverted-pen reports are both accepted.
- Hold one finger still and draw with another: lifting the moving finger completes the gesture. The stationary finger can stay down while the moving finger lands again to start another gesture.
- Ordinary gestures with multiple moving fingers still finish when the last finger lifts.
- For the Surface pen test, enable pen gestures, select Right button + Tip, hold the barrel button and draw on the screen.

Follow-up build issue6.3: keep the anchor finger down and repeat moving-finger gestures. Each new finger contact starts a fresh gesture; anchor movement or release alone never repeats the previous action.

Follow-up build issue6.4: clears interrupted touch sessions when pen input takes over; recovers stale touch ownership before a mouse gesture; refreshes native input on resume/unlock using the owning UI thread. The settings watchdog now sends a lightweight ping instead of repeatedly reloading all configuration.

If recognition stops again, collect the logs before recovery. Recover-Input.ps1 requests an input reset without restarting the daemon or enabling user-disabled recognition. Check GestureSign.log for the recovery result.
