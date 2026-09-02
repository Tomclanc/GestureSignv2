[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $LogPath,
    [string] $BaselinePath,
    [switch] $UpdateBaseline
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot 'warning-baseline.json'
}

function Get-WarningKeys([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Build log not found: $path"
    }

    foreach ($line in Get-Content -LiteralPath $path) {
        # MSBuild's diagnostic format is: file(line,column): warning CODE: ...
        if ($line -match '^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s+warning\s+(?<code>[A-Z]+\d+):') {
            $fullPath = $Matches.file.Trim()
            try {
                $fullPath = (Resolve-Path -LiteralPath $fullPath -ErrorAction Stop).Path
            } catch { }
            if ($fullPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
                $fullPath = $fullPath.Substring($repoRoot.Length).TrimStart('\', '/')
            }
            "{0}|{1}|{2}" -f $fullPath.Replace('\','/'), $Matches.line, $Matches.code
        }
    }
}

$expandedLogs = @($LogPath | ForEach-Object { $_ -split '[,;]' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$current = @($expandedLogs | ForEach-Object { Get-WarningKeys $_ } | Sort-Object -Unique)

if ($UpdateBaseline -or -not (Test-Path -LiteralPath $BaselinePath)) {
    $payload = [ordered]@{
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        warningCount = $current.Count
        warnings = $current
    }
    $payload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $BaselinePath -Encoding UTF8
    Write-Host "Warning baseline written: $($current.Count) unique warning locations"
    exit 0
}

$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$known = @($baseline.warnings)
$newWarnings = @($current | Where-Object { $_ -notin $known })
Write-Host "Current unique warning locations: $($current.Count)"
Write-Host "Baseline unique warning locations: $($known.Count)"
if ($newWarnings.Count -gt 0) {
    Write-Error ("Warning baseline exceeded by {0} new location(s):`n{1}" -f $newWarnings.Count, ($newWarnings -join "`n"))
    exit 1
}
Write-Host 'Warning baseline check passed (no new warning locations).'
