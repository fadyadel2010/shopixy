param([string]$Stage = 'before')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/registration-phone-20261020'
$dll = Join-Path $root 'PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll'
$sources = @('PointOfSale.API/Services/Storefront/StorefrontAuthService.cs',
    'PointOfSale.API/Services/Storefront/StorefrontEmailVerificationService.cs',
    'PointOfSale.API/Controllers/StorefrontAuthController.cs',
    'PointOfSale.Storefront/src/auth/components/RegisterForm.tsx',
    'PointOfSale.Storefront/src/auth/pages/RegisterPage.tsx',
    'PointOfSale.Storefront/src/auth/store/authStore.ts',
    'PointOfSale.Storefront/src/auth/api/authApi.ts')
$result = @{
    observedUtc = [DateTime]::UtcNow.ToString('o')
    apiProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'PointOfSale.API.exe' } | Select-Object ProcessId, CreationDate, ExecutablePath)
    listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5010,5173,5174,2526 | Select-Object LocalAddress, LocalPort, OwningProcess)
    apiDll = @{ hash = (Get-FileHash $dll).Hash; written = (Get-Item $dll).LastWriteTimeUtc.ToString('o') }
    sourceHashes = @($sources | ForEach-Object { Get-FileHash (Join-Path $root $_) | Select-Object Path,Hash })
}
$result | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $out "runtime-$Stage.json")
$result | ConvertTo-Json -Depth 7
