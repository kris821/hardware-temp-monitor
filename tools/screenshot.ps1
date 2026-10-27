param(
    [int]$x, [int]$y, [int]$w, [int]$h,
    [string]$out = "D:\WorkBuddyProjects\TempMonitor\docs\screenshot.png"
)
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
"saved: $out ({0}x{1})" -f $w, $h
