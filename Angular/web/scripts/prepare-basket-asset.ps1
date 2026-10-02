param(
  [string]$Source = 'src/assets/brand/wicker-basket-cutout-v3.png',
  [string]$Target = 'src/assets/brand/wicker-basket-header.png'
)
# Build optimisation only: keep the full-resolution generated master unchanged.
Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)
$targetImage = [System.Drawing.Bitmap]::new(256, [int][Math]::Round(256 * $sourceImage.Height / $sourceImage.Width), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$canvas = [System.Drawing.Graphics]::FromImage($targetImage)
try {
  $canvas.Clear([System.Drawing.Color]::Transparent)
  $canvas.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $canvas.DrawImage($sourceImage, 0, 0, $targetImage.Width, $targetImage.Height)
  $targetImage.Save([System.IO.Path]::GetFullPath($Target), [System.Drawing.Imaging.ImageFormat]::Png)
  Write-Output "Basket web asset: $Target; corner alpha: $($targetImage.GetPixel(0,0).A)"
} finally {
  $canvas.Dispose()
  $targetImage.Dispose()
  $sourceImage.Dispose()
}
