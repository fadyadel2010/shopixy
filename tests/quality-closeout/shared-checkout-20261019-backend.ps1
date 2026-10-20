param([string]$Run = 'backend-regression')
$ErrorActionPreference = 'Stop'
$evidence = (Resolve-Path '.ai/evidence/shared-checkout-20261019').Path
$runtime = Join-Path (Get-Location) 'temp/shared-checkout-20261019/tests/net9.0'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Copy-Item '.artifacts/shared-checkout-20261019/bin/PointOfSale.Tests/release/*' $runtime -Recurse -Force
$classes = @(
 'CheckoutControllerIdempotencyHeaderTests','CheckoutPromotionCorrectnessControllerTests',
 'CheckoutCartIdempotencySnapshotBuilderTests','CheckoutRequestIdempotencyCoordinatorTests','CheckoutExecutionLockManagerTests',
 'StorefrontPricingAuthorityTests','StorefrontCartCouponAtomicityTests','StorefrontCartCouponRevalidationTests',
 'StorefrontCouponServiceCustomerValidationTests','CouponUsageServiceCqrsConsistencyTests','CouponsControllerCqrsConsistencyTests',
 'CouponReservationAdmissionTests','CouponSettlementIntegrityTests','CheckoutOrderCommitRetryTests','SharedCheckoutOutboxConfiguredTests',
 'ManualWalletAdmissionRetryTests','ManualWalletConfiguredTenantTests',
 'PaymentMethodConfigurationValidatorTests','PaymentLifecycleSettlementAuthorityTests','PaymentLifecycleRefundAuthorityTests',
 'PaymentOperationalFinancialAuthorityTests','PaymentServiceCreatePaymentIntentGuardTests',
 'AdminOrderListDeterministicTests','PaymentMethodsAdminSafetyTests',
 'CheckoutConcurrencyHardeningSourceTests','CheckoutCouponCqrsConsistencyTests','StorefrontTenantIsolationTests'
)
$filter = ($classes | ForEach-Object { 'FullyQualifiedName~' + $_ + '.' }) -join '|'
$filter += '|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.LegacyItemGuard|FullyQualifiedName~ManualWalletConfiguredLifecycleTests.ManualSettlementGuard'
$filter | Out-File -Encoding utf8 "$evidence/$Run.filter.txt"
$env:SHARED_CHECKOUT_CONFIGURED = '1'
$env:MANUAL_WALLET_CONFIGURED = '1'
$env:MANUAL_WALLET_EVIDENCE = $evidence
$started = [DateTime]::UtcNow
dotnet vstest "$runtime/PointOfSale.Tests.dll" "/TestCaseFilter:$filter" "/ResultsDirectory:$evidence/trx" "/Logger:trx;LogFileName=$Run.trx" 2>&1 | Out-File -Encoding utf8 "$evidence/$Run.log"
$code = $LASTEXITCODE
@{startedUtc=$started;finishedUtc=[DateTime]::UtcNow;exitCode=$code;runtime=$runtime;testAssemblySha256=(Get-FileHash "$runtime/PointOfSale.Tests.dll").Hash} | ConvertTo-Json | Out-File -Encoding utf8 "$evidence/$Run-command.json"
exit $code
