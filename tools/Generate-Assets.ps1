<#
    Gera a identidade visual do Optimizer PC.

    Conceito: um medidor de desempenho (velocidade + otimizacao) com um raio
    central (ganho de desempenho). Nao utiliza elementos de antivirus e nao
    reproduz marcas de terceiros.

    Saidas:
      src/OptimizerPC.App/Assets/OptimizerPC.ico   (16,24,32,48,64,128,256)
      src/OptimizerPC.App/Assets/logo.png          (256x256)
      src/OptimizerPC.App/Assets/logo-large.png    (512x512)
      installer/wizard-large.bmp                   (164x314, pagina do instalador)
      installer/wizard-small.bmp                   (55x58, cabecalho do instalador)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERRO linha $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)" -ForegroundColor Red; break }
Add-Type -AssemblyName System.Drawing

function MaxD { param([double]$A, [double]$B) if ($A -gt $B) { return $A } else { return $B } }
function F { param([double]$V) return [float]$V }

function New-RoundedPath {
    param([double]$X, [double]$Y, [double]$W, [double]$H, [double]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2.0
    $path.AddArc((F $X), (F $Y), (F $d), (F $d), 180.0, 90.0)
    $path.AddArc((F ($X + $W - $d)), (F $Y), (F $d), (F $d), 270.0, 90.0)
    $path.AddArc((F ($X + $W - $d)), (F ($Y + $H - $d)), (F $d), (F $d), 0.0, 90.0)
    $path.AddArc((F $X), (F ($Y + $H - $d)), (F $d), (F $d), 90.0, 90.0)
    $path.CloseFigure()
    return $path
}

function New-LogoBitmap {
    param([int]$Size, [bool]$Transparent = $false)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    [double]$s = $Size

    if (-not $Transparent) {
        $bgRect = New-Object System.Drawing.RectangleF(0.0, 0.0, (F $s), (F $s))
        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $bgRect,
            [System.Drawing.Color]::FromArgb(255, 24, 36, 58),
            [System.Drawing.Color]::FromArgb(255, 9, 14, 26),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        [double]$radius = MaxD 2.0 ($s * 0.215)
        $bgPath = New-RoundedPath -X 0.0 -Y 0.0 -W $s -H $s -Radius $radius
        $g.FillPath($bgBrush, $bgPath)

        [double]$penW = MaxD 1.0 ($s * 0.035)
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 96, 165, 250), (F $penW))
        [double]$inset = $penW / 2.0
        $borderPath = New-RoundedPath -X $inset -Y $inset -W ($s - $penW) -H ($s - $penW) -Radius ($radius - $inset)
        $g.DrawPath($borderPen, $borderPath)
        $borderPen.Dispose(); $bgBrush.Dispose(); $bgPath.Dispose(); $borderPath.Dispose()
    }

    # Medidor: arco de 220 graus, abertura voltada para baixo.
    [double]$stroke = MaxD 1.4 ($s * 0.085)
    [double]$margin = ($stroke / 2.0) + ($s * 0.115)
    [double]$diameter = $s - ($margin * 2.0)
    [double]$arcY = $margin + ($s * 0.06)
    $arcRect = New-Object System.Drawing.RectangleF((F $margin), (F $arcY), (F $diameter), (F $diameter))
    $capStyle = [System.Drawing.Drawing2D.LineCap]::Round

    [double]$startAngle = 160.0
    [double]$totalSweep = 220.0
    [double]$seg1Sweep = $totalSweep * 0.62
    [double]$seg2Start = $startAngle + $seg1Sweep - 1.0
    [double]$seg2Sweep = ($totalSweep * 0.38) + 1.0

    # Duas cores: ciano (base) e verde (conclusao) - leitura de "progresso/otimizado".
    $seg1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 34, 211, 238), (F $stroke))
    $seg1.StartCap = $capStyle; $seg1.EndCap = $capStyle
    $seg2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 211, 153), (F $stroke))
    $seg2.StartCap = $capStyle; $seg2.EndCap = $capStyle

    $g.DrawArc($seg1, $arcRect, (F $startAngle), (F $seg1Sweep))
    $g.DrawArc($seg2, $arcRect, (F $seg2Start), (F $seg2Sweep))
    $seg1.Dispose(); $seg2.Dispose()

    # Raio central: ganho de desempenho.
    [double]$cx = $s * 0.5
    [double]$cy = $s * 0.60
    [double]$k = $s * 0.30
    $pts = New-Object 'System.Drawing.PointF[]' 6
    $pts[0] = New-Object System.Drawing.PointF((F ($cx + $k * 0.22)), (F ($cy - $k * 1.05)))
    $pts[1] = New-Object System.Drawing.PointF((F ($cx - $k * 0.62)), (F ($cy + $k * 0.10)))
    $pts[2] = New-Object System.Drawing.PointF((F ($cx - $k * 0.05)), (F ($cy + $k * 0.10)))
    $pts[3] = New-Object System.Drawing.PointF((F ($cx - $k * 0.28)), (F ($cy + $k * 1.02)))
    $pts[4] = New-Object System.Drawing.PointF((F ($cx + $k * 0.62)), (F ($cy - $k * 0.16)))
    $pts[5] = New-Object System.Drawing.PointF((F ($cx + $k * 0.04)), (F ($cy - $k * 0.16)))
    $boltBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
    $g.FillPolygon($boltBrush, $pts)
    $boltBrush.Dispose()

    $g.Dispose()
    return $bmp
}

function Save-Ico {
    param([string]$Path, [int[]]$Sizes)

    $blobs = New-Object System.Collections.ArrayList
    foreach ($size in $Sizes) {
        $bmp = New-LogoBitmap -Size $size
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [void]$blobs.Add([pscustomobject]@{ Size = $size; Data = [byte[]]$ms.ToArray() })
        $ms.Dispose(); $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$blobs.Count)

    $offset = 6 + (16 * $blobs.Count)
    foreach ($img in $blobs) {
        $dim = 0
        if ($img.Size -lt 256) { $dim = $img.Size }
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$img.Data.Length)
        $bw.Write([UInt32]$offset)
        $offset = $offset + $img.Data.Length
    }
    foreach ($img in $blobs) { $bw.Write($img.Data, 0, $img.Data.Length) }
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

$assets = Join-Path $Root 'src/OptimizerPC.App/Assets'
$installer = Join-Path $Root 'installer'
New-Item -ItemType Directory -Force -Path $assets, $installer | Out-Null

Save-Ico -Path (Join-Path $assets 'OptimizerPC.ico') -Sizes @(16, 24, 32, 48, 64, 128, 256)

$logo = New-LogoBitmap -Size 256
$logo.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

$logoLarge = New-LogoBitmap -Size 512
$logoLarge.Save((Join-Path $assets 'logo-large.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logoLarge.Dispose()

# Imagem lateral do instalador (Inno Setup: 164x314).
$bmpW = 164; $bmpH = 314
$wiz = New-Object System.Drawing.Bitmap($bmpW, $bmpH, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$wg = [System.Drawing.Graphics]::FromImage($wiz)
$wg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$wg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$rect = New-Object System.Drawing.Rectangle(0, 0, $bmpW, $bmpH)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 15, 23, 42),
    [System.Drawing.Color]::FromArgb(255, 8, 13, 25),
    [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$wg.FillRectangle($grad, $rect)
$mark = New-LogoBitmap -Size 112
$wg.DrawImage($mark, [int]((164 - 112) / 2), 84, 112, 112)
$wg.Dispose(); $grad.Dispose(); $mark.Dispose()
$wiz.Save((Join-Path $installer 'wizard-large.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$wiz.Dispose()

# Imagem do cabecalho do instalador (Inno Setup: 55x58, fundo claro).
$smallW = 55; $smallH = 58
$small = New-Object System.Drawing.Bitmap($smallW, $smallH, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$sg = [System.Drawing.Graphics]::FromImage($small)
$sg.Clear([System.Drawing.Color]::White)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$markSmall = New-LogoBitmap -Size 48
$sg.DrawImage($markSmall, [int](($smallW - 48) / 2), [int](($smallH - 48) / 2), 48, 48)
$sg.Dispose(); $markSmall.Dispose()
$small.Save((Join-Path $installer 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$small.Dispose()

Write-Host "Identidade visual gerada em: $assets"
Get-ChildItem $assets | Select-Object Name, Length | Format-Table -AutoSize
