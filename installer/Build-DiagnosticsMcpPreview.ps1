param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$project = Join-Path $repo 'GestureSign.Diagnostics.Mcp/GestureSign.Diagnostics.Mcp.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

$checksums = foreach ($arch in 'x64', 'arm64') {
    $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $payload = Join-Path $output "payload-$arch"
    $zip = Join-Path $output "GestureSign-Diagnostics-MCP-$version-preview-win-$arch.zip"
    if ((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $zip)) {
        throw "Use a fresh output directory: $output"
    }
    & dotnet publish $project -c Release -r "win-$arch" -p:Platform=$platform -p:PlatformTarget=$platform --self-contained false -o $payload | Out-Host
    if ($LASTEXITCODE) { throw "Diagnostics MCP publish failed: $arch" }
    Get-ChildItem -LiteralPath $payload -File -Filter '*.pdb' | Remove-Item
    Copy-Item -LiteralPath "$repo/GestureSign.Diagnostics.Mcp/README.md","$repo/LICENSE" -Destination $payload
    & "$repo/tools/Assert-PeArchitecture.ps1" -Path (Join-Path $payload 'GestureSign.Diagnostics.Mcp.exe') -Architecture $platform | Out-Host
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
}
$checksums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$checksums
