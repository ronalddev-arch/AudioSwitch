# Generates src/AudioSwitch.App/app.ico (the .exe icon): white "Headphone" glyph from Segoe MDL2 Assets on a rounded
# orange square, as PNG-compressed frames 16-256 px. The tray icon is drawn at runtime with the same design
# (TrayIcons.cs). Run from anywhere: powershell -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\AudioSwitch.App\app.ico'

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $d = 2 * [Math]::Max(2, [int]($size * 0.22)); $w = $size - 1
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(232, 89, 12))), $path)
    $font = New-Object System.Drawing.Font 'Segoe MDL2 Assets', ($size * 0.7), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat; $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
    $g.DrawString([string][char]0xE7F6, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ($size * 0.04), $size, $size), $format)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = $sizes | ForEach-Object { , (New-Frame $_) }
$file = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $file
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                      # ICONDIRENTRY
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\app.ico', $file.ToArray())
Write-Output "Wrote $out"
