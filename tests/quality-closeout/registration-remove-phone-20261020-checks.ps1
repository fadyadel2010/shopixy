param([ValidateSet('frontend','backend','api-build')][string]$Check)
$ErrorActionPreference = 'Continue'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/registration-remove-phone-20261020'
New-Item -ItemType Directory -Force $out | Out-Null
function Record($name, $command, [scriptblock]$action) {
    & $action *> (Join-Path $out "$name.log")
    $code = $LASTEXITCODE
    @{command=$command;exitCode=$code;observedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $out "$name-command.json")
    Write-Output "$name exit=$code"
    Get-Content (Join-Path $out "$name.log") -Tail 8
}
if ($Check -eq 'frontend') {
    Set-Location (Join-Path $root 'PointOfSale.Storefront')
    Record 'storefront-tests' 'npx vitest run src/auth src/services/api/httpClient.privacy.test.ts src/utils/phone.test.ts src/utils/checkoutFieldRules.test.ts src/store/checkoutStore.validation.test.ts src/components/checkout/ShippingStep.localization.test.ts' {
        npx vitest run src/auth src/services/api/httpClient.privacy.test.ts src/utils/phone.test.ts src/utils/checkoutFieldRules.test.ts src/store/checkoutStore.validation.test.ts src/components/checkout/ShippingStep.localization.test.ts
    }
    Record 'storefront-typecheck' 'npx tsc --noEmit' { npx tsc --noEmit }
    Record 'storefront-build' 'npm run build' { npm run build }
    Record 'storefront-lint' 'node ../PointOfSale.Web/scripts/lint.cjs <five changed files> --resolve-plugins-relative-to ../PointOfSale.Web --max-warnings 0' {
        node ../PointOfSale.Web/scripts/lint.cjs src/auth/components/RegisterForm.tsx src/auth/api/authApi.ts src/auth/api/authApi.registration.test.ts src/auth/types/auth.types.ts src/auth/pages/RegisterPage.registration.test.tsx --resolve-plugins-relative-to ../PointOfSale.Web --max-warnings 0
    }
} elseif ($Check -eq 'api-build') {
    Set-Location $root
    Record 'api-build' 'dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore' { dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore }
} else {
    Set-Location $root
    $env:REGISTRATION_PHONE_CONFIGURED = '1'
    $classes = @('StorefrontRegistrationPhoneConfiguredTests','StorefrontAuthenticationLifecycleTests','PhoneNumberNormalizerTests','PhoneRiskProfileServiceTests','PhoneRiskPhase4Tests','StorefrontTenantIsolationTests','StorefrontPhoneVerificationReliabilityTests','StorefrontAuthProviderAdminServiceOutboxTests','CookieAuthSecurityTests','StorefrontCustomerAuthorityCertificationTests')
    $filter = ($classes | ForEach-Object { 'FullyQualifiedName~' + $_ + '.' }) -join '|'
    $filter | Set-Content (Join-Path $out 'backend-filter.txt')
    Record 'backend-regression' "dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter $filter --logger trx --results-directory <task evidence>/trx" {
        dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter $filter --logger 'trx;LogFileName=backend.trx' --results-directory (Join-Path $out 'trx')
    }
}
