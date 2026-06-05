param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\SonarGameBar.Widget')
)

Add-Type -AssemblyName System.Drawing.Common

$assets = Join-Path $OutputRoot 'Assets'
$gameBarIcons = Join-Path $OutputRoot 'GameBar\Icons'
New-Item -ItemType Directory -Force -Path $assets, $gameBarIcons | Out-Null

function New-SonarIcon {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [bool]$Transparent = $false
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    if ($Transparent) {
        $graphics.Clear([System.Drawing.Color]::Transparent)
    } else {
        $graphics.Clear([System.Drawing.Color]::FromArgb(23, 26, 31))
    }

    $size = [Math]::Min($Width, $Height)
    $left = ($Width - $size) / 2
    $top = ($Height - $size) / 2
    $margin = $size * 0.19
    $lineWidth = [Math]::Max(1.5, $size * 0.07)
    $knobRadius = [Math]::Max(1.5, $size * 0.095)

    $linePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 244, 247, 250), $lineWidth)
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 53, 214, 199))
    $warmBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 200, 87))

    $x1 = $left + $margin
    $x2 = $left + $size - $margin
    $ys = @(($top + $size * 0.32), ($top + $size * 0.50), ($top + $size * 0.68))
    $knobs = @(($left + $size * 0.40), ($left + $size * 0.63), ($left + $size * 0.48))

    for ($i = 0; $i -lt 3; $i++) {
        $graphics.DrawLine($linePen, [float]$x1, [float]$ys[$i], [float]$x2, [float]$ys[$i])
        $brush = if ($i -eq 1) { $warmBrush } else { $accentBrush }
        $graphics.FillEllipse(
            $brush,
            [float]($knobs[$i] - $knobRadius),
            [float]($ys[$i] - $knobRadius),
            [float]($knobRadius * 2),
            [float]($knobRadius * 2))
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $warmBrush.Dispose()
    $accentBrush.Dispose()
    $linePen.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

New-SonarIcon 48 48 (Join-Path $assets 'LockScreenLogo.scale-200.png')
New-SonarIcon 1240 600 (Join-Path $assets 'SplashScreen.scale-200.png')
New-SonarIcon 300 300 (Join-Path $assets 'Square150x150Logo.scale-200.png')
New-SonarIcon 88 88 (Join-Path $assets 'Square44x44Logo.scale-200.png')
New-SonarIcon 24 24 (Join-Path $assets 'Square44x44Logo.targetsize-24_altform-unplated.png') $true
New-SonarIcon 50 50 (Join-Path $assets 'StoreLogo.png')
New-SonarIcon 620 300 (Join-Path $assets 'Wide310x150Logo.scale-200.png')

foreach ($size in 16, 20, 24, 32, 44, 256) {
    New-SonarIcon $size $size (Join-Path $gameBarIcons "icon.targetsize-$size.png") $true
}
