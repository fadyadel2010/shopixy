param([string]$Run = 'backend-tests')
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/manual-wallet-no-sandbox-20261020'
$classes = @('ManualWalletCheckoutReadinessTests','PaymentMethodConfigurationValidatorTests','StorefrontPaymentMethodAvailabilityTests','PaymentMethodReadAuthorityTests','PaymentMethodsAdminSafetyTests','PaymentMethodsAdminSyncReliabilityTests','Phase5PaymentConfigurationTests','ManualWalletConfiguredTenantTests','StorefrontTenantIsolationTests','StorefrontManualWalletStorageTests','PaymentServiceCreatePaymentIntentGuardTests','PaymentLifecycleSettlementAuthorityTests','PaymentLifecycleRefundAuthorityTests','AdminOrderListDeterministicTests','ManualWalletAdmissionRetryTests','CheckoutOrderCommitRetryTests','AdminManualPaymentPendingTests','ManualPaymentApplicationContractCertificationTests','ManualPaymentEvidenceApplicationContractCertificationTests','ManualPaymentCertificationClosure2Tests','ManualPaymentFoundationCertificationTests','ManualPaymentProduction2SandboxValidationTests','ManualPaymentProductionOperationalReadinessTests','StripeContractClosureApplicationCertificationTests','PaymobContractClosureApplicationCertificationTests','StripeProductionSandboxValidationTests','PaymobProductionSandboxValidationTests','StorefrontProductionBlockerFixTests')
$filter = ($classes | ForEach-Object {'FullyQualifiedName~'+$_+'.'}) -join '|'
$filter += '|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.LegacyItemGuard|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.ManualSettlementGuard'
$env:MANUAL_WALLET_CONFIGURED = '1'
$env:MANUAL_WALLET_EVIDENCE = $out
$filter | Set-Content (Join-Path $out "$Run-filter.txt")
dotnet vstest "$root/PointOfSale.Tests/bin/Debug/net9.0/PointOfSale.Tests.dll" "/TestCaseFilter:$filter" "/ResultsDirectory:$out/trx" "/Logger:trx;LogFileName=$Run.trx" 2>&1 | Out-File -Encoding utf8 (Join-Path $out "$Run.log")
$code = $LASTEXITCODE
@{exitCode=$code;testAssembly=(Get-FileHash "$root/PointOfSale.Tests/bin/Debug/net9.0/PointOfSale.Tests.dll").Hash;observedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $out "$Run-result.json")
exit $code
