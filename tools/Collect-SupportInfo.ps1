[CmdletBinding()]
param([string]$OutputDirectory = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$folder = Join-Path $OutputDirectory ('GestureSign-support-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
$logRoot = Join-Path $env:LOCALAPPDATA 'GestureSign V2'
# Snapshot request is read-only: never sends RecoverInput or changes capture mode.
$requestUtc = [DateTimeOffset]::UtcNow
$healthStatus = 'Not acknowledged (older build, stopped daemon, or unresponsive input thread)'
try {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', "GestureSignDaemon-$sid", [IO.Pipes.PipeDirection]::Out)
    try { $pipe.Connect(1500); $pipe.WriteByte(15); $pipe.Flush() } finally { $pipe.Dispose() }
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 100
        try {
            $health = Get-Content (Join-Path $logRoot 'Input-health.json') -Raw -ErrorAction Stop | ConvertFrom-Json
            if ($health.Reason -eq 'SupportRequest' -and [DateTimeOffset]::Parse($health.RecordedUtc) -ge $requestUtc) {
                $healthStatus = 'Acknowledged by input owner thread'; break
            }
        } catch { }
    }
} catch { $healthStatus = 'Snapshot request failed: ' + $_.Exception.Message }
$healthStatus | Set-Content (Join-Path $folder 'Input-health-request.txt') -Encoding UTF8
foreach ($name in @('GestureSign.log','GestureSign.WinUI.log','Input-health.json')) {
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
    InputHealthRequest = $healthStatus
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
