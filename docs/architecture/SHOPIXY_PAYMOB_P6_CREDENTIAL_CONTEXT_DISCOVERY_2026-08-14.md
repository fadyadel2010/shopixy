# SHOPIXY Paymob P6 Credential Context Discovery

Date: 2026-08-14
Project: SHOPIXY / PointOfSale
Scope: Paymob credential/account/environment/integration reconciliation after P5 source remediation.

## 1. Executive Summary

P6 investigated why the Paymob Dashboard-visible Integration ID `3153090` is rejected by Paymob Intention API when SHOPIXY uses the current secure tenant credentials.

Conclusion:

```text
The current Secret Key is valid for Egypt/Test Paymob API access.
The current Public Key and Secret Key are region/mode consistent.
The configured tenant MID is 645875.
The configured tenant Integration ID is 3153090.
The same Secret-Key context rejects payment_methods [3153090] at Intention creation with HTTP 404.
The tenant config does not contain the legacy API key required by Paymob's official PHP library integration-list discovery flow.
```

Final certification:

```text
BLOCKED - CREDENTIAL/ACCOUNT MISMATCH
```

No production source configuration was changed. No iframe behavior was touched.

## 2. Current Contradiction

Dashboard-visible state supplied for P6:

```text
Mode: TEST
MID: 645875
Integration ID: 3153090
Currency: EGP
Channel: online_new
Payment Method: UIG
Type: Non-Shopify
```

Runtime Paymob Intention request:

```text
POST https://accept.paymob.com/v1/intention/
Authorization: Token <redacted>
payment_methods: [3153090]
amount: 41000
currency: EGP
```

Provider response:

```text
HTTP 404
Integration ID/Name does not exist in our system.
```

## 3. Secure Credential Context

Observed through the existing encrypted tenant payment-method configuration path. Secret values were not printed.

```text
TenantId: 83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5
OrderId: 3d58ccb9-c8c3-4278-9a39-e34de38663ef
OrderNumber: ORD-83B3-2026-01014
Public Key configured: YES
Secret Key configured: YES
Secret Key decryptable: YES
HMAC configured: YES
HMAC decryptable: YES
Legacy API Key configured: NO
Configured MID: 645875
Configured Integration ID: 3153090
Configured iframe ID: <null>
Tenant IsSandbox: true
Public Key country: egy
Public Key mode: test
Secret Key country: egy
Secret Key mode: test
Public/Secret country match: PASS
Public/Secret mode match: PASS
Secret implied region: Egypt
Secret implied environment: Test
```

## 4. API Account Discovery

Official documentation confirms the Intention API flow uses `POST /v1/intention/`, a Secret Key in the `Authorization: Token` header, integration IDs in `payment_methods`, and a `client_secret` for Unified Checkout.

Paymob's official PHP library also contains an integration-list discovery flow:

```text
POST {region}/api/auth/tokens
GET  {region}/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

That flow requires the legacy API key to obtain a bearer token. The current tenant config does not contain that API key, so API-visible integration listing is blocked.

Secret-Key-only gateway discovery was available through Paymob's official PHP library flow and succeeded:

```text
GET https://accept.paymob.com/api/ecommerce/gateways
Authorization: Token <redacted>
HTTP 200
Gateway result count: 21
UIG listed: YES
```

This proves the Secret Key can authenticate against Egypt/Test Paymob API context, but it does not prove Integration ID membership.

References:

- https://developers.paymob.com/paymob-docs/integration-paths/apis
- https://developers.paymob.com/paymob-docs/developers/checkout-experiences
- https://developers.paymob.com/paymob-docs/developers/quicklink-apis/overview
- https://wizard.paymob.com/
- https://gitlab.com/paymob-integrations/php-lib/-/raw/main/src/Paymob.php

## 5. Dashboard Context

Dashboard context is user-supplied and visible in TEST mode:

```text
MID: 645875
Integration ID: 3153090
Currency: EGP
Channel: online_new
Payment Method: UIG
Type: Non-Shopify
```

Dashboard limitation:

```text
Cannot create more than 1 test integration with the same gateway type and currency.
```

No attempt was made to create, delete, edit, or replace a Paymob integration.

## 6. Dashboard vs API Reconciliation

| Field | Dashboard | API Context | Match? |
| --- | --- | --- | --- |
| Environment | TEST | Secret Key mode is TEST | PASS |
| MID | 645875 | Tenant config says 645875; Secret-Key API MID not discoverable without API key | PARTIAL |
| Region | Egypt | Secret/Public key country `egy`; gateway endpoint `accept.paymob.com` returned HTTP 200 | PASS |
| Currency | EGP | Intention request sends EGP | PASS |
| Integration 3153090 | Visible | Intention API rejects with HTTP 404 | FAIL |
| Gateway type | UIG | Gateway discovery lists UIG | PARTIAL |
| Channel | online_new | Not discoverable without integration list API key | UNPROVEN |
| Payment method | UIG | UIG gateway type visible; exact integration membership rejected | PARTIAL/FAIL |

Canonical conclusion:

```text
PARTIAL MATCH
```

Region and mode match. Gateway type exists in the Secret-Key context. Integration `3153090` is not accepted by the Secret-Key Intention context.

## 7. MID Verification

Configured MID:

```text
645875
```

API-visible MID:

```text
UNPROVEN
```

Reason: the current tenant config lacks the legacy API key required by Paymob's official integration-list/auth-token flow. Secret-Key-only Intention and gateway endpoints did not expose MID.

## 8. Public/Secret Consistency

Result:

```text
Public key/account consistency: PASS for country/mode
Public key/account consistency: UNPROVEN for merchant account
```

Evidence:

```text
Public Key country: egy
Secret Key country: egy
Public Key mode: test
Secret Key mode: test
```

The keys are both Egypt/Test. Merchant-account consistency cannot be proven without an API endpoint that returns account identity or the missing API key discovery flow.

## 9. Integration Discovery

Official-library integration discovery status:

```text
BLOCKED_API_KEY_NOT_CONFIGURED
```

Because no API key is configured, P6 could not list the integrations visible to the current Paymob account context.

Secret-Key gateway discovery status:

```text
HTTP 200
UIG gateway listed: YES
```

Gateway discovery is not integration discovery. It confirms gateway availability, not that Integration ID `3153090` belongs to the Secret Key.

## 10. Integration 3153090 Analysis

| Integration ID | Account | MID | Environment | Gateway | Currency | Channel | API-visible? | Intention-eligible? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 3153090 | Dashboard-visible account | 645875 dashboard/configured | TEST | UIG dashboard/gateway type exists | EGP dashboard/request | online_new dashboard | Not listable without API key | NO, Paymob HTTP 404 |

Exact discrepancy:

- Symptom: Dashboard-visible integration is rejected by runtime Intention API.
- Provider endpoint: `POST https://accept.paymob.com/v1/intention/`
- HTTP status: `404`
- Account context: Egypt/Test Secret Key, API-authenticating for gateway discovery.
- Likely cause: Integration `3153090` is not attached to the current Secret Key's Intention account context, or the Dashboard view and tenant Secret Key are not the same API account context.
- Confidence: High that `3153090` is not Intention-eligible for this Secret Key; medium on the exact account mismatch subtype because integration listing is blocked by missing API key.
- Safe remediation: provide/configure the matching Paymob API key for discovery, or rotate tenant Public/Secret/HMAC/API Key together from the same Dashboard account, then rerun P6.

## 11. Environment/Region Analysis

Environment:

```text
Secret Key mode: test
Public Key mode: test
Tenant IsSandbox: true
```

Region:

```text
Secret Key country: egy
Public Key country: egy
Endpoint used: https://accept.paymob.com
Gateway discovery: HTTP 200
```

Region/mode are not the current blocker.

## 12. Direct Intention Comparison

Controlled P6 Intention request:

```text
POST https://accept.paymob.com/v1/intention/
Authorization: Token <redacted>
configured_integration_id: 3153090
tested_integration_id: 3153090
amount: 41000
currency: EGP
payment_methods: [3153090]
```

Provider response:

```text
HTTP 404
Provider request ID: <null>
Body: {"detail":"Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"}
```

No `client_secret` was returned.

## 13. Controlled Alternate Integration Test if available

No alternate integration was discovered.

Reason:

```text
Legacy API Key configured: NO
Integration-list discovery: BLOCKED_API_KEY_NOT_CONFIGURED
```

No random integration was tested. No tenant config was changed.

## 14. Root Cause

Root cause statement:

```text
The currently configured Egypt/Test Secret Key is valid for Paymob API access, but Integration ID 3153090 is not recognized by the Intention API under that Secret-Key context. The exact Dashboard/API membership mismatch cannot be fully reconciled because the tenant config lacks the API key required to list API-visible integrations.
```

Ruled out:

- P5 source initialization defect: already repaired.
- Wrong Paymob region: Secret/Public key country is `egy`, and `accept.paymob.com` gateway discovery succeeds.
- Wrong test/live mode between Public and Secret keys: both are `test`.
- Missing UIG gateway support: gateway discovery lists `UIG`.
- iframe issue: current path is Intention API to Unified Checkout.

Still unresolved:

- Whether Secret Key belongs to a different MID than the Dashboard.
- Whether Dashboard session shows a different account than the tenant Secret Key.
- Whether Integration `3153090` is stale, disabled, unpublished, or not attached to this Secret-Key context.
- Whether the missing API key would reveal a different API-visible integration set.

## 15. Minimal Remediation

No remediation was applied.

Evidence did not prove a safe replacement integration ID. The tenant configuration must not be changed blindly.

Safe next remediation choices:

1. Add the matching Paymob API key from the same TEST Dashboard account to the tenant's secure Paymob config, then rerun P6 integration discovery.
2. If Paymob support/dashboard confirms the current Secret/Public/HMAC/API Key set belongs to MID `645875`, ask Paymob why Intention rejects `3153090`.
3. If support confirms the current Secret Key belongs to a different account, rotate the entire tenant credential set together from the correct Dashboard account.

## 16. Post-Remediation Verification

Not executed because no safe configuration correction was proven.

Required after correction:

```text
POST /v1/intention/
payment_methods: [provider-accepted integration]
Expected: HTTP 200/201
client_secret returned: YES
```

## 17. Security

Verified:

- Secret Key stayed server-side.
- HMAC stayed server-side.
- API key, if present later, must stay server-side.
- Public Key was not printed.
- No secret value was added to source, logs, report, screenshots, or test snapshots.
- Tenant isolation remains tenant-scoped through `TenantPaymentMethod.ConfigEncrypted`.
- No iframe fallback was introduced.

## 18. Regression

P6 changed only the local forensic helper and added this report. Production payment source, checkout, state machine, HMAC, COD, Manual Wallet, P4, and P5 architecture were not changed in P6.

Minimum affected checks are therefore build/static checks plus Paymob diagnostic probes.

## 19. Build/EF

Executed after report creation:

```text
git diff --check
dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -p:UseAppHost=false -p:OutDir=C:\tmp\shopixy-p6-api-out\ -v:minimal
dotnet build PointOfSale.sln --no-restore -p:UseAppHost=false -p:OutDir=.codex-temp\p6-sln-out\ -v:minimal
npm run build
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build
```

Results:

```text
git diff --check: PASS, with existing LF/CRLF warnings only
API build: PASS, 176 existing warnings, 0 errors
Solution build: PASS, 389 existing warnings, 0 errors
Storefront build: PASS, existing Vite/Browserslist/chunk warnings
EF pending model changes: PASS, no changes have been made to the model since the last migration
```

No migration was created. No production Paymob source was changed in P6.

## 20. Remaining Limitations

- API-visible integration IDs cannot be listed because the secure tenant config does not contain the legacy API key required by Paymob's official library flow.
- Secret-Key-only endpoints proved API authentication and gateway availability, but not MID or integration membership.
- Paymob Dashboard screenshots remain secondary evidence; runtime API acceptance is the authority.
- Public HTTPS webhook finality is outside P6 and was not attempted.

## 21. Worktree Safety

Before P6:

```text
git status --short: dirty worktree with existing P4/C5/checkout/manual-wallet/payment changes.
git diff --stat: 181 files changed, 6762 insertions, 2801 deletions.
```

P6 did not reset, clean, restore, checkout, stash, commit, push, or modify unrelated work.

## 22. Final Certification

BLOCKED - CREDENTIAL/ACCOUNT MISMATCH

Reason:

```text
The current Secret Key is valid Egypt/Test API context, but Paymob Intention API still rejects Integration ID 3153090 with HTTP 404. The official integration-list discovery path is blocked because the tenant config lacks the API key required to list API-visible integrations. No provider-accepted replacement integration was discovered, so no tenant configuration change is safe.
```
