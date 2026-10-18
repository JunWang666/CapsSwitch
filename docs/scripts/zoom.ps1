param([string]$Src, [string]$Dst, [int]$X, [int]$Y, [int]$W, [int]$H, [int]$Zoom)
Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap -ArgumentList $Src
$c = New-Object System.Drawing.Bitmap -ArgumentList ($W * $Zoom), ($H * $Zoom)
$g = [System.Drawing.Graphics]::FromImage($c)
$dw = $W * $Zoom
$dh = $H * $Zoom
$dst = [System.Drawing.Rectangle]::new(0, 0, $dw, $dh)
$srcR = [System.Drawing.Rectangle]::new($X, $Y, $W, $H)
$g.DrawImage($b, [System.Drawing.Rectangle]$dst, [System.Drawing.Rectangle]$srcR, [System.Drawing.GraphicsUnit]::Pixel)
$c.Save($Dst, [System.Drawing.Imaging.ImageFormat]::Png)
$px = $c.GetPixel(5, 5)
Write-Output ("saved {0} sample={1},{2},{3}" -f $Dst, $px.R, $px.G, $px.B)
$g.Dispose(); $b.Dispose(); $c.Dispose()
