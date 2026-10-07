[CmdletBinding()]
param(
    [ValidateSet('x64','arm64')][string]$Architecture = 'arm64',
    [string]$Version = '18.3.2-issue6.1',
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$SampleArchive,
    [string]$ReadmePath = "$PSScriptRoot\..\docs\releases\issue6-portable-readme.txt"
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
$payload = Join-Path $output "portable-$Architecture"
if (Test-Path -LiteralPath $payload) { throw "Use a fresh output directory: $payload" }
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
& dotnet publish "$repo\GestureSign.Daemon\GestureSign.Daemon.csproj" -c Release -r "win-$Architecture" --self-contained true -o "$payload\Backend" /p:Platform=$platform /p:PlatformTarget=$platform /p:PublishReadyToRun=false /m:1 /nr:false /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Daemon publish failed.' }
& dotnet publish "$repo\GestureSign.WinUI\GestureSign.WinUI.csproj" -c Release -r "win-$Architecture" --self-contained true -o $payload /p:Platform=$platform /p:PlatformTarget=$platform /p:SelfContained=true /p:WindowsAppSDKSelfContained=true /p:WindowsPackageType=None /p:StorePackage=false /p:PublishReadyToRun=false /m:1 /nr:false /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed.' }
Get-ChildItem -LiteralPath $payload -Recurse -File -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Copy-Item -LiteralPath $ReadmePath -Destination "$payload\README.txt"
Copy-Item -LiteralPath "$repo\tools\Collect-SupportInfo.ps1" -Destination $payload
Copy-Item -LiteralPath "$repo\tools\Recover-Input.ps1" -Destination $payload
if ($SampleArchive) {
    New-Item -ItemType Directory -Path "$payload\Sample-config" | Out-Null
    Copy-Item -LiteralPath $SampleArchive -Destination "$payload\Sample-config\GestureSign-Sample-V2.ges"
}
& "$repo\tools\Test-BackendArchitecture.ps1" -PackagePath $payload -Architecture $platform
& "$repo\tools\Test-PortablePackage.ps1" -PackagePath $payload
if ($Architecture -eq 'x64') { & "$repo\tools\Test-DaemonStartup.ps1" -Executable "$payload\Backend\GestureSign.exe" -SampleArchive $SampleArchive }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $output "GestureSign-V2-$Version-$Architecture-portable.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip) | Set-Content ($zip + '.sha256')
Write-Output "Portable package: $zip"