[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [string]$SampleArchive
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
if (Get-Process GestureSign -ErrorAction SilentlyContinue) { throw 'Exit existing GestureSign daemons before running the startup test.' }
$report = Join-Path ([IO.Path]::GetTempPath()) ("GestureSign-startup-" + [Guid]::NewGuid().ToString('N') + '.txt')
$data = $report + '.data'
New-Item -ItemType Directory -Path $data | Out-Null
if ($SampleArchive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $SampleArchive).Path)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.Name -in @('Actions.gsa','Gestures.gest')) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $data $entry.Name), $true)
            }
        }
    } finally { $archive.Dispose() }
}
$proc = Start-Process -FilePath $exe -ArgumentList @('--startup-self-test', ('"{0}"' -f $report)) -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
try {
    if (!$proc.WaitForExit(30000)) { throw "Startup self-test timed out. Log: $data\GestureSign.log" }
    if (!(Test-Path -LiteralPath $report)) { throw "Startup self-test exited $($proc.ExitCode) without a report." }
    $result = Get-Content -LiteralPath $report -Raw
    if ($proc.ExitCode -ne 0 -or $result -notmatch '(?m)^Pass=True\r?$') { throw $result }
    Write-Output $result
    Write-Output "Startup log: $data\GestureSign.log"
} finally {
    if (!$proc.HasExited) { Stop-Process -Id $proc.Id }
    $proc.Dispose()
}