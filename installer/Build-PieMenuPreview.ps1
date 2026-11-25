param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$project=Join-Path $repo 'GestureSign.PieMenu/GestureSign.PieMenu.csproj'
$version=([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$artifacts = foreach($arch in @('x64','arm64')) {
    $platform = if($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $payload=Join-Path $output "payload-$arch"
    $zip=Join-Path $output "GestureSign-PieMenu-$version-preview-win-$arch.zip"
    if ((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $zip)) {
        throw "Use a fresh output directory: $output"
    }
    & dotnet publish $project -c Release -r "win-$arch" -p:Platform=$platform -p:PlatformTarget=$platform --self-contained false -o $payload | Out-Host
    if ($LASTEXITCODE) { throw "Pie Menu publish failed: $arch" }
    Get-ChildItem -LiteralPath $payload -File -Filter '*.pdb' | Remove-Item
    Copy-Item -LiteralPath "$repo/GestureSign.PieMenu/README.md","$repo/LICENSE" -Destination $payload
    foreach($name in @('GestureSignV2.PieMenu.exe','GestureSignV2.PieMenu.dll','App.xbf','GestureSignV2.PieMenu.pri','WindowsInput.dll','KandoWeb/renderer.js','KandoWeb/catalog.json','KandoWeb/locales/en/translation.json','KandoWeb/windows-key-codes.json','KandoWeb/native-icons.json','EditorAssets/PieMenu.ico','EditorAssets/PieMenu.png','EditorAssets/editor-strings.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $payload $name))) { throw "$arch package missing $name" }
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
}
$artifacts | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$artifacts
