$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$private = Join-Path $env:LOCALAPPDATA 'Temp/opencode/registration-remove-phone-20261020'
Set-Location (Join-Path $root 'PointOfSale.Storefront')
$env:VITE_DEV_API_TARGET = 'http://localhost:5011'
& node node_modules/vite/bin/vite.js --port 5175 --strictPort *> (Join-Path $private 'storefront-5175.log')
exit $LASTEXITCODE
