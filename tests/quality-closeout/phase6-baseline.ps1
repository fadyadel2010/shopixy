$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$evidence = Join-Path $root '.ai/evidence/storefront-phase6-20261018'
New-Item -ItemType Directory -Force $evidence | Out-Null
git status --porcelain=v2 --untracked-files=all | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-git-status.txt')
git rev-parse HEAD | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-head.txt')
git diff --cached --raw | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-index.txt')
git ls-files --deleted | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-deleted.txt')
git ls-files --others --exclude-standard | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-untracked.txt')
$scopes = @('PointOfSale.API/Controllers/AdminAnalytics*', 'PointOfSale.API/Controllers/AdminCampaignCostController.cs', 'PointOfSale.API/Controllers/Admin/AdminPixelTrackingController.cs', 'PointOfSale.API/Services/Analytics', 'PointOfSale.Infrastructure/Analytics', 'PointOfSale.Infrastructure/Attribution', 'PointOfSale.Infrastructure/CampaignCost', 'PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs', 'PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs', 'PointOfSale.Core/Analytics', 'PointOfSale.Storefront/src/analytics', 'PointOfSale.Web/src/modules/analytics', 'PointOfSale.Web/src/modules/storefront/config/PixelTrackingConfigPage.jsx', 'PointOfSale.Web/src/App.jsx', 'PointOfSale.Web/src/i18n/locales', 'PointOfSale.Tests/Analytics', 'PointOfSale.Tests/Infrastructure/Analytics', 'PointOfSale.Tests/Architecture/StorefrontAnalyticsAuthorityCertificationTests.cs')
$scopes += @('PointOfSale.Infrastructure/Data/ApplicationDbContext.cs', 'PointOfSale.Storefront/src/app/router.tsx', 'PointOfSale.Tests/Analytics/ProviderDeliveryInfrastructureTests.cs', 'PointOfSale.Tests/Architecture/ProviderDeliveryHardeningArchitectureTests.cs')
$files = @($scopes | ForEach-Object { Get-ChildItem $_ -File -Recurse } | Sort-Object FullName -Unique)
$manifest = foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1)
    $target = Join-Path (Join-Path $evidence 'originals') $relative
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    [pscustomobject]@{path = $relative; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-originals.json')
git diff -- $scopes | Set-Content -Encoding utf8 (Join-Path $evidence 'baseline-scope.diff')
[pscustomobject]@{localDate = (Get-Date).ToString('o'); utcDate = [DateTime]::UtcNow.ToString('o'); originalFiles = $files.Count; deleted = @(Get-Content (Join-Path $evidence 'baseline-deleted.txt')).Count; untracked = @(Get-Content (Join-Path $evidence 'baseline-untracked.txt')).Count} | ConvertTo-Json
