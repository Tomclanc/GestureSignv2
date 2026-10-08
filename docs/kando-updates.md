# Updating the Kando component

In GestureSign, open **Quick Actions → Kando optional component → Check for updates**.
The dialog shows the installed version, the latest stable version and the release
notes. Select **Update now** to download and install the latest stable release.

Downloads first use the official `kando-menu/kando` GitHub `releases/latest` API. If
that API is rate limited, unavailable or times out, the updater follows the official
GitHub `/releases/latest` web redirect and reads the matching release assets and
release notes. Successful checks are cached for five minutes to avoid repeated
requests from checking and then installing. Both paths select the latest stable
release, excluding
drafts and prereleases. Windows OS architecture selects the x64 or ARM64 ZIP. A
missing architecture asset is an error; there is no silent fallback to another
architecture or an older version. GitHub/network failures leave the installation
unchanged and can be retried later.

The updater downloads, verifies the GitHub SHA-256 digest when provided, extracts
the application and checks its version and Windows executable compatibility before
closing Kando. It replaces the managed component directory, keeps menus/settings
and GestureSign bindings, and restarts Kando only if that copy was running before
replacement. The updater briefly blocks daemon launches through an exclusive,
automatically released file lease; it does not rewrite the user's enable setting.

The old application and configuration backup remain until startup validation
succeeds. Failed replacement/startup restores the old application and settings,
including settings a newer version may have migrated. If recovery itself fails
(for example, files remain locked), the error identifies the retained recovery
directories. A successful update removes the temporary backups.

Installations selected through a custom executable path outside the managed
component directory receive **Update instructions** and the official release link.
GestureSign does not replace or stop those installations. Update them using the
original installer/package manager, or replace their ZIP installation yourself.

Managed components use the existing persistent component directory and roaming
Kando configuration. If a managed installation was configured with
`portableMode.json`, that file and its configuration are preserved too.

## Validation

```powershell
dotnet run --project tests/GestureSign.KandoUpdateTests -c Release
# Optional network check of the actual latest stable release:
dotnet run --project tests/GestureSign.KandoUpdateTests -c Release -- --live-release
```

The offline suite covers API rate-limit/network fallback, cache expiry, exact
release/architecture/asset matching, failure reporting, app metadata,
exclusive update leases, replacement failure, startup failure after configuration
migration, restored portable settings and retained backups after recovery failure.

The optional network check simulates an API 403 response and verifies that the
official web fallback returns the current stable release, its ZIP digest and notes.
