[CmdletBinding()]
param([string]$OutputDirectory = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$folder = Join-Path $OutputDirectory ('GestureSign-support-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
$logRoot = Join-Path $env:LOCALAPPDATA 'GestureSign V2'
foreach ($name in @('GestureSign.log','GestureSign.WinUI.log')) {
    $path = Join-Path $logRoot $name
    if (Test-Path -LiteralPath $path) {
        try {
            $inputFile = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try {
                $outputFile = [IO.File]::Create((Join-Path $folder $name))
                try { $inputFile.CopyTo($outputFile) } finally { $outputFile.Dispose() }
            } finally { $inputFile.Dispose() }
        } catch { $_.Exception.Message | Set-Content (Join-Path $folder ($name + '.error.txt')) }
    }
}
$info = [ordered]@{
    CollectedAt = (Get-Date).ToString('o')
    Windows = [Environment]::OSVersion.VersionString
    OSArchitecture = $env:PROCESSOR_ARCHITECTURE
    ShellArchitecture = $env:PROCESSOR_ARCHITEW6432
    PackageDirectory = $PSScriptRoot
    Processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('GestureSign','GestureSign.WinUI') } | ForEach-Object {
        [ordered]@{ Id=$_.Id; Name=$_.ProcessName; Path=$_.Path }
    })
}
$info | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'Support-info.json') -Encoding UTF8
$zip = $folder + '.zip'
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip
Write-Output "Support package: $zip"
Write-Output 'Contains application logs and process/OS information, not gesture configuration files.'