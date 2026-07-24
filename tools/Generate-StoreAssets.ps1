param(
    [string]$Source = (Join-Path $PSScriptRoot '..\GestureSign.WinUI\Assets\logo.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\GestureSign.WinUI\Assets\Store')
)

Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)

function Write-CenteredLogo {
    param(
        [string]$Name,
        [int]$Width,
        [int]$Height,
        [int]$LogoSize
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $x = [int](($Width - $LogoSize) / 2)
        $y = [int](($Height - $LogoSize) / 2)
        $graphics.DrawImage($sourceImage, $x, $y, $LogoSize, $LogoSize)
        $bitmap.Save((Join-Path $destinationPath $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    Write-CenteredLogo 'StoreLogo.png' 50 50 40
    Write-CenteredLogo 'StoreLogo.scale-100.png' 50 50 40
    Write-CenteredLogo 'StoreLogo.scale-125.png' 63 63 50
    Write-CenteredLogo 'StoreLogo.scale-150.png' 75 75 60
    Write-CenteredLogo 'StoreLogo.scale-200.png' 100 100 80
    Write-CenteredLogo 'StoreLogo.scale-250.png' 125 125 100
    Write-CenteredLogo 'StoreLogo.scale-300.png' 150 150 120
    Write-CenteredLogo 'StoreLogo.scale-400.png' 200 200 160
    Write-CenteredLogo 'Square71x71Logo.png' 71 71 57
    Write-CenteredLogo 'Square71x71Logo.scale-200.png' 142 142 114
    Write-CenteredLogo 'Square150x150Logo.png' 150 150 120
    Write-CenteredLogo 'Square150x150Logo.scale-200.png' 300 300 240
    Write-CenteredLogo 'Square310x310Logo.png' 310 310 248
    Write-CenteredLogo 'Square310x310Logo.scale-200.png' 620 620 496
    Write-CenteredLogo 'Wide310x150Logo.png' 310 150 120
    Write-CenteredLogo 'Wide310x150Logo.scale-200.png' 620 300 240
    Write-CenteredLogo 'SplashScreen.png' 620 300 200

    # Windows 11 uses a separate unplated icon qualifier in light theme.  If it
    # is missing, the shell may put the app glyph on an accent-coloured plate in
    # taskbar jump lists and other compact surfaces.
    Get-ChildItem -LiteralPath $destinationPath -File -Filter 'Square44x44Logo.targetsize-*_altform-unplated.png' |
        ForEach-Object {
            $lightName = $_.Name.Replace('_altform-unplated.png', '_altform-lightunplated.png')
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destinationPath $lightName) -Force
        }
}
finally {
    $sourceImage.Dispose()
}
