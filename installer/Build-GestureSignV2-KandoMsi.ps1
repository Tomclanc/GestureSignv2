param(
    [string]$PublishDir = (Join-Path $PSScriptRoot "publish\GestureSign-WinUI-Preview"),
    [string]$OutputMsi = "",
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [string]$PackageName = "GestureSign V2",
    [string]$PackageVersion = "18.1.8.0",
    [string]$UpgradeCode = "6FBC49C5-1E7F-4C2E-9C68-02BA42C3B5E1",
    [string]$InstallFolderName = "GestureSign V2",
    [string]$CompressionLevel = "high",
    [switch]$SkipMajorUpgrade,
    [switch]$SkipPayloadBuild
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputMsi)) {
    $OutputMsi = Join-Path $PSScriptRoot "GestureSign-V2-$Architecture.msi"
}

function Get-StableId {
    param(
        [string]$Prefix,
        [string]$Value
    )

    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
        $hash = $sha1.ComputeHash($bytes)
        $hex = -join ($hash | ForEach-Object { $_.ToString("x2") })
        return "$Prefix$($hex.Substring(0, 24))"
    }
    finally {
        $sha1.Dispose()
    }
}

function Escape-Xml {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseUri = [System.Uri]((Resolve-Path -LiteralPath $BasePath).ProviderPath.TrimEnd('\') + '\')
    $targetFullPath = if (Test-Path -LiteralPath $TargetPath) {
        (Resolve-Path -LiteralPath $TargetPath).ProviderPath
    }
    else {
        [System.IO.Path]::GetFullPath($TargetPath)
    }
    if ([string]::Equals(
        $baseUri.LocalPath.TrimEnd('\'),
        $targetFullPath.TrimEnd('\'),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }
    $targetUri = [System.Uri]$targetFullPath
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
        if ($msbuild -and (Test-Path -LiteralPath $msbuild)) {
            return $msbuild
        }
    }

    $fallback = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path -LiteralPath $fallback) {
        return $fallback
    }

    return $null
}

$publishPath = [System.IO.Path]::GetFullPath($PublishDir)
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$publishBackendPath = Join-Path $publishPath "Backend"
$wxsPath = Join-Path $PSScriptRoot "GestureSign.generated.kando.wxs"
$iconPath = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\GestureSign.WinUI\Assets\logo.ico")
$scope = "perUser"
$installRootDirectory = "LocalAppDataFolder"
$shortcutRegistryRoot = "HKCU"
$msbuild = Find-MSBuild

if (-not $SkipPayloadBuild) {
$backendProject = Join-Path $repoRoot.ProviderPath "GestureSign.Daemon\GestureSign.Daemon.csproj"
$backendOutputPath = Join-Path $repoRoot.ProviderPath "bin\Release"
if (Test-Path -LiteralPath $backendOutputPath) {
    Remove-Item -LiteralPath $backendOutputPath -Recurse -Force
}
& dotnet publish $backendProject -c Release -r "win-$Architecture" --self-contained false -o $backendOutputPath /p:PlatformTarget=$Architecture /m:1 /nr:false /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 $Architecture backend publish failed with exit code $LASTEXITCODE"
}

if (!(Test-Path -LiteralPath (Join-Path $backendOutputPath "GestureSign.exe"))) {
    throw "Backend build output is missing GestureSign.exe: $backendOutputPath"
}

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}
New-Item -ItemType Directory -Path $publishPath | Out-Null

$winUiProject = Join-Path $repoRoot.ProviderPath "GestureSign.WinUI\GestureSign.WinUI.csproj"
$winUiOutputPath = Join-Path $repoRoot.ProviderPath "GestureSign.WinUI\bin\$Architecture\Release\net10.0-windows10.0.26100.0\win-$Architecture\publish"
$platformTarget = if ($Architecture -eq "arm64") { "ARM64" } else { "x64" }
if (Test-Path -LiteralPath $winUiOutputPath) {
    Remove-Item -LiteralPath $winUiOutputPath -Recurse -Force
}
& dotnet publish $winUiProject -c Release -r "win-$Architecture" --self-contained false -o $winUiOutputPath /p:Platform=$platformTarget /p:PlatformTarget=$platformTarget /p:StorePackage=false /p:WindowsPackageType=None /m:1 /nr:false /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "WinUI build failed with exit code $LASTEXITCODE"
}

if (!(Test-Path -LiteralPath (Join-Path $winUiOutputPath "GestureSign.WinUI.exe"))) {
    throw "WinUI build output is missing GestureSign.WinUI.exe: $winUiOutputPath"
}

Copy-Item -Path (Join-Path $winUiOutputPath "*") -Destination $publishPath -Recurse -Force
New-Item -ItemType Directory -Path $publishBackendPath | Out-Null
Copy-Item -Path (Join-Path $backendOutputPath "*") -Destination $publishBackendPath -Recurse -Force
foreach ($requiredBackendFile in @("GestureSign.exe", "GestureSign.Common.dll", "GestureSign.CorePlugins.dll", "ManagedWinapi.dll", "WindowsInput.dll")) {
    if (!(Test-Path -LiteralPath (Join-Path $publishBackendPath $requiredBackendFile))) {
        throw "Backend file is missing from publish directory: $requiredBackendFile"
    }
}

foreach ($requiredWinUiFile in @("GestureSign.WinUI.exe", "GestureSign.WinUI.dll")) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $requiredWinUiFile))) {
        throw "WinUI file is missing from publish directory: $requiredWinUiFile"
    }
}

$uninstallerProject = Join-Path $PSScriptRoot "Uninstaller\GestureSign.Uninstaller.csproj"
if (!(Test-Path -LiteralPath $uninstallerProject)) {
    throw "Uninstaller project is missing: $uninstallerProject"
}
$uninstallerOutputPath = Join-Path $PSScriptRoot "Uninstaller\bin\publish\win-$Architecture"
& dotnet publish $uninstallerProject -c Release -r "win-$Architecture" --self-contained false -o $uninstallerOutputPath /p:PlatformTarget=$platformTarget /p:PublishSingleFile=true /m:1 /nr:false /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 $Architecture uninstaller publish failed with exit code $LASTEXITCODE"
}

$uninstallerExe = Join-Path $uninstallerOutputPath "GestureSign-Uninstall.exe"
if (!(Test-Path -LiteralPath $uninstallerExe)) {
    throw "Uninstaller publish output is missing: $uninstallerExe"
}
Copy-Item -LiteralPath $uninstallerExe -Destination (Join-Path $publishPath "GestureSign-Uninstall.exe") -Force

$updaterProject = Join-Path $PSScriptRoot "Updater\GestureSign.Updater.csproj"
if (!(Test-Path -LiteralPath $updaterProject)) {
    throw "Updater project is missing: $updaterProject"
}
$updaterExe = Join-Path $PSScriptRoot "Updater\bin\Release\GestureSign-Updater.exe"
if ($msbuild) {
    & $msbuild $updaterProject /p:Configuration=Release /v:m
}
else {
    & dotnet msbuild $updaterProject /p:Configuration=Release /v:m
}
if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $updaterExe) {
        Write-Warning "Updater build failed with exit code $LASTEXITCODE; reusing the existing unchanged updater binary: $updaterExe"
    }
    else {
        throw "Updater build failed with exit code $LASTEXITCODE and no existing updater binary is available"
    }
}

if (!(Test-Path -LiteralPath $updaterExe)) {
    throw "Updater build output is missing: $updaterExe"
}
Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $publishPath "GestureSign-Updater.exe") -Force

# Remove files used solely for debugging self-contained .NET deployments. These
# files are not required for normal startup, crash logging, or updating.
foreach ($payloadRoot in @($publishPath, $publishBackendPath)) {
    Get-ChildItem -LiteralPath $payloadRoot -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    foreach ($diagnosticFile in @("createdump.exe", "clretwrc.dll", "mscordaccore.dll", "mscordbi.dll")) {
        $diagnosticPath = Join-Path $payloadRoot $diagnosticFile
        if (Test-Path -LiteralPath $diagnosticPath) {
            Remove-Item -LiteralPath $diagnosticPath -Force
        }
    }
    Get-ChildItem -LiteralPath $payloadRoot -Filter "mscordaccore_*.dll" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}
}
elseif (!(Test-Path -LiteralPath $publishPath)) {
    throw "Prepared publish directory is missing: $publishPath"
}

# Kando is an on-demand component. Never inherit a stale bundled copy from a previous publish.
$staleKandoPath = Join-Path $publishPath "Kando"
if (Test-Path -LiteralPath $staleKandoPath) {
    Remove-Item -LiteralPath $staleKandoPath -Recurse -Force
}

$files = Get-ChildItem -LiteralPath $publishPath -Recurse -File | Sort-Object FullName
if ($files.Count -eq 0) {
    throw "No files found in publish directory: $publishPath"
}

$directoryIds = @{
    "" = "INSTALLFOLDER"
}

$directories = New-Object System.Collections.Generic.SortedSet[string]
foreach ($file in $files) {
    $relative = Get-RelativePath $publishPath $file.DirectoryName
    if ($relative -eq ".") {
        continue
    }

    $parts = $relative -split '[\\/]'
    $current = ""
    foreach ($part in $parts) {
        $current = if ($current.Length -eq 0) { $part } else { Join-Path $current $part }
        [void]$directories.Add($current)
        if (-not $directoryIds.ContainsKey($current)) {
            $directoryIds[$current] = Get-StableId "DIR_" $current
        }
    }
}

$components = New-Object System.Collections.Generic.List[string]
$componentRefs = New-Object System.Collections.Generic.List[string]
$cleanupComponentRefs = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $relative = Get-RelativePath $publishPath $file.FullName
    $relativeDir = [System.IO.Path]::GetDirectoryName($relative)
    if ($null -eq $relativeDir) {
        $relativeDir = ""
    }

    $componentId = Get-StableId "CMP_" $relative
    $fileId = Get-StableId "FILE_" $relative
    $directoryId = $directoryIds[$relativeDir]
    $source = Join-Path $publishPath $relative

    $components.Add("    <Component Id=`"$componentId`" Directory=`"$directoryId`" Guid=`"*`">")
    $components.Add("      <File Id=`"$fileId`" Source=`"$(Escape-Xml $source)`" KeyPath=`"yes`" />")
    $components.Add("    </Component>")
    $componentRefs.Add("      <ComponentRef Id=`"$componentId`" />")
}

foreach ($directory in $directories) {
    if ($directory -ne "Kando" -and -not $directory.StartsWith("Kando\", [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    $directoryId = $directoryIds[$directory]
    $componentId = Get-StableId "CMP_CLEAN_" $directory
    $removeId = Get-StableId "RM_" $directory
    $registryName = "clean_" + ((Get-StableId "" $directory).Substring(0, 12))
    $components.Add("    <Component Id=`"$componentId`" Directory=`"$directoryId`" Guid=`"*`">")
    $components.Add("      <RemoveFile Id=`"$removeId`" Name=`"*.*`" On=`"install`" />")
    $components.Add("      <RegistryValue Root=`"$shortcutRegistryRoot`" Key=`"Software\GestureSign V2\Cleanup`" Name=`"$registryName`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
    $components.Add("    </Component>")
    $cleanupComponentRefs.Add("      <ComponentRef Id=`"$componentId`" />")
}

function Add-DirectoryXml {
    param(
        [System.Collections.Generic.List[string]]$Output,
        [string]$Parent,
        [int]$Indent
    )

    $children = $directories | Where-Object {
        $childParent = [System.IO.Path]::GetDirectoryName($_)
        if ($null -eq $childParent) {
            $childParent = ""
        }
        $childParent -eq $Parent
    }

    foreach ($child in $children) {
        $name = Split-Path $child -Leaf
        $spaces = " " * $Indent
        $grandChildren = $directories | Where-Object {
            $grandParent = [System.IO.Path]::GetDirectoryName($_)
            if ($null -eq $grandParent) {
                $grandParent = ""
            }
            $grandParent -eq $child
        }

        if ($grandChildren.Count -eq 0) {
            $Output.Add("$spaces<Directory Id=`"$($directoryIds[$child])`" Name=`"$(Escape-Xml $name)`" />")
        }
        else {
            $Output.Add("$spaces<Directory Id=`"$($directoryIds[$child])`" Name=`"$(Escape-Xml $name)`">")
            Add-DirectoryXml -Output $Output -Parent $child -Indent ($Indent + 2)
            $Output.Add("$spaces</Directory>")
        }
    }
}

$directoryXml = New-Object System.Collections.Generic.List[string]
Add-DirectoryXml -Output $directoryXml -Parent "" -Indent 8
$majorUpgradeXml = if ($SkipMajorUpgrade) {
    "    <!-- Major upgrade removal is intentionally disabled for this build. -->"
}
else {
    "    <MajorUpgrade AllowSameVersionUpgrades=`"yes`" Schedule=`"afterInstallInitialize`" DowngradeErrorMessage=`"A newer version of GestureSign is already installed.`" />"
}

$wxs = @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="$(Escape-Xml $PackageName)" Manufacturer="TransposonY / WinUI rebuild" Version="$(Escape-Xml $PackageVersion)" UpgradeCode="$(Escape-Xml $UpgradeCode)" Scope="$scope">
$majorUpgradeXml
    <Property Id="CLEANALL" Secure="yes" Value="0" />
    <MediaTemplate EmbedCab="yes" CompressionLevel="$(Escape-Xml $CompressionLevel)" />
    <Icon Id="GestureSignIcon" SourceFile="$(Escape-Xml $($iconPath.ProviderPath))" />
    <Property Id="ARPPRODUCTICON" Value="GestureSignIcon" />
    <SetProperty Id="ARPINSTALLLOCATION" Value="[INSTALLFOLDER]" After="CostFinalize" Sequence="execute" />
    <CustomAction Id="CleanupAllGestureSignData" Directory="SystemFolder" Execute="deferred" Impersonate="yes" Return="ignore" ExeCommand="powershell.exe -NoP -W Hidden -C &quot;`$p=`$env:LOCALAPPDATA+'\GestureSign V2',`$env:APPDATA+'\GestureSign V2','HKCU:\Software\GestureSign V2'; Remove-Item -LiteralPath `$p -Recurse -Force -EA 0&quot;" />
    <CustomAction Id="PreserveExistingKando" Directory="SystemFolder" Execute="immediate" Impersonate="yes" Return="ignore" ExeCommand="powershell.exe -NoP -W Hidden -C &quot;`$s='[INSTALLFOLDER]Kando';`$r=`$env:LOCALAPPDATA+'\GestureSign V2\Components';`$d=`$r+'\Kando';`$m=`$r+'\Kando.removed';if((Test-Path -LiteralPath `$s) -and !(Test-Path -LiteralPath `$d) -and !(Test-Path -LiteralPath `$m)){New-Item -ItemType Directory -Path `$r -Force|Out-Null;Copy-Item -LiteralPath `$s -Destination `$d -Recurse -Force}&quot;" />
    <InstallExecuteSequence>
      <Custom Action="PreserveExistingKando" Before="RemoveExistingProducts" Condition="WIX_UPGRADE_DETECTED" />
      <Custom Action="CleanupAllGestureSignData" After="RemoveFiles" Condition="REMOVE=&quot;ALL&quot; AND CLEANALL=&quot;1&quot; AND NOT UPGRADINGPRODUCTCODE" />
    </InstallExecuteSequence>

    <StandardDirectory Id="$installRootDirectory">
      <Directory Id="INSTALLFOLDER" Name="$(Escape-Xml $InstallFolderName)">
$($directoryXml -join "`r`n")
      </Directory>
    </StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ProgramMenuDir" Name="$(Escape-Xml $PackageName)" />
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder" />

$($components -join "`r`n")
    <Component Id="StartMenuShortcutComponent" Directory="ProgramMenuDir" Guid="*">
      <Shortcut Id="StartMenuShortcut" Directory="ProgramMenuDir" Name="$(Escape-Xml $PackageName)" Target="[INSTALLFOLDER]GestureSign.WinUI.exe" WorkingDirectory="INSTALLFOLDER" Icon="GestureSignIcon" />
      <RemoveFolder Id="RemoveProgramMenuDir" Directory="ProgramMenuDir" On="uninstall" />
      <RegistryValue Root="$shortcutRegistryRoot" Key="Software\GestureSign V2" Name="startMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
    </Component>

    <Component Id="DesktopShortcutComponent" Directory="DesktopFolder" Guid="*">
      <Shortcut Id="DesktopShortcut" Directory="DesktopFolder" Name="$(Escape-Xml $PackageName)" Target="[INSTALLFOLDER]GestureSign.WinUI.exe" WorkingDirectory="INSTALLFOLDER" Icon="GestureSignIcon" />
      <RegistryValue Root="$shortcutRegistryRoot" Key="Software\GestureSign V2" Name="desktopShortcut" Type="integer" Value="1" KeyPath="yes" />
    </Component>

    <Feature Id="MainFeature" Title="$(Escape-Xml $PackageName)" Level="1">
$($componentRefs -join "`r`n")
$($cleanupComponentRefs -join "`r`n")
      <ComponentRef Id="StartMenuShortcutComponent" />
      <ComponentRef Id="DesktopShortcutComponent" />
    </Feature>
  </Package>
</Wix>
"@

[System.IO.File]::WriteAllText($wxsPath, $wxs, [System.Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $OutputMsi) {
    Remove-Item -LiteralPath $OutputMsi -Force
}

$wixCommand = Get-Command wix -ErrorAction SilentlyContinue
if ($wixCommand) {
    & $wixCommand.Source build -arch $Architecture -dcl $CompressionLevel -intermediatefolder (Join-Path $PSScriptRoot "obj\wix") -out $OutputMsi $wxsPath
}
else {
    & dotnet tool run wix -- build -arch $Architecture -dcl $CompressionLevel -intermediatefolder (Join-Path $PSScriptRoot "obj\wix") -out $OutputMsi $wxsPath
}
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

Write-Host "MSI built: $OutputMsi"
