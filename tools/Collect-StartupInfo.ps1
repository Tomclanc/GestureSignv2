[CmdletBinding()]
param([string]$OutputDirectory = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
# Read-only inspection. Does not launch GestureSign, enable tasks, or change startup settings.
$report = [ordered]@{
    CollectedAt = (Get-Date).ToString('o')
    Windows = [Environment]::OSVersion.VersionString
    IsAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('GestureSignV2.WinUI','GestureSignV2Daemon','GestureSignV2.PieMenu','GestureSignV2.IntentDlc','GestureSignV2','GestureSign','GestureSign.WinUI') } | ForEach-Object {
        [ordered]@{ Name=$_.ProcessName; Id=$_.Id; Path=$_.Path }
    })
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'GestureSign.lnk'
$linkInfo = [ordered]@{ Path=$shortcut; Exists=(Test-Path -LiteralPath $shortcut) }
if ($linkInfo.Exists) {
    $shell = $null; $link = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($shortcut)
        $linkInfo.Target = $link.TargetPath
        $linkInfo.TargetExists = [IO.File]::Exists($link.TargetPath)
        $linkInfo.WorkingDirectory = $link.WorkingDirectory
    } catch { $linkInfo.Error = $_.Exception.Message }
    finally {
        if ($null -ne $link) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
        if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }
}
$report.StartupShortcut = $linkInfo
$scheduler = $null; $taskFolder = $null; $task = $null
try {
    $scheduler = New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    $taskFolder = $scheduler.GetFolder('\')
    $task = $taskFolder.GetTask('StartGestureSign')
    $report.AdminTask = [ordered]@{
        Registered = $true
        Enabled = $task.Enabled
        State = $task.State
        LastRunTime = $task.LastRunTime.ToString('o')
        LastTaskResult = $task.LastTaskResult
        Xml = $task.Xml
    }
} catch {
    # Absence and access failure must stay distinguishable in the report.
    $report.AdminTask = [ordered]@{ QuerySucceeded=$false; HResult=$_.Exception.HResult; Message=$_.Exception.Message }
} finally {
    foreach ($com in @($task,$taskFolder,$scheduler)) {
        if ($null -ne $com) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($com) }
    }
}
# Preserve raw Windows startup approval data without guessing undocumented state values.
$approval = @()
foreach ($hive in @('HKCU','HKLM')) {
    $key = "${hive}:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
    try {
        $value = Get-ItemPropertyValue -LiteralPath $key -Name 'GestureSign.lnk' -ErrorAction Stop
        $approval += [ordered]@{ Key=$key; RawBytes=[BitConverter]::ToString([byte[]]$value) }
    } catch { $approval += [ordered]@{ Key=$key; Message=$_.Exception.Message } }
}
$report.WindowsStartupApproval = $approval
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory ('GestureSign-startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
Write-Output "Startup report: $path"
Write-Output 'Contains startup paths, account identifiers in task XML, task results and process information. Review before sharing.'
