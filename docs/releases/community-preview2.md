# GestureSign V2 18.4.3 Community Preview 2

This prerelease simplifies creating gestures and commands, adds desktop-edge wheel bindings and drag lock, and improves configuration import and diagnostics. It is intended for testing, not a declaration that the intermittent Surface input issue is resolved.

- Gesture recording reports input-device and mouse-button information. Mouse-emulated touchpads remain explicitly ambiguous; backend recording supplies the device identity.
- Desktop outer-edge wheel actions respect application exclusions and skip internal monitor boundaries and injected wheel events.
- Drag lock supports toggling and Esc release, with release on pause, normal exit, lock and suspend.
- Archive import previews contents, backs up replaced files and rolls back failed writes. It replaces files rather than merging individual rules. Legacy JSON Actions.act conversion is supported; not every historical format is supported.
- Diagnostics are selected and previewed before local export. New UI strings include Japanese and Simplified Chinese translations, with English fallback in other existing catalogs.
- A CPU/memory sampling helper is included. Real battery/power comparisons have not been performed.

## Downloads

x64 MSI and portable ZIP use shared runtimes. Install x64 .NET 10 Desktop Runtime and a compatible Windows App SDK runtime separately. Neither download bundles those runtimes, AI components or Kando.

The MSI uses the existing GestureSign V2 upgrade family and numeric installer version 18.4.3; same-version upgrades are enabled, so it can replace the stable 18.4.3 MSI installation. The application identifies itself as 18.4.3-community-preview2. The portable package also uses existing V2 configuration. Export a configuration backup, exit the settings window and tray daemon, and then install or launch the preview. Do not run old and new backends together.

## Validation and limits

Passed x64 release/package architecture checks, native daemon startup/input-health/recovery checks, desktop-edge and drag-lock simulated tests, archive failure/rollback tests, mouse-binding and training-IPC compatibility tests, drawing/dialog/shortcut regression suites and localization checks. ARM64 backend cross-compilation passed; no ARM64 package is distributed here.

Full interactive UI and physical-device testing, Surface Pro 11 long-session/resume/pen-touch/spacedesk validation and real power measurements remain pending. Existing community issues remain open.
