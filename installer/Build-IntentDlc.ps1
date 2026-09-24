param(
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $repo 'GestureSign.IntentDlc\GestureSign.IntentDlc.csproj') -c Release -r "win-$Architecture" --self-contained true -o (Join-Path $output 'Runtime') -p:PlatformTarget=$Architecture
if ($LASTEXITCODE -ne 0) { throw 'Intent DLC publish failed.' }
$metadata = @{ Protocol = 2; Version = '18.2.9'; Architecture = $Architecture }
[IO.File]::WriteAllText((Join-Path $output 'component.json'), ($metadata | ConvertTo-Json))
Copy-Item -LiteralPath (Join-Path $repo 'docs\intent-dlc.md') -Destination (Join-Path $output 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $output 'LICENSE') -Force
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$notices = Join-Path $output 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.windows.ai.machinelearning\2.3.42\license.txt') -Destination (Join-Path $notices 'Windows-ML-license.txt') -Force
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.ml.onnxruntime.managed\1.27.1\LICENSE.txt') -Destination (Join-Path $notices 'ONNX-Runtime-license.txt') -Force
Copy-Item -LiteralPath (Join-Path $nugetRoot 'microsoft.ml.onnxruntime.managed\1.27.1\ThirdPartyNotices.txt') -Destination (Join-Path $notices 'ONNX-Runtime-ThirdPartyNotices.txt') -Force
Write-Host "Optional DLC payload: $output"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = Join-Path (Split-Path $output -Parent) "GestureSign-IntentDlc-18.2.9-win-$Architecture-repack.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists; choose a fresh output directory: $archive" }
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Extension -ne '.pdb' }) {
        $relative = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$asset = @{ Architecture = $Architecture; Version = '18.2.9'; FileName = [IO.Path]::GetFileName($archive); Bytes = (Get-Item -LiteralPath $archive).Length; Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash; Url = "https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/$([IO.Path]::GetFileName($archive))" }
[IO.File]::WriteAllText(($archive + '.catalog.json'), ($asset | ConvertTo-Json))
Write-Host "Archive and catalog entry: $archive"
