# Generates app.ico from the same donut motif the UI uses, so the taskbar icon and
# the window agree. PNG-compressed ICO frames (supported since Vista).
Add-Type -AssemblyName System.Drawing
$sizes = 16,20,24,32,48,64,128,256
$frames = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded dark plate
    $pad = [Math]::Max(1, [int]($s * 0.06))
    $r   = [Math]::Max(2, [int]($s * 0.22))
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($s - 2*$pad), ($s - 2*$pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $plate = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255,26,36,56)),
        ([System.Drawing.Color]::FromArgb(255,11,17,32)), 45.0
    $g.FillPath($plate, $path)

    # the donut: full track + a green sweep, matching the app's chart
    $m = $s * 0.28
    $ring = New-Object System.Drawing.Rectangle ([int]$m), ([int]$m), ([int]($s - 2*$m)), ([int]($s - 2*$m))
    $tw = [Math]::Max(1.6, $s * 0.11)
    $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,34,44,66)), $tw
    $g.DrawEllipse($track, $ring)
    $arc = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,34,197,94)), $tw
    $arc.StartCap = 'Round'; $arc.EndCap = 'Round'
    $g.DrawArc($arc, $ring, -90, 264)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($s, $ms.ToArray())
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $sz = $f[0]; $data = $f[1]
    $bw.Write([Byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([Byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
"app.ico written: $((Get-Item (Join-Path $PSScriptRoot 'app.ico')).Length) bytes"
