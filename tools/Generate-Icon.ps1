# Generate a Windows .ico file from the iOS/macOS PNG assets.
# Uses the pre-rendered sizes 16, 32, 48, 64, 128, 256.
param(
    [string]$SourceDir,
    [string]$OutputPath
)

$projectRoot = Split-Path $PSScriptRoot -Parent

if (-not $SourceDir) {
    $SourceDir = "$projectRoot\TypeAgent_Images\Assets.xcassets\AppIcon.appiconset\_"
}
if (-not $OutputPath) {
    $OutputPath = "$projectRoot\src\TypeGent.App\Assets\TypeGent.ico"
}

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 64, 128, 256)
$images = @()

foreach ($size in $sizes) {
    $pngPath = Join-Path $SourceDir "$size.png"
    if (-not (Test-Path $pngPath)) {
        Write-Error "Missing PNG for size $size at $pngPath"
        exit 1
    }
    $images += [System.Drawing.Bitmap]::FromFile($pngPath)
}

$outputDir = Split-Path $OutputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$icoStream = [System.IO.FileStream]::new($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($icoStream)

# ICO header
$writer.Write([System.UInt16]0)       # Reserved
$writer.Write([System.UInt16]1)       # Type: icon
$writer.Write([System.UInt16]$images.Count) # Count

$offset = 6 + 16 * $images.Count

foreach ($image in $images) {
    $width = $image.Width
    $height = $image.Height
    $dirWidth = if ($width -ge 256) { 0 } else { $width }
    $dirHeight = if ($height -ge 256) { 0 } else { $height }

    $memoryStream = [System.IO.MemoryStream]::new()
    $image.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageBytes = $memoryStream.ToArray()
    $memoryStream.Dispose()

    $writer.Write([byte]$dirWidth)
    $writer.Write([byte]$dirHeight)
    $writer.Write([byte]0)       # Colors
    $writer.Write([byte]0)       # Reserved
    $writer.Write([System.UInt16]1)     # Color planes
    $writer.Write([System.UInt16]32)    # Bits per pixel
    $writer.Write([System.UInt32]$imageBytes.Length)
    $writer.Write([System.UInt32]$offset)

    $offset += $imageBytes.Length
}

foreach ($image in $images) {
    $memoryStream = [System.IO.MemoryStream]::new()
    $image.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $writer.Write($memoryStream.ToArray())
    $memoryStream.Dispose()
    $image.Dispose()
}

$writer.Dispose()
$icoStream.Dispose()

Write-Host "Created $OutputPath"
