param(
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [ValidateSet('Cpu','Hardware')][string]$Backend = 'Cpu',
    [switch]$SelfContained,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$Backend = if ($Backend -eq 'Hardware') { 'Hardware' } else { 'Cpu' }
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Choose an empty output directory; stale runtime files must never enter a new component.'
}
$version = '18.4.2'
$runtimeSuffix = if ($SelfContained) { '-standalone' } else { '' }
$archive = Join-Path (Split-Path $output -Parent) "GestureSign-IntentDlc-$version-$($Backend.ToLowerInvariant())-win-$Architecture$runtimeSuffix.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists; choose a fresh output directory: $archive" }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $repo 'GestureSign.IntentDlc\GestureSign.IntentDlc.csproj') -c Release -r "win-$Architecture" --self-contained ($SelfContained.IsPresent.ToString().ToLowerInvariant()) -o (Join-Path $output 'Runtime') -p:PlatformTarget=$Architecture -p:IntentBackend=$Backend
if ($LASTEXITCODE -ne 0) { throw 'Intent DLC publish failed.' }
$metadata = @{ Protocol = 2; Version = $version; Architecture = $Architecture; Backend = $Backend; SelfContained = $SelfContained.IsPresent }
[IO.File]::WriteAllText((Join-Path $output 'component.json'), ($metadata | ConvertTo-Json))
Copy-Item -LiteralPath (Join-Path $repo 'docs\intent-dlc.md') -Destination (Join-Path $output 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $output 'LICENSE') -Force
if ($Backend -eq 'Hardware') {
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$notices = Join-Path $output 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.windows.ai.machinelearning\2.3.42\license.txt') -Destination (Join-Path $notices 'Windows-ML-license.txt') -Force
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.ml.onnxruntime.managed\1.27.1\LICENSE.txt') -Destination (Join-Path $notices 'ONNX-Runtime-license.txt') -Force
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.ml.onnxruntime.managed\1.27.1\ThirdPartyNotices.txt') -Destination (Join-Path $notices 'ONNX-Runtime-ThirdPartyNotices.txt') -Force
}
Write-Host "Optional DLC payload: $output"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Extension -ne '.pdb' }) {
        $relative = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$asset = @{ Architecture = $Architecture; Version = $version; Backend = $Backend; FileName = [IO.Path]::GetFileName($archive); Bytes = (Get-Item -LiteralPath $archive).Length; Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash; Url = "https://github.com/Tomclanc/GestureSignv2/releases/download/v$version/$([IO.Path]::GetFileName($archive))" }
[IO.File]::WriteAllText(($archive + '.catalog.json'), ($asset | ConvertTo-Json))
Write-Host "Archive and catalog entry: $archive"
