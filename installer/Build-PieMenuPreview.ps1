param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$ShareHostAssemblies
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$project=Join-Path $repo 'GestureSign.PieMenu/GestureSign.PieMenu.csproj'
$version=([xml](Get-Content -LiteralPath $project -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Compress-ShareableAssemblies([string]$payload) {
    # The generated WinRT module initializer runs before Main and needs its
    # runtime available locally. Share only the larger managed projections.
    $filenames = @('Microsoft.Windows.SDK.NET.dll', 'Microsoft.WinUI.dll')
    $checksums = [ordered]@{}
    $zip = Join-Path $payload 'shared-assemblies.zip'
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($filename in $filenames) {
            $path = Join-Path $payload $filename
            $checksums[$filename] = (Get-FileHash -LiteralPath $path).Hash
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $path, $filename, [IO.Compression.CompressionLevel]::SmallestSize)
        }
    } finally {
        $archive.Dispose()
    }

    # The entry point loads these assemblies. Remove their runtime assets so
    # the .NET host does not require loose copies before managed startup.
    $depsPath = Join-Path $payload 'GestureSignV2.PieMenu.deps.json'
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($target in $deps.targets.Values) {
        foreach ($library in $target.Values) {
            if (!$library.runtime) { continue }
            foreach ($asset in @($library.runtime.Keys)) {
                if ((Split-Path $asset -Leaf) -in $filenames) {
                    $library.runtime.Remove($asset)
                }
            }
        }
    }
    [IO.File]::WriteAllText($depsPath, ($deps | ConvertTo-Json -Depth 80 -Compress))
    [IO.File]::WriteAllText((Join-Path $payload 'shared-assemblies.json'), ($checksums | ConvertTo-Json -Compress))
    foreach ($filename in $filenames) {
        Remove-Item -LiteralPath (Join-Path $payload $filename)
    }
}

$artifacts = foreach($arch in @('x64','arm64')) {
    $platform = if($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $payload=Join-Path $output "payload-$arch"
    $variant = if ($ShareHostAssemblies) { 'shared-preview' } else { 'preview' }
    $zip=Join-Path $output "GestureSign-PieMenu-$version-$variant-win-$arch.zip"
    if ((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $zip)) {
        throw "Use a fresh output directory: $output"
    }
    $share = $ShareHostAssemblies.IsPresent.ToString().ToLowerInvariant()
    & dotnet publish $project -c Release -r "win-$arch" -p:Platform=$platform -p:PlatformTarget=$platform -p:ShareHostAssemblies=$share --self-contained false -o $payload | Out-Host
    if ($LASTEXITCODE) { throw "Pie Menu publish failed: $arch" }
    Get-ChildItem -LiteralPath $payload -File -Filter '*.pdb' | Remove-Item
    Copy-Item -LiteralPath "$repo/GestureSign.PieMenu/README.md","$repo/LICENSE" -Destination $payload
    foreach($name in @('GestureSignV2.PieMenu.exe','GestureSignV2.PieMenu.dll','App.xbf','GestureSignV2.PieMenu.pri','WindowsInput.dll','KandoWeb/renderer.js','KandoWeb/catalog.json','KandoWeb/locales/en/translation.json','KandoWeb/windows-key-codes.json','KandoWeb/native-icons.zip','EditorAssets/PieMenu.ico','EditorAssets/PieMenu.png','EditorAssets/editor-strings.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $payload $name))) { throw "$arch package missing $name" }
    }
    if ($ShareHostAssemblies) {
        Compress-ShareableAssemblies $payload
    }
    $inventory = Get-ChildItem -LiteralPath $payload -Recurse -File -Force | ForEach-Object {
        $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
    } | Sort-Object
    [IO.File]::WriteAllLines((Join-Path $payload 'payload-files.txt'), [string[]]$inventory)
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
}
$artifacts | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$artifacts
