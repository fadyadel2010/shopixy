$ErrorActionPreference = 'Continue'
$e = (Resolve-Path '.ai/evidence/storefront-phase8-20261018').Path
if (Get-NetTCPConnection -State Listen -LocalPort 5010 -ErrorAction SilentlyContinue) { throw 'API must be user-stopped for whole-table preservation comparisons.' }
$env:PHASE8_CONFIGURED='1'; $env:PHASE8_EVIDENCE=$e; $env:PHASE8_OBSERVE_HOSTED='1'
# Never opt into aging again: that exact session has already converted.
$env:PHASE8_AGE_HOSTED='0'
$env:PHASE6_CONFIGURED='1'; $env:PHASE6_EVIDENCE=$e; $env:PHASE6_STAGE='final'
$env:PHASE7_CONFIGURED='1'; $env:PHASE7_EVIDENCE=$e
$env:STOREFRONT_PHASE4_CONFIGURED='1'; $env:STOREFRONT_PHASE4_EVIDENCE=$e; $env:STOREFRONT_PHASE4_STAGE='final'
$env:PHASE5_CONFIGURED='1'; $env:PHASE5_EVIDENCE=$e
$env:SHIPPING_CONFIGURED_ROLLBACK_QUALIFY='1'; $env:SHIPPING_FINAL_EVIDENCE_DIRECTORY=$e
$filter = 'FullyQualifiedName~AbandonedCartNativeSchedulerTests|FullyQualifiedName~LocalDiagnostics_ReadExistingAiKeysAndPreservePriorEvidence_NoInference|FullyQualifiedName~RetainedLifecycle_IndependentSqlMongoLedgerAttributionObserver_ReadOnly|FullyQualifiedName~ConfiguredCarrierRemittanceRollbackTests|FullyQualifiedName~ExistingMerchantAndPriorFinancialHistory_IndependentReadOnlyObserver|FullyQualifiedName~StorefrontPhase4HostedObserverTests|FullyQualifiedName~Phase7FinalObserverTests|FullyQualifiedName~ConfiguredMethodConcurrency_RejectsStaleWritesAndReviewedVersion_Rollback'
$filter | Out-File -Encoding utf8 "$e/quiescent.filter.txt"
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --artifacts-path .artifacts/storefront-phase8-hosted -p:UseSharedCompilation=false -nodeReuse:false --filter $filter --results-directory "$e/trx" --logger 'trx;LogFileName=quiescent-final.trx' -v minimal -p:WarningLevel=0 2>&1 | Out-File -Encoding utf8 "$e/quiescent-final.log"
$code = $LASTEXITCODE
@{ exitCode=$code; observedUtc=[DateTime]::UtcNow.ToString('o'); apiStopped=$true; filterFile='quiescent.filter.txt'; ageOptIn=$false; noProvisioning=$true } | ConvertTo-Json | Out-File -Encoding utf8 "$e/quiescent-command.json"
exit $code
