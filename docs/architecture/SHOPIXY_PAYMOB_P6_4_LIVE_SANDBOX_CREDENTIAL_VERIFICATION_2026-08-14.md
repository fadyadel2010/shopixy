# SHOPIXY Paymob P6.4 Live Sandbox Credential Verification

Date: 2026-08-14

Tenant: 83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5

Certification: BLOCKED

No credential values, auth tokens, or Paymob client secrets are included in this report.

## 1. Secure Credential Context

Runtime source: TenantPaymentMethod.ConfigEncrypted, decrypted through the existing PaymentMethodSecrets protection path.

| Field | Result |
| --- | --- |
| public_key_configured | YES |
| secret_key_configured | YES |
| secret_key_decryptable | YES |
| hmac_configured | YES |
| hmac_decryptable | YES |
| api_key_configured | YES |
| configured MID | 645875 |
| configured integrationId | 3153090 |
| configured iframeId | null |
| sandbox | YES |
| public key country/mode | egy / test |
| secret key country/mode | egy / test |
| public/secret country match | PASS |
| public/secret mode match | PASS |

Security note: operator-provided credential values appeared in the task conversation. P6.4 did not echo or persist those values in source, appsettings, logs, reports, or frontend artifacts. The live checks used the encrypted runtime tenant configuration.

## 2. API Key Authentication

Endpoint:

```text
POST https://accept.paymob.com/api/auth/tokens
```

Evidence:

| Field | Result |
| --- | --- |
| HTTP status | 201 |
| latency_ms | 777 |
| token_returned | YES |
| provider_request_id | null |
| api_visible_mid | 645875 |

Conclusion: the configured API Key authenticates successfully to Paymob Egypt/Test and exposes MID 645875.

## 3. Integration Discovery

Endpoint:

```text
GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

Evidence:

| Field | Result |
| --- | --- |
| HTTP status | 200 |
| latency_ms | 192 |
| integration_results_present | YES |
| integration_result_count | 1 |

Returned non-secret integration metadata:

| Integration ID | Name | Gateway | Currency | is_live | is_standalone | is_deprecated | Channel | Enabled |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 3153058 | null | VPC | EGP | false | false | false | null | null |

Configured integration visibility:

```text
configured_integration_api_visible = NO
```

## 4. 3153090 Reconciliation

| Field | Dashboard | API |
| --- | --- | --- |
| Environment | TEST | TEST, inferred from authenticated endpoint and integration is_live=false |
| MID | 645875 | 645875 |
| Integration ID | 3153090 | NOT FOUND |
| Currency | EGP | UNPROVEN for 3153090 |
| Gateway | UIG | UNPROVEN for 3153090 |
| Channel | online_new | UNPROVEN for 3153090 |
| Type | Non-Shopify | UNPROVEN for 3153090 |
| Status | Dashboard-visible | NOT API-visible |
| Deprecated | UNPROVEN | NOT API-visible |
| Standalone | UNPROVEN | NOT API-visible |

Classification for Integration 3153090: NOT_FOUND in the same API-key account integration list.

## 5. Intention Request

Endpoint:

```text
POST https://accept.paymob.com/v1/intention/
```

Safe request facts:

| Field | Value |
| --- | --- |
| authorization | Token redacted |
| configured_integration_id | 3153090 |
| tested_integration_id | 3153090 |
| amount | 41000 |
| currency | EGP |
| payment_methods | [3153090] |
| notification_url | http://localhost:5010/api/webhooks/paymob |
| redirection_url | http://localhost:5010/api/webhooks/paymob/callback |

The request body included order item, billing data, customer data, merchant reference, and callback URLs. No credential or client secret is recorded here.

## 6. Intention Response

Provider response:

| Field | Result |
| --- | --- |
| HTTP status | 404 |
| latency_ms | 727 |
| provider_request_id | null |
| intention ID present | NO |
| client_secret present | NO |
| checkout handoff available | NO |

Safe provider body:

```json
{
  "detail": "Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"
}
```

First failing operation:

```text
POST https://accept.paymob.com/v1/intention/
payment_methods = [3153090]
```

First real failure: Paymob API rejects Integration ID 3153090 with HTTP 404 under the same authenticated MID 645875 account context.

Controlled comparison after operator request:

| Field | Result |
| --- | --- |
| tested_integration_id | 3153058 |
| configured_integration_id | 3153090 |
| amount | 41000 |
| currency | EGP |
| payment_methods | [3153058] |
| HTTP status | 201 |
| intention ID present | YES |
| client_secret present | YES |
| checkout URL constructible | YES |
| response method metadata | online / EGP |
| response gateway metadata | MIGS in payment key, VPC in integration list |

Interpretation: Integration 3153058 is accepted by Intention and can open Unified Checkout, but it is not proven to be the intended Dashboard UIG/online_new/Non-Shopify integration. It is therefore a valid diagnostic comparison, not a safe replacement for 3153090 without merchant/provider confirmation that card/VPC/MIGS is the desired checkout method.

## 7. Checkout Handoff

No Paymob Unified Checkout handoff can be produced because Intention creation returns HTTP 404.

Expected successful handoff remains:

```text
client_secret returned = YES
Unified Checkout URL = https://accept.paymob.com/unifiedcheckout/?publicKey=<public_key>&clientSecret=<client_secret>
```

Actual P6.4 result:

| Field | Result |
| --- | --- |
| client_secret returned | NO |
| checkout URL constructible | NO |
| browser handoff legitimate | NO |

## 8. Storefront Navigation

Symptom:

```text
Place Order -> order created -> no Paymob checkout -> /checkout/pending
```

Source defect found:

| Item | Evidence |
| --- | --- |
| First bad frontend gate | `requireStorefrontPaymentSessionAuthority` accepted online provider responses without requiring any provider handoff artifact when no artifact field was present. |
| File/symbol | `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`, `requireStorefrontPaymentSessionAuthority` |
| Impact | A failed Paymob initialization could be presented as a normal pending checkout response instead of stopping navigation. |
| Fix | Non-terminal online providers now require a Stripe or Paymob handoff artifact. Missing handoff throws before `/checkout/pending` navigation. |
| Regression test | `paymentSessionAuthority.test.ts` rejects a non-terminal Paymob response without a Unified Checkout handoff and accepts a complete Unified Checkout URL. |

## 9. Browser Verification

Full browser sandbox checkout was not executed after the provider reconciliation because Intention remains blocked by Paymob HTTP 404 for the configured Integration ID. Opening a browser checkout would not reach Paymob until Paymob exposes or accepts a UIG/EGP integration under this same account context.

The source-side browser fallback defect is remediated by the storefront authority guard: a non-terminal Paymob checkout response without a Unified Checkout handoff is no longer accepted as a successful checkout submission.

## 10. Sandbox Payment

Not executed.

Reason: no Intention, no client_secret, and no Unified Checkout URL were returned by Paymob for Integration ID 3153090.

## 11. Callback/Webhook

Not executed.

Current local callback URLs are localhost HTTP URLs. These are acceptable for source/runtime payload construction tests, but Paymob final webhook delivery cannot be certified without public HTTPS ingress.

## 12. HMAC

HMAC configuration remains present and decryptable. No live Paymob callback was received in P6.4, so no live HMAC finality certification was possible.

Existing HMAC behavior was not weakened.

## 13. Payment Finality

No payment finality occurred because Paymob did not create an Intention.

State semantics applied in P6.4:

- Provider initialization failure is not payment processing.
- A pending page is legitimate only after a provider payment attempt exists or another payment method has valid pending evidence.
- Frontend handoff authority now rejects non-terminal online provider responses with no handoff artifact.

## 14. Order Identity

Diagnostic order used for provider request reconstruction:

| Field | Value |
| --- | --- |
| orderId | 3d58ccb9-c8c3-4278-9a39-e34de38663ef |
| orderNumber | ORD-83B3-2026-01014 |
| amount | 410.00 EGP |
| Paymob amount sent | 41000 |

P6.4 did not create a fresh live customer-facing Paymob order because the same-account integration discovery already proved the configured Integration ID is not API-visible and Intention still returns HTTP 404.

## 15. Retry/Idempotency

Not live-tested in a browser because Intention remains provider-blocked.

Source behavior improved: repeated checkout responses for online providers cannot be accepted as successful when they lack provider handoff evidence. This reduces duplicate-click and refresh ambiguity around a failed Paymob initialization.

## 16. Root Cause

Primary provider/configuration root cause:

| Field | Value |
| --- | --- |
| Symptom | Paymob selected, order created, browser never reaches Paymob Unified Checkout |
| First failing operation | Paymob Intention creation |
| Provider endpoint | POST https://accept.paymob.com/v1/intention/ |
| Provider status | HTTP 404 |
| Provider evidence | Integration ID/Name does not exist |
| API account evidence | API Key auth succeeds and returns MID 645875 |
| Integration list evidence | Same API account exposes integration 3153058 VPC/EGP only |
| Configured integration evidence | 3153090 is not API-visible |
| Root cause | Dashboard-visible Integration ID 3153090 is not exposed to or accepted by the same Paymob API account context used by SHOPIXY runtime credentials. |
| Confidence | HIGH |

Secondary source root cause:

| Field | Value |
| --- | --- |
| Symptom | Browser goes to `/checkout/pending` with no hosted checkout handoff |
| First failing source gate | `requireStorefrontPaymentSessionAuthority` |
| Root cause | Non-terminal online provider responses without Stripe/Paymob handoff artifacts were allowed through. |
| Impact | Payment initialization failure could look like normal payment processing. |
| Fix | Require online provider handoff artifacts before accepting non-terminal checkout responses. |

## 17. Fixes

Changed files:

- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.test.ts`

Implementation:

- Added an online-provider no-handoff rejection for non-terminal checkout responses.
- Added regression coverage for complete Paymob Unified Checkout handoff acceptance.
- Added regression coverage for Paymob no-handoff rejection.

No tenant Integration ID change was made. No iframe behavior was changed. No Paymob adapter architecture was changed.

## 18. Regression Tests

Executed:

```text
npm test -- paymentSessionAuthority.test.ts --run --maxWorkers=1 --no-fileParallelism
```

Result:

```text
1 file passed, 17 tests passed
```

Executed:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~PaymentAdmin2PaymobProviderConfigurationTests|FullyQualifiedName~PaymentMethodsAdminSafetyTests|FullyQualifiedName~PaymentMethodConfigurationValidatorTests|FullyQualifiedName~PaymobPaymentAdapterCertificationTests|FullyQualifiedName~PaymobProductionOperationalReadinessTests|FullyQualifiedName~StorefrontPaymentMethodAvailabilityTests"
```

Result:

```text
Passed: 40, Failed: 0
```

Executed:

```text
npm test -- src/pages/PaymentMethods.test.tsx --run --maxWorkers=1 --no-fileParallelism
```

Result:

```text
1 file passed, 4 tests passed
```

## 19. Security

Verified in P6.4:

- API Key is configured in encrypted tenant runtime configuration.
- API Key was used server-side only by the diagnostic path.
- Secret Key server-side only.
- HMAC server-side only.
- Auth token not printed.
- Paymob client_secret not printed.
- Public key not returned as a secret.
- No credential values were added to source or reports.
- Iframe 707560 remains outside the active Intention/Unified Checkout architecture.

Security recommendation: because Paymob credentials were pasted into the task conversation, rotate the Paymob Test API Key, Secret Key, and HMAC after this forensic run. Public key rotation is optional from a secrecy standpoint but may be included for hygiene.

## 20. Build/EF

Executed:

```text
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
dotnet build PointOfSale.sln --no-restore
npm run build
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj
```

Results:

| Check | Result |
| --- | --- |
| API build | PASS, 0 warnings, 0 errors |
| Solution build | PASS, 0 warnings, 0 errors |
| Storefront build | PASS with existing Vite chunk warnings |
| Admin Web build | PASS |
| EF pending model changes | PASS, no changes |
| git diff --check | PASS, existing LF-to-CRLF warnings only |

## 21. Remaining Limitations

- Integration ID 3153090 is visible in the Dashboard according to operator evidence, but it is not returned by the same account API integration list.
- The only API-visible integration is 3153058 with gateway VPC/EGP, and controlled Intention for 3153058 succeeds. It still does not match the required UIG/online_new/Non-Shopify target and is not a safe replacement without explicit merchant/provider confirmation.
- No safe tenant Integration ID remediation exists from the returned API data.
- Unified Checkout cannot open until Paymob exposes or accepts a UIG/EGP Integration ID under the same MID 645875 API credential context.
- Localhost webhook URLs cannot prove Paymob webhook finality without public HTTPS ingress.

## 22. Final Certification

Certification: BLOCKED

Reason:

- API Key authentication succeeds.
- Integration discovery succeeds.
- Same account MID is proven as 645875.
- Integration ID 3153090 is not API-visible.
- Intention with payment_methods [3153090] still fails with Paymob HTTP 404.
- Integration 3153058 is API-visible and Intention-compatible, but provider metadata identifies it as VPC/MIGS-style, not UIG/online_new/Non-Shopify.
- No same-account UIG/EGP/online_new/Non-Shopify candidate was discovered.
- Unified Checkout cannot open for the configured 3153090 integration because no client_secret exists for that integration.

Required external remediation:

Ask Paymob support or the Dashboard owner to reconcile why TEST MID 645875 shows Integration ID 3153090 as UIG/EGP/online_new/Non-Shopify while the official API integration list for the same MID returns only Integration ID 3153058 VPC/EGP and Intention rejects 3153090.

Do not switch SHOPIXY to 3153058 unless Paymob proves it is the intended Unified Checkout UIG integration. Current evidence says it is VPC, not UIG.
