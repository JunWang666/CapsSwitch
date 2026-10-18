Add-Type -AssemblyName System.Drawing
function Sample($file, $xs) {
    $b = [System.Drawing.Bitmap]::FromFile($file)
    foreach ($pair in $xs) {
        $px = [int]$pair[0]; $py = [int]$pair[1]
        $c = $b.GetPixel($px, $py)
        Write-Output ("{0} ({1},{2}) = {3},{4},{5}" -f (Split-Path $file -Leaf), $px, $py, $c.R, $c.G, $c.B)
    }
    $b.Dispose()
}
Sample 'C:\DevCodes\Others\CapsSwitch\docs\shots\crop3-c.png' @(,@(60,25), @(60,70), @(60,115))
Sample 'C:\DevCodes\Others\CapsSwitch\docs\shots\crop3-d.png' @(,@(60,25), @(60,70), @(60,115))
Sample 'C:\DevCodes\Others\CapsSwitch\docs\shots\hud-list-probe.png' @(,@(30,12), @(30,40), @(30,68))
