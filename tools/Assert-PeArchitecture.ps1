param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "ARM64")]
    [string]$Architecture
)

$resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
$stream = [System.IO.File]::OpenRead($resolvedPath)
try {
    $reader = [System.IO.BinaryReader]::new($stream)
    if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
        throw "File does not have a valid DOS header: $resolvedPath"
    }

    $stream.Position = 0x3C
    $peHeaderOffset = $reader.ReadInt32()
    if ($peHeaderOffset -lt 0 -or $peHeaderOffset -gt ($stream.Length - 6)) {
        throw "File has an invalid PE header offset: $resolvedPath"
    }

    $stream.Position = $peHeaderOffset
    if ($reader.ReadUInt32() -ne 0x00004550) {
        throw "File does not have a valid PE header: $resolvedPath"
    }

    $actualMachine = $reader.ReadUInt16()
}
finally {
    $stream.Dispose()
}

$expectedMachine = if ($Architecture -eq "x64") { 0x8664 } else { 0xAA64 }
if ($actualMachine -ne $expectedMachine) {
    throw ("Kando architecture mismatch. Package={0}, Expected=0x{1:X4}, Actual=0x{2:X4}, File={3}" -f $Architecture, $expectedMachine, $actualMachine, $resolvedPath)
}

Write-Host ("Validated Kando architecture: {0} (0x{1:X4})" -f $Architecture, $actualMachine)
