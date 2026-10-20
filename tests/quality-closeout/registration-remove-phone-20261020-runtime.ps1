param([string]$Stage = 'tested')
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/registration-remove-phone-20261020'
$ports = @(5010,5011,5173,5174,5175,2526)
$listeners = @(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in $ports } | Select-Object LocalAddress,LocalPort,OwningProcess)
$ids = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -in $ids } | Select-Object ProcessId,ParentProcessId,CreationDate,ExecutablePath)
$sources = @('PointOfSale.Storefront/src/auth/components/RegisterForm.tsx','PointOfSale.Storefront/src/auth/api/authApi.ts','PointOfSale.Storefront/src/auth/types/auth.types.ts','PointOfSale.API/Services/Storefront/StorefrontAuthService.cs','PointOfSale.API/Services/Storefront/StorefrontEmailVerificationService.cs','PointOfSale.API/Controllers/StorefrontAuthController.cs')
$result = @{
    observedUtc=[DateTime]::UtcNow.ToString('o');workspace=$root;listeners=$listeners;processes=$processes;
    storefront='http://ahramstore.localhost:5175';primaryStorefront='http://ahramstore.localhost:5174';api='http://localhost:5011';
    apiDllHash=(Get-FileHash (Join-Path $root 'PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll')).Hash;
    sourceHashes=@($sources | ForEach-Object { @{path=$_;sha256=(Get-FileHash (Join-Path $root $_)).Hash} })
}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out "runtime-$Stage.json")
$result | ConvertTo-Json -Depth 8
