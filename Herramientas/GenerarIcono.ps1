# Genera un icono .ico multi-resolucion para Mis Finanzas usando System.Drawing.
Add-Type -AssemblyName System.Drawing

$outIco = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\MisFinanzas\Recursos\app.ico"))
$sizes  = @(16, 32, 48, 64, 128, 256)

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Fondo con degradado azul (paleta de la app)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $c1 = [System.Drawing.Color]::FromArgb(74, 144, 226)   # #4A90E2
    $c2 = [System.Drawing.Color]::FromArgb(45, 55, 72)      # #2D3748
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $g.FillRectangle($brush, $rect)

    # Simbolo de moneda "$" centrado en blanco
    $fontSize = [float]($size * 0.6)
    $font  = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $fmt   = New-Object System.Drawing.StringFormat
    $fmt.Alignment     = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rectF = New-Object System.Drawing.RectangleF(0, 0, [float]$size, [float]$size)
    $g.DrawString('$', $font, $white, $rectF, $fmt)

    $g.Dispose()
    return $bmp
}

# Construir el archivo .ico manualmente (formato ICO con PNG embebido por tamano)
$pngStreams = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += ,($ms.ToArray())
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($outIco)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0)              # Reserved
$bw.Write([uint16]1)              # Type = icon
$bw.Write([uint16]$sizes.Count)   # Count

# Offset inicial de datos = 6 (ICONDIR) + 16 * count (entries)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s   = $sizes[$i]
    $len = $pngStreams[$i].Length
    $wh  = if ($s -ge 256) { 0 } else { $s }   # 0 significa 256 en formato ICO
    $bw.Write([byte]$wh)   # width
    $bw.Write([byte]$wh)   # height
    $bw.Write([byte]0)     # color count
    $bw.Write([byte]0)     # reserved
    $bw.Write([uint16]1)   # planes
    $bw.Write([uint16]32)  # bit count
    $bw.Write([uint32]$len)     # bytes of data
    $bw.Write([uint32]$offset)  # offset
    $offset += $len
}
foreach ($png in $pngStreams) { $bw.Write($png) }

$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "Icono generado en: $outIco"
