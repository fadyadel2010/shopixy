# Assemble headed-browser screenshots for visual review; source images stay intact.
Add-Type -AssemblyName System.Drawing
$themes = @('modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven')
$artifacts = Join-Path $PSScriptRoot '../../.artifacts'
if (!(Test-Path -LiteralPath $artifacts)) { throw 'Expected workspace artifact directory missing' }
foreach ($language in @('en','ar')) {
    foreach ($device in @('desktop','mobile')) {
        foreach ($surface in @('top','footer','middle')) {
            for ($group = 0; $group -lt 2; $group++) {
                $tileWidth = if ($device -eq 'mobile') { 195 } else { 388 }
                $tileHeight = if ($device -eq 'mobile') { 422 } else { 211 }
                $canvas = New-Object System.Drawing.Bitmap ($tileWidth * 2), (($tileHeight + 24) * 5)
                $graphics = [System.Drawing.Graphics]::FromImage($canvas)
                $graphics.Clear([System.Drawing.Color]::White)
                $font = New-Object System.Drawing.Font 'Arial', 11
                for ($row = 0; $row -lt 5; $row++) {
                    $theme = $themes[$group * 5 + $row]
                    $y = $row * ($tileHeight + 24)
                    $graphics.DrawString("$theme - preview / published - $language $device", $font, [System.Drawing.Brushes]::Black, 4, $y)
                    $column = 0
                    foreach ($kind in @('preview','live')) {
                        if ($surface -eq 'middle') {
                            $middleKind = if ($kind -eq 'live') { 'storefront' } else { 'preview' }
                            $file = Join-Path $artifacts "gap-middle-$theme-$language-$device-$middleKind.png"
                        } else {
                            $file = Join-Path $artifacts "gap-parity-$theme-$language-$device-$kind-$surface.png"
                        }
                        $image = [System.Drawing.Image]::FromFile($file)
                        $graphics.DrawImage($image, ($column * $tileWidth), ($y + 24), $tileWidth, $tileHeight)
                        $image.Dispose()
                        $column++
                    }
                }
                $output = Join-Path $artifacts "gap-contact-$language-$device-$surface-$group.png"
                $canvas.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
                $font.Dispose(); $graphics.Dispose(); $canvas.Dispose()
            }
        }
    }
}
'Generated 24 paired visual contact sheets from the 240 headed screenshots.'
