$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort 5010 -State Listen -ErrorAction SilentlyContinue) { throw 'API listener must be user-stopped before activation.' }
$e = (Resolve-Path '.ai/evidence/storefront-phase8-20261018').Path
$source = (Resolve-Path '.artifacts/storefront-phase8-api/bin/PointOfSale.API/debug').Path
$target = (Resolve-Path 'PointOfSale.API/bin/Debug/net9.0').Path
$backup = Join-Path (Get-Location) '.opencode-tmp/storefront-phase8-20261018/api-before-activation'
if (Test-Path $backup) { throw 'Backup already exists; refusing to overwrite the activation before-image.' }
$baseline = Get-Content "$e/api-baseline.json" -Raw | ConvertFrom-Json
foreach ($artifact in $baseline.assemblies) {
    if ((Get-FileHash $artifact.path -Algorithm SHA256).Hash -ne $artifact.sha256) { throw "Installed assembly changed concurrently: $($artifact.path)" }
}
foreach ($config in $baseline.configurationFiles) {
    if ((Get-FileHash $config.path -Algorithm SHA256).Hash -ne $config.sha256) { throw "Configuration changed concurrently: $($config.path)" }
}
$names = @('PointOfSale.API.exe','PointOfSale.API.dll','PointOfSale.API.pdb','PointOfSale.API.deps.json','PointOfSale.API.runtimeconfig.json','PointOfSale.Core.dll','PointOfSale.Core.pdb','PointOfSale.Infrastructure.dll','PointOfSale.Infrastructure.pdb')
foreach ($name in $names) { if (!(Test-Path (Join-Path $source $name)) -or !(Test-Path (Join-Path $target $name))) { throw "Missing named runtime artifact: $name" } }
New-Item -ItemType Directory -Path $backup | Out-Null
$records = @()
foreach ($name in $names) {
    $from = Join-Path $source $name; $to = Join-Path $target $name; $old = Join-Path $backup $name
    $before = (Get-FileHash $to -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $to -Destination $old
    if ((Get-FileHash $old -Algorithm SHA256).Hash -ne $before) { throw 'Backup verification failed' }
    Copy-Item -LiteralPath $from -Destination $to
    $built = (Get-FileHash $from -Algorithm SHA256).Hash
    if ((Get-FileHash $to -Algorithm SHA256).Hash -ne $built) { throw 'Installation verification failed' }
    $records += @{ file=$name; before=$before; installed=$built; backup=$old }
}
foreach ($config in $baseline.configurationFiles) { if ((Get-FileHash $config.path -Algorithm SHA256).Hash -ne $config.sha256) { throw 'Unexpected configuration change' } }
@{ observedUtc=[DateTime]::UtcNow.ToString('o'); source=$source; target=$target; records=$records; configurationCopied=$false; apiStopped=$true } | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$e/api-install.json"
Write-Output 'Nine named runtime artifacts backed up, installed and SHA256-verified. No configuration copied.'
