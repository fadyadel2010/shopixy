param([string]$Stage = 'before')
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/manual-wallet-no-sandbox-20261020'
New-Item -ItemType Directory -Force $out | Out-Null
if ($Stage -eq 'before') {
    git status --short | Set-Content (Join-Path $out 'workspace-before.txt')
    git rev-parse HEAD | Set-Content (Join-Path $out 'head-before.txt')
}
$listeners = @(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 5010,5173,5174 } | Select-Object LocalAddress,LocalPort,OwningProcess)
$ids = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
$files = @('PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll','PointOfSale.API/appsettings.json','PointOfSale.API/appsettings.Development.json','PointOfSale.Web/src/pages/PaymentMethods.jsx','PointOfSale.Storefront/src/store/checkoutStore.ts')
@{observedUtc=[DateTime]::UtcNow.ToString('o');workspace=$root;listeners=$listeners;processes=@(Get-CimInstance Win32_Process | Where-Object {$_.ProcessId -in $ids} | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine);files=@($files | ForEach-Object {@{path=$_;hash=(Get-FileHash (Join-Path $root $_)).Hash;written=(Get-Item (Join-Path $root $_)).LastWriteTimeUtc}})} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out "runtime-$Stage.json")
