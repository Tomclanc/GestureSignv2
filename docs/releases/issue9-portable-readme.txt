GestureSign V2 - issue #9 localization test build
Version: 18.3.2-issue9.1

1. Choose the ZIP matching your Windows device: x64 for Intel/AMD, arm64 for Windows on ARM.
2. Extract the entire ZIP to a new writable folder. Keep the Backend subfolder.
3. Exit any running GestureSign settings window and tray service before testing.
4. Start GestureSign.WinUI.exe, then choose your language under Options.
5. Test the application/action/command editors, edge editor, gesture drawing and recording dialogs, Kando settings, and log viewer.

This build translates previously Chinese-only editor captions, placeholders,
buttons, hints and messages. Missing translations fall back to English for
non-Chinese languages, including Japanese and Korean two-language captions.
Existing translated pages continue to use the selected language. User-entered
names, paths, plugin class names and command identifiers are preserved.
The direction selected when creating a gesture still uses the original template.

This is a prerelease for feedback, not a fully translated release in every language.
Back up your configuration before testing. Do not delete your current configuration.
No separate .NET or Windows App Runtime installation is required for this package.
Optional Kando and intent learning components remain separate downloads.
If some interface text remains Chinese, report the selected language and the
page/dialog name (or a screenshot) at:
https://github.com/Tomclanc/GestureSignv2/issues/9

Collect-SupportInfo.ps1 collects diagnostics locally when needed.
Recover-Input.ps1 requests input recovery from the running background service.