[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][ValidateSet('x64','ARM64')][string]$Architecture
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackagePath).Path
$backend = Join-Path $root 'Backend'
if (!(Test-Path -LiteralPath $backend -PathType Container)) { $backend = $root }
# Check managed libraries too: checking only the native apphost misses mixed
# project-reference outputs, which prevent an ARM64 daemon from loading.
$names = @('GestureSign.exe','GestureSign.dll','GestureSign.Common.dll',
    'GestureSign.Foundation.dll','GestureSign.CorePlugins.dll',
    'GestureSign.PointPatterns.dll','ManagedWinapi.dll','WindowsInput.dll',
    'GestureSign.ClipboardMatch.Plugin.dll','GestureSign.ExtraPlugins.TextCopyer.dll')
foreach ($name in $names) {
    & "$PSScriptRoot\Assert-PeArchitecture.ps1" -Path (Join-Path $backend $name) -Architecture $Architecture
}
$plugins = Join-Path $backend 'Plugins'
if (Test-Path -LiteralPath $plugins) {
    foreach ($dll in Get-ChildItem -LiteralPath $plugins -Filter '*.dll' -File) {
        & "$PSScriptRoot\Assert-PeArchitecture.ps1" -Path $dll.FullName -Architecture $Architecture
    }
}
if (Test-Path -LiteralPath (Join-Path $root 'GestureSign.WinUI.exe')) {
    foreach ($name in @('GestureSign.WinUI.exe','GestureSign.WinUI.dll','GestureSign.Foundation.dll')) {
        & "$PSScriptRoot\Assert-PeArchitecture.ps1" -Path (Join-Path $root $name) -Architecture $Architecture
    }
}
Write-Output "PASS: $Architecture application, backend, hooks and plugins have matching PE architecture."