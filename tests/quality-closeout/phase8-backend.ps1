param([string]$Run = 'cross-domain')
$ErrorActionPreference = 'Continue'
$evidence = (Resolve-Path '.ai/evidence/storefront-phase8-20261018').Path
$env:PHASE8_CONFIGURED='1'
$env:PHASE8_EVIDENCE=$evidence
$env:STOREFRONT_PHASE1_CONFIGURED='1'
$env:STOREFRONT_PHASE1_TENANT='c9c5c1ae-949e-498e-bc63-07242daeb49d'
$env:STOREFRONT_PHASE1_EVIDENCE=$evidence
$env:STOREFRONT_PHASE2_TENANT=$env:STOREFRONT_PHASE1_TENANT
$env:STOREFRONT_PHASE2_EVIDENCE=$evidence
$env:STOREFRONT_PHASE2_SNAPSHOT_STAGE='after-final-rollback'
$env:STOREFRONT_PHASE3_CONFIGURED='1'
$env:STOREFRONT_PHASE3_EVIDENCE=$evidence
$env:STOREFRONT_PHASE3_OBSERVE_TENANT=$env:STOREFRONT_PHASE1_TENANT
$env:STOREFRONT_PHASE4_CONFIGURED='1'
$env:STOREFRONT_PHASE4_EVIDENCE=$evidence
$env:STOREFRONT_PHASE4_STAGE=$Run
$env:PHASE6_CONFIGURED='1'
$env:PHASE6_EVIDENCE=$evidence
$env:PHASE6_STAGE=$Run
$env:PHASE7_CONFIGURED='1'
$env:PHASE7_EVIDENCE=$evidence
$env:SHIPPING_CONFIGURED_ROLLBACK_QUALIFY='1'
$env:SHIPPING_FINAL_EVIDENCE_DIRECTORY=$evidence
$env:SHOPIXY_PHASE8_ROLLBACK='1'
$filter = '(' + (Get-Content "$evidence/cross-domain-safe.filter.txt" -Raw).Trim() + ')'
# Committed capacity fixtures and special browser/observer hosts run separately,
# not as a side effect of expanding the current selection to class prefixes.
$filter += '&FullyQualifiedName!~Configured_Concurrent&FullyQualifiedName!~RealMongo_'
$filter += '&FullyQualifiedName!~ConfiguredCarrierRemittanceRollbackTests'
$filter += '|FullyQualifiedName~PosUserManagementTests.|FullyQualifiedName~PosUserManagementApiTests.'
$filter += '|FullyQualifiedName~Phase5HttpCacheTests.|FullyQualifiedName~CacheCoreAndCleanupArchitectureTests.'
$filter += '|' + (Get-Content "$evidence/historical-safe.filter.txt" -Raw).Trim()
$filter | Out-File -Encoding utf8 "$evidence/$Run.filter.txt"
$output = Join-Path (Get-Location) '.opencode-tmp/storefront-phase8-20261018/test-runtime/net9.0'
if (!(Test-Path $output)) {
    # Legacy root discoverers mistake artifacts/bin (containing sibling project
    # directories) for the repository root. Copy the compiled runtime, unchanged,
    # to a private four-level path compatible with both legacy and current tests.
    New-Item -ItemType Directory -Path $output | Out-Null
    Copy-Item -Path '.artifacts/storefront-phase8-hosted/bin/PointOfSale.Tests/release/*' -Destination $output -Recurse
    if ((Get-FileHash "$output/PointOfSale.Tests.dll").Hash -ne (Get-FileHash '.artifacts/storefront-phase8-hosted/bin/PointOfSale.Tests/release/PointOfSale.Tests.dll').Hash) { throw 'Copied test assembly identity mismatch' }
}
$started = [DateTime]::UtcNow
dotnet vstest "$output/PointOfSale.Tests.dll" "/TestCaseFilter:$filter" "/ResultsDirectory:$evidence/trx" "/Logger:trx;LogFileName=$Run.trx" '/Logger:Console;Verbosity=quiet' 2>&1 | Out-File -Encoding utf8 "$evidence/$Run.log"
$code = $LASTEXITCODE
@{ startedUtc=$started; finishedUtc=[DateTime]::UtcNow; exitCode=$code; output=$output; filterFile="$Run.filter.txt" } | ConvertTo-Json | Out-File -Encoding utf8 "$evidence/$Run-command.json"
exit $code
