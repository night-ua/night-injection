param(
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '..\src\NightInjection.UI\Assets')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$assets = [IO.Path]::GetFullPath($AssetsDirectory)
$masterPath = Join-Path $assets 'AppIconMaster.png'
if (-not (Test-Path -LiteralPath $masterPath)) {
    throw "Missing icon master: $masterPath"
}

$master = [Drawing.Bitmap]::FromFile($masterPath)

function New-IconBitmap([int]$width, [int]$height, [double]$scale, [Drawing.Color]$background) {
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear($background)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $edge = [int]([Math]::Min($width, $height) * $scale)
        $x = [int](($width - $edge) / 2)
        $y = [int](($height - $edge) / 2)
        $graphics.DrawImage($master, [Drawing.Rectangle]::new($x, $y, $edge, $edge))
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

function Save-IconPng([string]$name, [int]$width, [int]$height, [double]$scale, [Drawing.Color]$background) {
    $bitmap = New-IconBitmap $width $height $scale $background
    try {
        $bitmap.Save((Join-Path $assets $name), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$transparent = [Drawing.Color]::Transparent
$splashBackground = [Drawing.Color]::FromArgb(255, 21, 24, 31)

Save-IconPng 'Square44x44Logo.scale-200.png' 88 88 0.82 $transparent
Save-IconPng 'Square44x44Logo.targetsize-24_altform-unplated.png' 24 24 0.88 $transparent
Save-IconPng 'Square44x44Logo.targetsize-48_altform-lightunplated.png' 48 48 0.84 $transparent
Save-IconPng 'Square150x150Logo.scale-200.png' 300 300 0.72 $transparent
Save-IconPng 'StoreLogo.png' 50 50 0.78 $transparent
Save-IconPng 'LockScreenLogo.scale-200.png' 48 48 0.78 $transparent
Save-IconPng 'Wide310x150Logo.scale-200.png' 620 300 0.62 $splashBackground
Save-IconPng 'SplashScreen.scale-200.png' 1240 600 0.50 $splashBackground

$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$encodedImages = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $iconSizes) {
    $bitmap = New-IconBitmap $size $size 0.88 $transparent
    $stream = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $encodedImages.Add($stream.ToArray())
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$iconStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + (16 * $iconSizes.Count)
    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $size = $iconSizes[$index]
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$encodedImages[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $encodedImages[$index].Length
    }
    foreach ($image in $encodedImages) {
        $writer.Write($image)
    }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.ico'), $iconStream.ToArray())
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
    $master.Dispose()
}

Write-Host "Generated Windows icon assets in $assets"
