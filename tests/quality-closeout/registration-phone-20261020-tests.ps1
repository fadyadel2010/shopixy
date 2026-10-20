param([string]$Run = 'backend-regression')
$ErrorActionPreference = 'Stop'
$evidence = (Resolve-Path '.ai/evidence/registration-phone-20261020').Path
$assembly = (Resolve-Path '.artifacts/registration-phone-20261020/bin/PointOfSale.Tests/release/PointOfSale.Tests.dll').Path
$classes = @('StorefrontRegistrationPhoneConfiguredTests','StorefrontAuthenticationLifecycleTests',
    'PhoneNumberNormalizerTests','PhoneRiskProfileServiceTests','PhoneRiskPhase4Tests',
    'StorefrontTenantIsolationTests','StorefrontPhoneVerificationReliabilityTests',
    'StorefrontAuthProviderAdminServiceOutboxTests','CookieAuthSecurityTests')
$filter = ($classes | ForEach-Object { 'FullyQualifiedName~' + $_ + '.' }) -join '|'
$env:REGISTRATION_PHONE_CONFIGURED = '1'
$filter | Out-File -Encoding utf8 "$evidence/$Run.filter.txt"
$ErrorActionPreference = 'Continue' # Native stderr must not truncate failed-test evidence.
& dotnet vstest $assembly "/TestCaseFilter:$filter" "/ResultsDirectory:$evidence/trx" "/Logger:trx;LogFileName=$Run.trx" 2>&1 | Out-File -Encoding utf8 "$evidence/$Run.log"
$code = $LASTEXITCODE
@{observedUtc=[DateTime]::UtcNow.ToString('o');exitCode=$code;assemblySha256=(Get-FileHash $assembly).Hash;configuredRollbackOptIn=$true} | ConvertTo-Json | Out-File -Encoding utf8 "$evidence/$Run-command.json"
Get-Content "$evidence/$Run.log" -Tail 20
exit $code
