param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
$entries = @()
foreach ($architecture in @('x64', 'arm64')) {
    foreach ($backend in @('Cpu', 'Hardware')) {
        $payload = Join-Path $output "$architecture-$backend"
        & (Join-Path $PSScriptRoot 'Build-IntentDlc.ps1') -Architecture $architecture -Backend $backend -OutputDirectory $payload
        $entry = Get-Content (Join-Path $output "GestureSign-IntentDlc-18.4.2-$($backend.ToLowerInvariant())-win-$architecture.zip.catalog.json") -Raw | ConvertFrom-Json
        $entries += $entry
    }
}
# Build the application only after all four immutable archives have their real
# lengths and digests. Never replace a released archive under the same URL.
[IO.File]::WriteAllText((Join-Path $repo 'GestureSign.WinUI/Assets/intent-dlc.catalog.json'), ($entries | ConvertTo-Json -Depth 4))
