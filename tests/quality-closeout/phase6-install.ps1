$ErrorActionPreference = 'Stop'
$listener = Get-NetTCPConnection -LocalPort 5010 -State Listen -ErrorAction SilentlyContinue
if ($listener) { throw 'API still listening on 5010; installation withheld.' }
$built = '.opencode-tmp/storefront-phase6-20261018/api-built-final-activation'
$active = 'PointOfSale.API/bin/Debug/net9.0'
$backup = '.opencode-tmp/storefront-phase6-20261018/api-before-closeout-install'
New-Item -ItemType Directory -Force $backup | Out-Null
$names = @('PointOfSale.API.dll','PointOfSale.API.pdb','PointOfSale.API.exe','PointOfSale.API.deps.json','PointOfSale.API.runtimeconfig.json','PointOfSale.Core.dll','PointOfSale.Core.pdb','PointOfSale.Infrastructure.dll','PointOfSale.Infrastructure.pdb')
$manifest = foreach ($name in $names) {
    $source = Join-Path $built $name
    $target = Join-Path $active $name
    $previous = (Get-FileHash $target -Algorithm SHA256).Hash
    Copy-Item $target (Join-Path $backup $name)
    if ((Get-FileHash (Join-Path $backup $name) -Algorithm SHA256).Hash -ne $previous) { throw 'Backup hash mismatch' }
    Copy-Item $source $target -Force
    $expected = (Get-FileHash $source -Algorithm SHA256).Hash
    $actual = (Get-FileHash $target -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw 'Install hash mismatch' }
    [pscustomobject]@{name=$name; previous=$previous; built=$expected; installed=$actual; matched=$true}
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 '.ai/evidence/storefront-phase6-20261018/api-install-closeout.json'
$manifest | Select-Object name,matched
