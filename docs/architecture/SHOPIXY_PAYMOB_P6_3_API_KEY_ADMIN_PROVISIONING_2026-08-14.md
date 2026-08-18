# SHOPIXY Paymob P6.3 - API Key Admin Provisioning

Date: 2026-08-14

## 1. Executive Summary

P6.3 implemented the missing Paymob API Key admin provisioning capability using the existing encrypted credential architecture.

The source now supports Paymob `apiKey` as a protected admin credential in both tenant payment-method configuration and the super-admin Paymob provider configuration shell.

Runtime Paymob integration discovery remains blocked because the tenant for the tested order still has `api_key_configured = NO`. No Paymob API Key value was available in secure tenant storage during this run, so `POST https://accept.paymob.com/api/auth/tokens` was not sent and the integration list could not be fetched.

Final certification: BLOCKED.

## 2. Existing Configuration Contract

Canonical property name: `apiKey`.

Existing source contract before P6.3:

- `PaymentMethodConfigurationValidator.NormalizeConfiguration` preserved arbitrary non-empty fields, including `apiKey`.
- `PaymentMethodsAdminController` merged normalized incoming configuration with decrypted stored configuration.
- `PaymentMethodsAdminController` persisted the merged configuration through `PaymentMethodSecrets`.
- `PaymobTenantConfigurationResolver` read `apiKey` from decrypted `TenantPaymentMethod.ConfigEncrypted`.
- `TenantPaymobConfiguration` already contained `ApiKey`.

No duplicate field was introduced. P6.3 uses only `apiKey`.

## 3. Admin UX Changes

File: `PointOfSale.Web/src/pages/PaymentMethods.jsx`

Defect:

- Symptom: merchant/admin could not enter the Paymob Dashboard API Key.
- First failing point: Paymob configuration field list omitted `apiKey`.
- Root cause: UI exposed Public Key, Secret Key, Integration ID, HMAC, merchant ID, and legacy iframe ID, but not the separate account-level Paymob API Key needed for auth-token based integration discovery.
- Fix: added a Paymob `API Key` password field with help text stating it is encrypted and blank preserves the existing API Key.
- Security impact: write-only UI field; no GET prefill; no plaintext display.
- Regression test: `PaymentMethods.test.tsx` verifies the field is present, password-style, and submits `apiKey` only when entered.

The field labels in the configuration modal now use `htmlFor`/`id`, improving accessibility and making the write surface testable.

## 4. Backend Provisioning

File: `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`

Defect:

- Symptom: super-admin Paymob configuration could not submit an API Key as a protected credential replacement.
- First failing point: `AllowedSecretReplacementKeys` omitted `apiKey`.
- Root cause: P4/P5 modern checkout work made Secret Key and HMAC protected replacements, but left the Paymob Dashboard API Key out of the admin secret replacement allowlist.
- Fix: added `apiKey` to protected replacement keys and dashboard secret definitions.
- Security impact: `apiKey` is rejected if submitted in ordinary configuration and accepted only through the protected `secretReplacements` envelope.
- Regression test: `PaymentAdmin2PaymobProviderConfigurationTests` verifies protected save, redacted changed fields, operational availability, and blank-preserve behavior.

The service status calculation was tightened so Paymob checkout configuration remains based on required checkout secrets: `secretKey` + `hmacSecret`. Adding `apiKey` alone does not make Paymob checkout configured.

## 5. Encryption

Tenant path:

- Write authority: `PaymentMethodsAdminController.UpdateConfiguration`.
- Protection purpose: `PaymentMethodSecrets`.
- Storage: `TenantPaymentMethod.ConfigEncrypted`.
- Test evidence: `UpdateConfiguration_WhenPaymobApiKeyIsSubmitted_EncryptsAndRedactsIt` decrypts with the same protector and confirms `apiKey` is present only inside protected configuration.

Super-admin provider shell:

- Write authority: `PaymentProviderAdministrationService.SaveProviderConfiguration`.
- Protected replacement field: `apiKey`.
- Stored in protected secret dictionary with the existing provider administration protector.

No new secret store or encryption mechanism was introduced.

## 6. Redaction

Redaction behavior after P6.3:

- Paymob `apiKey` is not accepted in the ordinary configuration envelope.
- Public read DTO shows only `apiKeyConfigured` and `apiKeyMask`.
- Changed-field audit emits `credentials.accountApiKey:redacted`.
- Returned provider DTO JSON does not contain the submitted API Key value.
- Tenant payment-method GET still returns no Paymob secret public configuration.

Focused tests verify returned DTO JSON does not include the API Key placeholder.

## 7. Preserve-on-Blank Semantics

Merchant/admin tenant flow:

- `sanitizeConfiguration` removes blank fields before the PUT request.
- `PaymentMethodsAdminController` merges incoming non-empty fields with decrypted stored configuration.
- Blank `apiKey` therefore preserves the existing encrypted `apiKey`.

Super-admin provider flow:

- `NormalizeSecretReplacements` ignores blank protected replacements.
- Existing `apiKey` stays in protected secrets unless a non-empty replacement is supplied.

Tests:

- `UpdateConfiguration_WhenPaymobApiKeyIsBlank_PreservesExistingEncryptedApiKey`
- `PaymobConfiguration_BlankSecretReplacementPreservesExistingApiKey`

## 8. Paymob Authentication

Required provider call:

```text
POST https://accept.paymob.com/api/auth/tokens
```

Runtime evidence from P6.3:

```text
api_key_configured NO
discovery_status BLOCKED_API_KEY_NOT_CONFIGURED
```

Because no separate Paymob API Key was present in encrypted tenant configuration, the auth-token call was intentionally not sent.

## 9. Integration Discovery

Required provider call after successful API-key auth:

```text
GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

P6.3 result:

```text
integration list unavailable
reason: API Key not configured in tenant encrypted config
```

No integration IDs were guessed or substituted.

## 10. 3153090 Reconciliation

Dashboard context remains:

```text
MID: 645875
Integration ID: 3153090
Currency: EGP
Channel: online_new
Payment Method: UIG
Type: Non-Shopify
```

Runtime context remains:

```text
configured_mid: 645875
configured_integration_id: 3153090
configured_region: Egypt
configured_environment: Test
public_secret_country_match: PASS
public_secret_mode_match: PASS
api_key_configured: NO
```

Decision matrix:

| Field | Dashboard | API |
| --- | --- | --- |
| Environment | Test | Test inferred from keys |
| MID | 645875 | Tenant configured 645875; API-visible MID unproven |
| Integration ID | 3153090 | Integration list unavailable |
| Currency | EGP | Intention request EGP |
| Gateway | UIG | Gateway discovery previously showed UIG present |
| Channel | online_new | Unproven by integration list |
| Type | Non-Shopify | Unproven by integration list |
| Status | Dashboard visible | Unproven |
| Deprecated | Dashboard not deprecated by observation | Unproven |
| Standalone | Non-Shopify by dashboard | Unproven |

Conclusion: UNPROVEN.

## 11. Candidate Integrations

No candidate integrations were selected.

Reason: the same-account integration list could not be fetched without a configured Paymob API Key.

P6.3 did not change Integration ID `3153090`.

## 12. Controlled Intention Test

No new controlled Intention test was run after API-key provisioning because provisioning did not occur.

Most recent established evidence remains P6/P6.1:

```text
POST https://accept.paymob.com/v1/intention/
payment_methods: [3153090]
amount: 41000
currency: EGP
HTTP 404
detail: Integration ID/Name does not exist in our system
```

No `client_secret` was returned.

## 13. Configuration Remediation

Source remediation completed:

- Added merchant Paymob API Key password field.
- Added super-admin Paymob API Key protected replacement support.
- Added redaction metadata for `apiKeyConfigured` and `apiKeyMask`.
- Added preserve-on-blank tests.
- Added tenant encrypted storage/redaction tests.

Runtime tenant configuration remediation not completed:

- The actual Paymob Dashboard API Key was not available in secure tenant configuration.
- No tenant integration ID change was made.

## 14. Security

Verified source behavior:

- API Key is server credential material.
- API Key is not accepted in ordinary super-admin configuration.
- API Key is accepted only through protected replacement fields.
- API Key is masked in admin read DTOs.
- API Key is not returned in merchant payment-method public configuration.
- API Key is not persisted in plaintext source or appsettings.
- Existing Secret Key, HMAC, Public Key, MID, and Integration ID were not changed.
- Iframe behavior was not changed.

Important operator note: credentials pasted into chat earlier must be considered exposed and rotated before long-lived or production use.

## 15. Regression

Focused regression coverage added/updated:

- Admin Paymob form exposes API Key field.
- API Key input is password-style.
- API Key save reaches the existing configure endpoint.
- API Key is encrypted into tenant configuration.
- API Key is redacted from tenant response DTOs.
- Blank API Key preserves existing encrypted value.
- Super-admin protected credential replacement supports `apiKey`.
- Super-admin GET returns configured/masked metadata, not value.
- Super-admin blank replacement preserves existing API Key.
- Checkout readiness still depends on Secret Key and HMAC, not API Key alone.

No COD, Manual Wallet, Paymob adapter, Intention API, HMAC, or iframe architecture changes were made.

## 16. Live Admin Verification

Automated UI verification:

```text
PaymentMethods.test.tsx
4 tests passed
```

Verified:

- Paymob configuration modal contains API Key field.
- API Key field is password type.
- Save submits `apiKey` only when typed.
- Blank field is not sent by the UI sanitizer.

Live provider discovery was not executed because the actual Paymob Dashboard API Key was not configured into the tenant runtime.

## 17. Build/EF

Commands run:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~PaymentAdmin2PaymobProviderConfigurationTests|FullyQualifiedName~PaymentMethodsAdminSafetyTests"
Passed: 20

dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~PaymentMethodConfigurationValidatorTests|FullyQualifiedName~PaymobPaymentAdapterCertificationTests|FullyQualifiedName~PaymobProductionOperationalReadinessTests|FullyQualifiedName~StorefrontPaymentMethodAvailabilityTests"
Passed: 20

npm test -- src/pages/PaymentMethods.test.tsx --run --maxWorkers=1 --no-fileParallelism
Passed: 4

npm test -- PaymentMethods.test.tsx --run --maxWorkers=1 --no-fileParallelism
Passed: 4

dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
Passed

dotnet build PointOfSale.sln --no-restore
Passed

npm run build
Passed

dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj
No pending model changes

git diff --check
Passed with existing LF/CRLF warnings
```

Initial EF command from repo root without explicit project failed with:

```text
No project was found. Change the current working directory or use the --project option.
```

The explicit project/startup command succeeded.

## 18. Worktree Safety

Before P6.3:

```text
181 files changed, 6762 insertions(+), 2801 deletions(-)
```

The worktree already contained many modified and untracked files from Shipping, Checkout C1-C5.7, Payment Methods, Manual Wallet, Paymob P4, P5, P6, P6.1, and P6.2.

P6.3 touched only Paymob/admin configuration surfaces, focused tests, and this report.

No reset, clean, restore, stash, commit, or push was performed.

## 19. Remaining Limitations

The source/admin provisioning defect is fixed, but the runtime Paymob discovery milestone is still blocked until an operator enters the separate Paymob Dashboard API Key through the new secure admin field.

After provisioning, rerun:

```text
POST https://accept.paymob.com/api/auth/tokens
GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

Then reconcile Integration ID `3153090` from the returned integration list before any tenant Integration ID change.

## 20. Final Certification

BLOCKED

Reason:

- API Key provisioning capability is implemented and tested.
- API Key is not yet configured in the tested tenant runtime.
- Paymob auth-token call could not be sent.
- Integration list is unavailable.
- Integration `3153090` remains unreconciled through same-account API discovery.
- Intention still cannot be certified successful.

Do not proceed to Unified Checkout or sandbox payment finality until the actual same-account Paymob API Key is configured and integration discovery succeeds.
