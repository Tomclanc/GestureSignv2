param([Parameter(Mandatory=$true)][string]$PackagePath, [ValidateSet('Cpu','Hardware')][string]$Backend = 'Cpu')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try {
    $names = @($archive.Entries.FullName)
    if ($names -contains 'Runtime/coreclr.dll' -or $names -contains 'Runtime/System.Private.CoreLib.dll') { throw 'Shared-runtime package contains a private .NET runtime.' }
    $reader = [IO.StreamReader]::new($archive.GetEntry('Runtime/GestureSignV2.IntentDlc.runtimeconfig.json').Open())
    try { $config = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') { throw 'Missing shared .NET runtime declaration.' }
    if ($Backend -eq 'Cpu') {
        if ($names -match '(?i)(onnxruntime|DirectML|Windows\.AI\.MachineLearning|Windows\.SDK\.NET|WinRT\.Runtime)') { throw 'CPU package contains hardware dependencies.' }
        if (($archive.Entries | Measure-Object Length -Sum).Sum -gt 8MB) { throw 'CPU package exceeds its 8 MiB unpacked size budget.' }
    } elseif ($names -notcontains 'Runtime/onnxruntime.dll' -or $names -notcontains 'Runtime/DirectML.dll') { throw 'Hardware package lost acceleration dependencies.' }
    Write-Host "PASS: $Backend package dependency and size checks."
} finally { $archive.Dispose() }
