function Compress-ShareableAssemblies([string]$payload, [string]$depsFile, [string[]]$filenames) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
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
    $depsPath = Join-Path $payload $depsFile
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
