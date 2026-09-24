[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PackagePath,

    [int] $MinimumFileCount = 1
)

$ErrorActionPreference = 'Stop'

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedPackagePath -PathType Container)) {
    throw "Portable package path is not a directory: $resolvedPackagePath"
}

$files = @(Get-ChildItem -LiteralPath $resolvedPackagePath -Recurse -File)
if ($files.Count -lt $MinimumFileCount) {
    throw "Portable package contains $($files.Count) files; expected at least $MinimumFileCount."
}

$requiredFiles = @(
    'GestureSign.WinUI.exe',
    'Backend\GestureSign.exe',
    'GestureSign.WinUI.dll',
    'Backend\GestureSign.dll'
)
foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $resolvedPackagePath $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required portable package file is missing: $requiredFile"
    }
}

# Kando is an optional external integration and must not be bundled in the
# portable package. Documentation/assets may mention it, but executable
# payloads and libraries must not be present.
# System.Diagnostics assemblies are required framework libraries in the self-contained intent component.
$forbidden = @($files | Where-Object {
    $_.FullName.Substring($resolvedPackagePath.Length + 1) -match '(?i)(^|[\\/])Components[\\/]IntentDlc([\\/]|$)' -or
    $_.Name -match '(?i)^(GestureSign\.(IntentDlc|IntentLearning)|Microsoft\.(ML\.OnnxRuntime|Windows\.AI\.MachineLearning)|onnxruntime|DirectML).*\.(exe|dll|zip)$' -or
    $_.Extension -in '.pdb', '.dmp', '.diag' -or
    $_.Name -match '(?i)^kando.*\.(exe|dll|zip)$' -or
    ($_.Name -match '(?i)(diagnostic|diagnostics|trace|crashdump)' -and $_.Name -notmatch '^System\.Diagnostics\.[A-Za-z]+\.dll$')
})
if ($forbidden.Count -gt 0) {
    $names = ($forbidden | ForEach-Object { $_.FullName.Substring($resolvedPackagePath.Length + 1) }) -join ', '
    throw "Portable package contains forbidden diagnostic or bundled Kando/AI component files: $names"
}

[pscustomobject]@{
    PackagePath = $resolvedPackagePath
    FileCount = $files.Count
    RequiredFiles = $requiredFiles.Count
    ForbiddenFiles = 0
    Status = 'PASS'
}
