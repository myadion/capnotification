# Compile Adion CapNotification avec le compilateur C# fourni avec Windows.
# Résultat : dist\AdionCapNotification-Setup.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$ico  = Join-Path $src 'app.ico'
New-Item -ItemType Directory -Force $dist | Out-Null

# Icône : un « A » blanc sur fond bleu, en quatre tailles.
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $images = foreach ($size in 16, 32, 48, 256) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAlias'
        $r = $size * 0.22; $d = $r * 2; $m = $size - $d - 1
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($m, 0, $d, $d, 270, 90)
        $path.AddArc($m, $m, $d, $d, 0, 90); $path.AddArc(0, $m, $d, $d, 90, 90)
        $path.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 120, 212))), $path)
        $font = New-Object System.Drawing.Font 'Segoe UI', ($size * 0.62), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $g.DrawString('A', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ($size * 0.02), $size, $size), $fmt)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = 16, 32, 48, 256
    for ($i = 0; $i -lt $images.Count; $i++) {
        $w.Write([byte]($sizes[$i] % 256)); $w.Write([byte]($sizes[$i] % 256))
        $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    [System.IO.File]::WriteAllBytes($ico, $out.ToArray())
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$dist\AdionCapNotification-Setup.exe" `
    "/win32manifest:$src\app.manifest" "/win32icon:$ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'La compilation a échoué.' }
Get-Item "$dist\AdionCapNotification-Setup.exe" | Select-Object Name, Length
