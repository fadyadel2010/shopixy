param([string]$Run = 'backend-tests')
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$out = Join-Path $root '.ai/evidence/manual-wallet-visibility-20261020'
$runtime = Join-Path $root 'temp/manual-wallet-visibility-20261020/tests/net9.0'
New-Item -ItemType Directory -Force $runtime | Out-Null
Copy-Item '.artifacts/manual-wallet-visibility-20261020/bin/PointOfSale.Tests/debug/*' $runtime -Recurse -Force
$classes = @('ManualWalletCheckoutReadinessTests','PaymentMethodConfigurationValidatorTests','StorefrontPaymentMethodAvailabilityTests','PaymentMethodReadAuthorityTests','PaymentMethodsAdminSafetyTests','PaymentMethodsAdminSyncReliabilityTests','Phase5PaymentConfigurationTests','ManualWalletConfiguredTenantTests','StorefrontTenantIsolationTests','StorefrontManualWalletStorageTests','ManualPaymentCapabilityAdmissionTests','PaymentServiceCreatePaymentIntentGuardTests','PaymentLifecycleSettlementAuthorityTests','PaymentLifecycleRefundAuthorityTests','AdminOrderListDeterministicTests','ManualWalletAdmissionRetryTests','CheckoutOrderCommitRetryTests')
$filter = ($classes | ForEach-Object {'FullyQualifiedName~'+$_+'.'}) -join '|'
$filter += '|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.LegacyItemGuard|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.ManualSettlementGuard'
$env:MANUAL_WALLET_CONFIGURED = '1'
$env:MANUAL_WALLET_EVIDENCE = $out
$filter | Set-Content (Join-Path $out "$Run-filter.txt")
dotnet vstest "$runtime/PointOfSale.Tests.dll" "/TestCaseFilter:$filter" "/ResultsDirectory:$out/trx" "/Logger:trx;LogFileName=$Run.trx" 2>&1 | Out-File -Encoding utf8 (Join-Path $out "$Run.log")
$code = $LASTEXITCODE
@{exitCode=$code;testAssembly=(Get-FileHash "$runtime/PointOfSale.Tests.dll").Hash;observedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $out "$Run-result.json")
exit $code
