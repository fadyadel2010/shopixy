# SHOPIXY Paymob P6.1 API Key Integration Discovery

Date: 2026-08-14

Project: SHOPIXY / PointOfSale

Certification: BLOCKED - ACCOUNT CONFIGURATION

## 1. Executive Summary

P6.1 attempted the required same-account Paymob API-key integration discovery. The runtime tenant Paymob configuration still does not contain a Paymob API Key, so Paymob token authentication and integration-list discovery cannot be executed safely.

The existing Secret Key remains valid for Egypt/Test gateway discovery, and Paymob still rejects dashboard-visible Integration ID `3153090` during Intention creation with HTTP `404`.

No tenant configuration remediation was performed because no API-visible same-account candidate integration was discovered.

## 2. API Key Source

Runtime Paymob credential authority:

| Field | Result |
| --- | --- |
| Source identifier | `TenantPaymentMethod.ConfigEncrypted` |
| Tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Data protection purpose | `PaymentMethodSecrets` |
| API Key configured | NO |
| API Key decryptable | NO |

The local forensic helper obtains the database connection from `PointOfSale.API/appsettings.Development.local.json`, then decrypts the tenant-scoped Paymob configuration from `TenantPaymentMethod.ConfigEncrypted`. The API key value was not printed, logged, or persisted.

Because the API key is absent, P6.1 source remediation stops here by design. An API Key must be configured from the same Paymob Test dashboard account, same merchant account, and same MID `645875` before integration-list discovery can continue.

## 3. Secure Credential Context

Command:

```text
dotnet run --project .codex-temp\PgForensics\PgForensics.csproj --no-restore -- p6-paymob-context 3d58ccb9-c8c3-4278-9a39-e34de38663ef
```

Result:

| Field | Result |
| --- | --- |
| Public Key configured | YES |
| Secret Key configured | YES |
| Secret Key decryptable | YES |
| HMAC configured | YES |
| HMAC decryptable | YES |
| API Key configured | NO |
| Configured MID | `645875` |
| Configured Integration ID | `3153090` |
| Configured iframe ID | `<null>` |
| Tenant sandbox flag | `True` |
| Public Key country | `egy` |
| Public Key mode | `test` |
| Secret Key country | `egy` |
| Secret Key mode | `test` |
| Public/Secret country match | PASS |
| Public/Secret mode match | PASS |
| Secret implied region | Egypt |
| Secret implied environment | Test |

## 4. Paymob Auth

Official/supported discovery flow identified in P6:

```text
POST https://accept.paymob.com/api/auth/tokens
GET  https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

P6.1 result:

| Field | Result |
| --- | --- |
| Auth endpoint | `POST https://accept.paymob.com/api/auth/tokens` |
| API Key configured | NO |
| Token created | NO |
| Token type | N/A |
| Safe provider error | N/A; request was not sent because API Key is absent |

No API Key or token was logged.

## 5. Integration Discovery

Command:

```text
dotnet run --project .codex-temp\PgForensics\PgForensics.csproj --no-restore -- p6-paymob-integration-discovery 3d58ccb9-c8c3-4278-9a39-e34de38663ef
```

Result:

| Field | Result |
| --- | --- |
| Base URL | `https://accept.paymob.com/` |
| Auth endpoint | `POST https://accept.paymob.com/api/auth/tokens` |
| Integrations endpoint | `GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false` |
| API Key configured | NO |
| Public/Secret country match | PASS |
| Public/Secret mode match | PASS |
| Requested mode | Test |
| Requested region | Egypt |
| Discovery status | `BLOCKED_API_KEY_NOT_CONFIGURED` |

No integration list was available.

## 6. Integration 3153090 Search

Integration ID `3153090` could not be searched in the API-visible integration list because the API-key auth prerequisite is missing.

Known provider comparison remains:

```text
POST https://accept.paymob.com/v1/intention/
payment_methods: [3153090]
amount: 41000
currency: EGP
```

Provider response:

```text
HTTP 404
{"detail":"Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"}
```

## 7. Relevant Integration Set

No API-visible UIG/EGP/Non-Shopify/online_new set could be constructed because integration-list discovery is blocked by the missing API Key.

Secret-Key-only gateway discovery did return Paymob gateway metadata:

| Field | Result |
| --- | --- |
| Endpoint | `GET https://accept.paymob.com/api/ecommerce/gateways` |
| Authorization | `Token <redacted>` |
| HTTP status | `200` |
| Result count | `21` |
| UIG present | YES |
| UIG label | `Mobile Wallets` |

This proves the current Secret Key can access Egypt/Test provider APIs and that `UIG` exists as a gateway type. It does not prove that Integration `3153090` belongs to this Secret-Key account context.

## 8. MID Reconciliation

| Field | Value |
| --- | --- |
| Dashboard MID | `645875` |
| Tenant configured MID | `645875` |
| API integration MID | UNPROVEN |
| Match | UNPROVEN |

The Secret-Key-only endpoints used so far do not expose the merchant MID. The API-key integration list is required for stronger reconciliation.

## 9. Environment/Region

| Field | Result |
| --- | --- |
| Dashboard environment | TEST |
| Tenant sandbox flag | `True` |
| Public Key context | Egypt/Test |
| Secret Key context | Egypt/Test |
| API endpoint | `https://accept.paymob.com` |
| Region/environment mismatch evidence | None |

P6.1 did not mix `accept-alpha`, production, or iframe endpoints.

## 10. Decision Matrix

Question:

| Question | Answer |
| --- | --- |
| Visible in Dashboard? | YES |
| Visible via API integration list? | UNPROVEN |
| API accepts Intention? | NO |
| Currency = EGP? | YES in Dashboard; request uses EGP |
| Gateway = UIG? | YES in Dashboard; UIG gateway visible to Secret Key |
| Channel = online_new? | YES in Dashboard; API list unavailable |
| Type = Non-Shopify? | YES in Dashboard; API list unavailable |
| Same MID? | UNPROVEN |
| Same Test account? | UNPROVEN |

Conclusion: UNPROVEN, with active provider rejection during Intention creation.

## 11. Candidate Integration(s)

No candidate integration was discovered.

No random Integration ID was selected. No first UIG integration, newest integration, iframe-linked integration, different gateway, different MID, different currency, Shopify integration, CAGG, MIGS, or CASH integration was used.

## 12. Controlled Intention Tests

Command:

```text
dotnet run --project .codex-temp\PgForensics\PgForensics.csproj --no-restore -- p6-paymob-direct-intention 3d58ccb9-c8c3-4278-9a39-e34de38663ef 3153090
```

Safe request evidence:

| Field | Value |
| --- | --- |
| Endpoint | `POST https://accept.paymob.com/v1/intention/` |
| Authorization | `Token <redacted>` |
| Configured Integration ID | `3153090` |
| Tested Integration ID | `3153090` |
| Amount | `41000` |
| Currency | `EGP` |
| payment_methods | `[3153090]` |
| Notification URL | `http://localhost:5010/api/webhooks/paymob` |
| Redirection URL | `http://localhost:5010/api/webhooks/paymob/callback` |

Safe response evidence:

| Field | Value |
| --- | --- |
| HTTP status | `404` |
| Provider request ID | `<null>` |
| Provider body | `{"detail":"Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"}` |
| client_secret returned | NO |
| Intention ID returned | NO |

No candidate comparison was possible.

## 13. Root Cause

First failing P6.1 operation:

```text
API-key integration discovery prerequisite
```

Exact source symbol:

```text
.codex-temp/PgForensics/Program.cs
LoadPaymobConfig
DiscoverP6PaymobIntegrations
```

Exact evidence:

```text
api_key_configured NO
discovery_status BLOCKED_API_KEY_NOT_CONFIGURED
```

Root cause:

The tenant-scoped Paymob runtime configuration does not contain the Paymob API Key needed to authenticate to Paymob's supported integration-list endpoint. Therefore SHOPIXY cannot prove which integrations the same Test dashboard account exposes via API, and cannot safely reconcile dashboard Integration `3153090` with the current runtime Secret-Key context.

Impact:

The runtime remains blocked on account/integration reconciliation. Intention creation with `3153090` still fails, and no safe integration ID replacement can be made.

Confidence level: HIGH for the missing API-key block; HIGH that `3153090` is rejected by Intention under the current Secret Key; UNPROVEN for the exact dashboard/API account membership mismatch subtype.

## 14. Minimal Remediation

No source or tenant configuration remediation was performed.

Safe next remediation prerequisite:

1. Configure the Paymob API Key from the same Paymob Test dashboard account.
2. Ensure it belongs to the same merchant account/MID `645875`.
3. Re-run `p6-paymob-integration-discovery`.
4. Only update `integrationId` if a fully matched same-account candidate is discovered and succeeds with Intention.

## 15. Post-Remediation Verification

Not run. No remediation was made.

Expected verification after API Key configuration:

```text
API Key authenticates = YES
integration list returned = YES
3153090 found/not found = PROVEN
candidate Intention succeeds = YES, if candidate exists
Storefront Paymob readiness = READY
Unified Checkout handoff available = YES
```

## 16. Security

Security results:

| Control | Result |
| --- | --- |
| API Key printed | NO |
| Secret Key printed | NO |
| HMAC printed | NO |
| Public Key printed | NO |
| Auth token printed | NO |
| client_secret printed | NO |
| Tenant config remains encrypted | YES |
| Iframe modified | NO |
| Adapter architecture modified | NO |

The controlled Intention diagnostic prints only redacted authorization metadata and non-secret request fields.

## 17. Cross-Provider Regression

No production source changes were made in P6.1, so no direct source-level regression was introduced for COD, Manual Wallet, Paymob readiness architecture, C5.3, C5.5, P4, or P5.

Build verification still ran as a safety check.

## 18. Build/EF

Verification commands:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~Paymob"
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~PaymentMethodConfigurationValidator|FullyQualifiedName~StorefrontPaymentMethodAvailability"
dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -p:UseAppHost=false -p:OutDir=C:\tmp\shopixy-p6-1-api-out\ -v:minimal
dotnet build PointOfSale.sln --no-restore -p:UseAppHost=false -p:OutDir=C:\tmp\shopixy-p6-1-sln-out\ -v:minimal
npm run build (from PointOfSale.Storefront)
dotnet ef migrations has-pending-model-changes
git diff --check
```

Results:

| Command | Result |
| --- | --- |
| Paymob tests | PASS - 174 passed |
| Payment configuration/storefront availability tests | PASS - 8 passed |
| Broad checkout/payment exploratory filter | FAIL - 57 preexisting unrelated failures surfaced by broad filter |
| API build | PASS - 176 existing warnings, 0 errors |
| Solution build | PASS - 389 existing warnings, 0 errors |
| Storefront build | PASS - existing Browserslist/chunk warnings |
| EF pending model changes | PASS - no pending model changes |
| `git diff --check` | PASS - existing LF/CRLF warnings only |

The broad exploratory filter was:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~Paymob|FullyQualifiedName~PaymentLifecycle|FullyQualifiedName~StorefrontPayment|FullyQualifiedName~PaymentMethodConfigurationValidator|FullyQualifiedName~Checkout"
```

It selected unrelated checkout, marketplace, coupon, commerce hub, and EF warning-sensitive suites. The P6.1-specific Paymob and payment configuration surfaces passed when run with focused filters.

## 19. Remaining Limitations

The exact account mismatch subtype remains unresolved until the same-account Paymob API Key is configured.

Remaining unknowns:

| Question | Status |
| --- | --- |
| Does API integration list include `3153090`? | UNPROVEN |
| Does API-visible integration metadata expose MID `645875`? | UNPROVEN |
| Is `3153090` stale, disabled, unpublished, or account-mismatched? | UNPROVEN |
| Is there another fully matched UIG/EGP/online_new/Non-Shopify Test integration? | UNPROVEN |

## 20. Worktree Safety

Before P6.1:

```text
git status --short: dirty tree with existing P4/P5/C5 and related changes
git diff --stat: 181 files changed, 6762 insertions(+), 2801 deletions(-)
```

No reset, clean, restore, stash, commit, or push was performed.

After P6.1 verification will be recorded at completion.

After P6.1:

```text
git status --short: dirty tree remains; P6.1 added docs/architecture/SHOPIXY_PAYMOB_P6_1_API_KEY_INTEGRATION_DISCOVERY_2026-08-14.md
git diff --stat: 181 files changed, 6762 insertions(+), 2801 deletions(-)
git diff --check: PASS, existing LF/CRLF warnings only
```

No reset, clean, restore, stash, commit, or push was performed.

## 21. Final Certification

BLOCKED - ACCOUNT CONFIGURATION

Reason:

The same-account Paymob API Key required for supported integration-list discovery is not configured in the tenant runtime credential authority. Intention creation with dashboard-visible Integration `3153090` still returns HTTP `404`, and no API-visible same-account replacement integration has been discovered.

P6.1 did not remediate configuration because doing so would require guessing.
