<#
.SYNOPSIS
Gera src/VNForge.App/Assets/VNForge.ico (16, 24, 32, 48 e 256 px) a partir do Brand.Seal.
.DESCRIPTION
Desenha o selo cheio do design system (docs/design/README-design-system.md, seção Marca): placa em brand-shu (tema claro)
inclinada -4°, com o glifo 訳 do Brand.Seal em tom de papel por cima. O traçado é lido de Assets/Brand.axaml, então
mudar a marca lá e rodar este script refaz o ícone. Cada tamanho é renderizado em 4x e reduzido, e vai no .ico como PNG.
Usa System.Drawing: roda no Windows (PowerShell 7).
#>
[CmdletBinding()]
param(
    [string]$Brand = (Join-Path $PSScriptRoot '..\src\VNForge.App\Assets\Brand.axaml'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\VNForge.App\Assets\VNForge.ico'),
    [string]$PlateColor = '#B8402E',
    [string]$GlyphColor = '#F8F4EC'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 16, 24, 32, 48, 256
$Supersample = 4
$Grid = 48.0       # lado da grade do selo no design system
$Tilt = -4.0       # graus, como no selo da interface
$PlateInset = 3.0  # a placa vai de 3 a 45 na grade; inclinada, ainda cabe nos 48
$PlateRadius = 8.0

function Read-SealPath([string]$File) {
    $xaml = Get-Content -LiteralPath $File -Raw
    if ($xaml -notmatch '<StreamGeometry x:Key="Brand\.Seal">([^<]+)</StreamGeometry>') { throw "Brand.Seal não encontrado em $File" }
    $Matches[1]
}

# Converte o traçado (M L H V Q C Z, coordenadas absolutas) em GraphicsPath. Preenchimento par-ímpar, como o StreamGeometry do Avalonia.
function ConvertTo-GraphicsPath([string]$Data) {
    $script:tokens = @([regex]::Matches($Data, '[MLHVQCZ]|-?(?:\d+\.?\d*|\.\d+)') | ForEach-Object Value)
    $script:i = 0
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Alternate)
    $cx = 0.0; $cy = 0.0; $sx = 0.0; $sy = 0.0
    $cmd = ''
    function Next-Number { [double]::Parse($script:tokens[$script:i++], [cultureinfo]::InvariantCulture) }
    while ($script:i -lt $script:tokens.Count) {
        if ($script:tokens[$script:i] -match '^[A-Z]$') { $cmd = $script:tokens[$script:i++] }
        switch ($cmd) {
            'M' { $path.StartFigure(); $cx = Next-Number; $cy = Next-Number; $sx = $cx; $sy = $cy; $cmd = 'L' }
            'L' { $x = Next-Number; $y = Next-Number; $path.AddLine([single]$cx, [single]$cy, [single]$x, [single]$y); $cx = $x; $cy = $y }
            'H' { $x = Next-Number; $path.AddLine([single]$cx, [single]$cy, [single]$x, [single]$cy); $cx = $x }
            'V' { $y = Next-Number; $path.AddLine([single]$cx, [single]$cy, [single]$cx, [single]$y); $cy = $y }
            'Q' {
                $qx = Next-Number; $qy = Next-Number; $x = Next-Number; $y = Next-Number
                # quadrática -> cúbica equivalente
                $path.AddBezier([single]$cx, [single]$cy,
                    [single]($cx + 2 / 3 * ($qx - $cx)), [single]($cy + 2 / 3 * ($qy - $cy)),
                    [single]($x + 2 / 3 * ($qx - $x)), [single]($y + 2 / 3 * ($qy - $y)),
                    [single]$x, [single]$y)
                $cx = $x; $cy = $y
            }
            'C' {
                $x1 = Next-Number; $y1 = Next-Number; $x2 = Next-Number; $y2 = Next-Number; $x = Next-Number; $y = Next-Number
                $path.AddBezier([single]$cx, [single]$cy, [single]$x1, [single]$y1, [single]$x2, [single]$y2, [single]$x, [single]$y)
                $cx = $x; $cy = $y
            }
            'Z' { $path.CloseFigure(); $cx = $sx; $cy = $sy }
            default { throw "Comando de traçado sem suporte: '$cmd'" }
        }
    }
    $path
}

function New-RoundedRect([double]$X, [double]$Y, [double]$W, [double]$H, [double]$R) {
    $d = $R * 2
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddArc([single]$X, [single]$Y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($X + $W - $d), [single]$Y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($X + $W - $d), [single]($Y + $H - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$X, [single]($Y + $H - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    $p
}

function Render-Seal([int]$Size, $Glyph) {
    $big = $Size * $Supersample
    $bmp = [System.Drawing.Bitmap]::new($big, $big, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.ScaleTransform($big / $Grid, $big / $Grid)
        $rotation = [System.Drawing.Drawing2D.Matrix]::new()
        $rotation.RotateAt([single]$Tilt, [System.Drawing.PointF]::new($Grid / 2, $Grid / 2))
        $g.MultiplyTransform($rotation)

        $side = $Grid - 2 * $PlateInset
        $plate = New-RoundedRect $PlateInset $PlateInset $side $side $PlateRadius
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($PlateColor))
        $g.FillPath($brush, $plate)
        $brush.Dispose(); $plate.Dispose()

        $ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($GlyphColor))
        $g.FillPath($ink, $Glyph)
        $ink.Dispose(); $rotation.Dispose()
    }
    finally { $g.Dispose() }

    $out = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $og = [System.Drawing.Graphics]::FromImage($out)
    try {
        $og.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $og.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $og.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $og.DrawImage($bmp, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
    }
    finally { $og.Dispose(); $bmp.Dispose() }
    $out
}

$glyph = ConvertTo-GraphicsPath (Read-SealPath $Brand)
$pngs = foreach ($size in $Sizes) {
    $bmp = Render-Seal $size $glyph
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($size, $ms.ToArray())
}
$glyph.Dispose()

# ICO: ICONDIR (6 bytes) + uma ICONDIRENTRY (16 bytes) por tamanho + os PNGs.
$file = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($file)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($entry in $pngs) {
    $size, $bytes = $entry
    $dim = if ($size -ge 256) { [byte]0 } else { [byte]$size }
    $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($entry in $pngs) { $w.Write([byte[]]$entry[1]) }
$w.Flush()

$target = [System.IO.Path]::GetFullPath($Output)
[System.IO.File]::WriteAllBytes($target, $file.ToArray())
Write-Host "Gerado: $target ($($Sizes -join ', ') px)"
