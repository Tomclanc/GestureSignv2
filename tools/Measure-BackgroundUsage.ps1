[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [ValidateRange(5,3600)][int]$Seconds = 60,
    [string]$Scenario = 'idle',
    [string]$OutputPath = (Join-Path (Get-Location) 'GestureSign-performance.csv')
)
$ErrorActionPreference = 'Stop'
$initial = Get-Process -Id $TargetProcessId
$startCpu = $initial.TotalProcessorTime.TotalMilliseconds
$watch = [Diagnostics.Stopwatch]::StartNew()
$samples = [Collections.Generic.List[object]]::new()
for ($sampleIndex = 0; $sampleIndex -lt $Seconds; $sampleIndex++) {
    Start-Sleep -Seconds 1
    $process = Get-Process -Id $TargetProcessId
    $samples.Add([pscustomobject]@{
        Scenario = $Scenario
        ElapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        CpuMilliseconds = [Math]::Round($process.TotalProcessorTime.TotalMilliseconds - $startCpu, 2)
        WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        Threads = $process.Threads.Count
        Handles = $process.HandleCount
    })
}
$samples | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$cpu = 100 * ($process.TotalProcessorTime.TotalMilliseconds - $startCpu) / $watch.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount
Write-Output ('Average CPU (all logical processors): {0:N3}%' -f $cpu)
Write-Output ('Saved: ' + [IO.Path]::GetFullPath($OutputPath))
Write-Output 'This measures CPU and memory, not battery energy. Compare identical-duration idle, scrolling, AI-off and AI-on runs.'
