param(
    [string]$PublishDir = (Join-Path $PSScriptRoot "publish\GestureSign-WinUI-Preview"),
    [string]$OutputMsi = (Join-Path $PSScriptRoot "GestureSign-V2-Kando-x64.msi"),
    [string]$PackageName = "GestureSign V2",
    [string]$PackageVersion = "8.2.8",
    [string]$UpgradeCode = "6FBC49C5-1E7F-4C2E-9C68-02BA42C3B5E1",
    [string]$InstallFolderName = "GestureSign V2",
    [string]$CompressionLevel = "low",
    [switch]$SkipMajorUpgrade
)

$ErrorActionPreference = "Stop"

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

    return "msbuild.exe"
}

$publish = Resolve-Path -LiteralPath $PublishDir
$publishPath = $publish.ProviderPath
$wxsPath = Join-Path $PSScriptRoot "GestureSign.generated.kando.wxs"
$iconPath = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\GestureSign.WinUI\Assets\logo.ico")
$scope = "perUser"
$installRootDirectory = "LocalAppDataFolder"
$shortcutRegistryRoot = "HKCU"
$uninstallerProject = Join-Path $PSScriptRoot "Uninstaller\GestureSign.Uninstaller.csproj"
if (Test-Path -LiteralPath $uninstallerProject) {
    $msbuild = Find-MSBuild
    & $msbuild $uninstallerProject /p:Configuration=Release /v:m
    if ($LASTEXITCODE -ne 0) {
        throw "Uninstaller build failed with exit code $LASTEXITCODE"
    }

    $uninstallerExe = Join-Path $PSScriptRoot "Uninstaller\bin\Release\GestureSign-Uninstall.exe"
    if (Test-Path -LiteralPath $uninstallerExe) {
        Copy-Item -LiteralPath $uninstallerExe -Destination (Join-Path $publishPath "GestureSign-Uninstall.exe") -Force
    }
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
    "    <MajorUpgrade AllowSameVersionUpgrades=`"yes`" Schedule=`"afterInstallValidate`" DowngradeErrorMessage=`"A newer version of GestureSign is already installed.`" />"
}

$wxs = @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="$(Escape-Xml $PackageName)" Manufacturer="TransposonY / WinUI rebuild" Version="$(Escape-Xml $PackageVersion)" UpgradeCode="$(Escape-Xml $UpgradeCode)" Scope="$scope">
$majorUpgradeXml
    <Property Id="DISABLEROLLBACK" Value="1" />
    <Property Id="CLEANALL" Secure="yes" Value="0" />
    <MediaTemplate EmbedCab="yes" CompressionLevel="$(Escape-Xml $CompressionLevel)" />
    <Icon Id="GestureSignIcon" SourceFile="$(Escape-Xml $($iconPath.ProviderPath))" />
    <Property Id="ARPPRODUCTICON" Value="GestureSignIcon" />
    <SetProperty Id="ARPINSTALLLOCATION" Value="[INSTALLFOLDER]" After="CostFinalize" Sequence="execute" />
    <CustomAction Id="CleanupAllGestureSignData" Directory="INSTALLFOLDER" Execute="deferred" Impersonate="yes" Return="ignore" ExeCommand="powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command &quot;`$paths=@([Environment]::GetFolderPath('LocalApplicationData') + '\GestureSign V2', [Environment]::GetFolderPath('ApplicationData') + '\GestureSign V2'); foreach (`$path in `$paths) { if (Test-Path -LiteralPath `$path) { Remove-Item -LiteralPath `$path -Recurse -Force -ErrorAction SilentlyContinue } }; Remove-Item -LiteralPath 'HKCU:\Software\GestureSign V2' -Recurse -Force -ErrorAction SilentlyContinue&quot;" />
    <InstallExecuteSequence>
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
      <Shortcut Id="UninstallShortcut" Directory="ProgramMenuDir" Name="&#x5378;&#x8F7D; $(Escape-Xml $PackageName)" Target="[INSTALLFOLDER]GestureSign-Uninstall.exe" WorkingDirectory="INSTALLFOLDER" Icon="GestureSignIcon" />
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

& wix build -arch x64 -dcl $CompressionLevel -intermediatefolder (Join-Path $PSScriptRoot "obj\wix") -out $OutputMsi $wxsPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

Write-Host "MSI built: $OutputMsi"
