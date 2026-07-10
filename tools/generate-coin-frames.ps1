Add-Type -AssemblyName System.Drawing

function Get-ContentBounds($bmp) {
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            if ($bmp.GetPixel($x, $y).A -eq 0) { continue }
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
    if ($maxX -lt $minX) { return [System.Drawing.Rectangle]::FromLTRB(0, 0, $bmp.Width, $bmp.Height) }
    return [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
}

function Remove-BlackBackground($bmp, $tolerance) {
    $w = $bmp.Width; $h = $bmp.Height
    $c1 = $bmp.GetPixel(0, 0)
    $c2 = $bmp.GetPixel($w - 1, 0)
    $c3 = $bmp.GetPixel(0, $h - 1)
    $c4 = $bmp.GetPixel($w - 1, $h - 1)
    $bgR = [int](($c1.R + $c2.R + $c3.R + $c4.R) / 4)
    $bgG = [int](($c1.G + $c2.G + $c3.G + $c4.G) / 4)
    $bgB = [int](($c1.B + $c2.B + $c3.B + $c4.B) / 4)
    $tolSq = $tolerance * $tolerance

    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $dr = $p.R - $bgR; $dg = $p.G - $bgG; $db = $p.B - $bgB
            $distSq = $dr * $dr + $dg * $dg + $db * $db
            if ($distSq -le $tolSq) {
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            } else {
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $p.R, $p.G, $p.B))
            }
        }
    }
}

$coinFlipDir = "C:\Users\david\DesktopPet\Sprites\Coin Flip"
$sheetPath = Join-Path $coinFlipDir "fJLBc.png"
$sheet = [System.Drawing.Bitmap]::FromFile($sheetPath)
$frameCount = 10
$frameWidth = [int]($sheet.Width / $frameCount)
$frameHeight = $sheet.Height
$extraPadding = 12
$minPadding = 16
$frameIndices = 0..5

Write-Host "Reading $sheetPath"
Write-Host "Frame size: ${frameWidth}x${frameHeight}"

foreach ($i in 0..($frameIndices.Length - 1)) {
    $frameIndex = $frameIndices[$i]
    $outputPath = Join-Path $coinFlipDir ("Coin {0}.png" -f ($i + 1))
    $backupPath = Join-Path $coinFlipDir ("Coin {0}.user-edit.png" -f ($i + 1))

    if ((Test-Path $outputPath) -and -not (Test-Path $backupPath)) {
        Copy-Item $outputPath $backupPath
        Write-Host "Backed up to $(Split-Path $backupPath -Leaf)"
    }

    $frame = New-Object System.Drawing.Bitmap $frameWidth, $frameHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($frame)
    $g.DrawImage($sheet, [System.Drawing.Rectangle]::new(0, 0, $frameWidth, $frameHeight), $frameIndex * $frameWidth, 0, $frameWidth, $frameHeight, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    Remove-BlackBackground $frame 40

    $bounds = Get-ContentBounds $frame
    $padLeft = $bounds.Left
    $padRight = $frame.Width - $bounds.Right
    $padTop = $bounds.Top
    $padBottom = $frame.Height - $bounds.Bottom
    $targetPad = [Math]::Max($minPadding, ([Math]::Max([Math]::Max($padLeft, $padRight), [Math]::Max($padTop, $padBottom)) + $extraPadding))
    $canvasSize = [Math]::Max($bounds.Width, $bounds.Height) + $targetPad * 2

    $normalized = New-Object System.Drawing.Bitmap $canvasSize, $canvasSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $ng = [System.Drawing.Graphics]::FromImage($normalized)
    $ng.Clear([System.Drawing.Color]::Transparent)
    $ng.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $x = [int](($canvasSize - $bounds.Width) / 2)
    $y = [int](($canvasSize - $bounds.Height) / 2)
    $ng.DrawImage($frame, [System.Drawing.Rectangle]::new($x, $y, $bounds.Width, $bounds.Height), $bounds, [System.Drawing.GraphicsUnit]::Pixel)
    $ng.Dispose()

    $normalized.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $nb = Get-ContentBounds $normalized
    Write-Host ("Wrote Coin {0}.png ({1}x{2}) pads L={3} R={4} T={5} B={6}" -f ($i + 1), $canvasSize, $canvasSize, $nb.Left, ($canvasSize - $nb.Right), $nb.Top, ($canvasSize - $nb.Bottom))

    $frame.Dispose()
    $normalized.Dispose()
}

$sheet.Dispose()
Write-Host "Done."