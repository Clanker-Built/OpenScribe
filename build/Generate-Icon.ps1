<#
.SYNOPSIS
    Generates the OpenScribe application icon (.ico) with an "OS" monogram.
.DESCRIPTION
    Creates a multi-size .ico file with an abstract "OS" monogram
    using OpenScribe brand colors (navy background, accent blue letters).
#>

[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot "..\src\OpenScribe.App\Assets\OpenScribe.ico"
}

$outputDir = Split-Path $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Brand colors
$navyColor = [System.Drawing.Color]::FromArgb(255, 31, 56, 100)    # #1F3864
$accentColor = [System.Drawing.Color]::FromArgb(255, 68, 114, 196)  # #4472C4
$lightAccent = [System.Drawing.Color]::FromArgb(255, 120, 160, 220) # lighter accent for depth

function New-IconImage {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Transparent background
    [void]$g.Clear([System.Drawing.Color]::Transparent)

    # Draw rounded rectangle background
    $navyBrush = New-Object System.Drawing.SolidBrush($navyColor)
    [int]$margin = [Math]::Max(1, [int]($Size * 0.02))
    [int]$cornerRadius = [int]($Size * 0.18)
    $rect = New-Object System.Drawing.Rectangle($margin, $margin, ($Size - 2 * $margin), ($Size - 2 * $margin))

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    [int]$d = $cornerRadius * 2
    if ($d -lt 2) { $d = 2 }
    # Top-left
    [void]$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    # Top-right
    [void]$path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    # Bottom-right
    [void]$path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    # Bottom-left
    [void]$path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    [void]$path.CloseFigure()

    [void]$g.FillPath($navyBrush, $path)

    # Draw "OS" text
    $accentBrush = New-Object System.Drawing.SolidBrush($accentColor)
    [float]$fontSize = [Math]::Max(6, $Size * 0.42)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

    $textRect = New-Object System.Drawing.RectangleF(0, [float]($Size * 0.02), [float]$Size, [float]$Size)
    [void]$g.DrawString("OS", $font, $accentBrush, $textRect, $sf)

    # Cleanup
    $sf.Dispose()
    $font.Dispose()
    $accentBrush.Dispose()
    $navyBrush.Dispose()
    $path.Dispose()
    $g.Dispose()

    return $bmp
}

function Write-IcoFile {
    param(
        [System.Drawing.Bitmap[]]$Images,
        [string]$Path
    )

    $ms = New-Object System.IO.MemoryStream

    $writer = New-Object System.IO.BinaryWriter($ms)

    # ICO header
    $writer.Write([UInt16]0)              # Reserved
    $writer.Write([UInt16]1)              # Type: ICO
    $writer.Write([UInt16]$Images.Count)  # Number of images

    # Calculate data offset (header=6 + entries=16*count)
    $dataOffset = 6 + (16 * $Images.Count)

    # Collect PNG data for each image
    $pngDataList = @()
    foreach ($img in $Images) {
        $pngStream = New-Object System.IO.MemoryStream
        $img.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngDataList += , $pngStream.ToArray()
        $pngStream.Dispose()
    }

    # Write directory entries
    for ($i = 0; $i -lt $Images.Count; $i++) {
        $img = $Images[$i]
        $pngData = $pngDataList[$i]

        $w = if ($img.Width -ge 256) { 0 } else { $img.Width }
        $h = if ($img.Height -ge 256) { 0 } else { $img.Height }

        $writer.Write([byte]$w)           # Width
        $writer.Write([byte]$h)           # Height
        $writer.Write([byte]0)            # Color palette
        $writer.Write([byte]0)            # Reserved
        $writer.Write([UInt16]1)          # Color planes
        $writer.Write([UInt16]32)         # Bits per pixel
        $writer.Write([UInt32]$pngData.Length)  # Size of image data
        $writer.Write([UInt32]$dataOffset)      # Offset to image data

        $dataOffset += $pngData.Length
    }

    # Write image data
    foreach ($pngData in $pngDataList) {
        $writer.Write($pngData)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())

    $writer.Dispose()
    $ms.Dispose()
}

Write-Host "Generating OpenScribe icon..." -ForegroundColor Cyan

# Generate at standard icon sizes
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$imageList = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()

foreach ($size in $sizes) {
    Write-Host "  Rendering ${size}x${size}..." -ForegroundColor Gray
    $img = New-IconImage -Size $size
    $imageList.Add($img)
}

Write-IcoFile -Images $imageList.ToArray() -Path $OutputPath

Write-Host "Icon saved to: $OutputPath" -ForegroundColor Green

# Cleanup bitmaps
foreach ($img in $imageList) {
    $img.Dispose()
}
