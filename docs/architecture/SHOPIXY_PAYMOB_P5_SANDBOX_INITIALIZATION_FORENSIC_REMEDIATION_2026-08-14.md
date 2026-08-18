# SHOPIXY Paymob P5 Sandbox Initialization Forensic Remediation

Date: 2026-08-14
Project: SHOPIXY / PointOfSale
Scope: Paymob Sandbox Intention initialization, Unified Checkout handoff, and payment state semantics.

## 1. Executive Summary

The observed storefront order did not reach Paymob. The first real failure for order `3d58ccb9-c8c3-4278-9a39-e34de38663ef` was generated inside the Paymob adapter path before any provider HTTP request was emitted.

FIRST REAL FAILURE:

`PaymobProviderCheckpointStore.LoadAsync` failed because the local seeded `PaymentIntent` had no Paymob routing metadata. The persisted failure was:

```text
FailureCode: PAYMOB_INTENT_CREATE_FAILED
FailureReason: Paymob routing metadata is missing or invalid.
```

After repairing the source defect, a controlled provider comparison using the same current decrypted tenant configuration reached Paymob Intention API and returned:

```text
HTTP 404
{"detail":"Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"}
```

Therefore the source defect that prevented provider I/O is fixed, but the live Paymob sandbox flow remains blocked by provider/account/configuration evidence: Integration ID `3153090` is not accepted by Paymob for the current Secret Key/account/region/mode.

Final certification: BLOCKED.

## 2. Current Runtime Symptom

Checkout request:

```text
POST /api/storefront/checkout
OrderId: 3d58ccb9-c8c3-4278-9a39-e34de38663ef
OrderNumber: ORD-83B3-2026-01014
PaymentMethod: PAYMOB
Total: 410.00 EGP
```

Canonical status response:

```json
{
  "status": "FAILED",
  "orderStatus": "Pending",
  "paymentStatus": "Pending",
  "paymentIntentStatus": "Failed",
  "paymentMethod": "PAYMOB",
  "totalAmount": 410.00,
  "currency": "EGP"
}
```

The browser went to `/checkout/pending` because no Paymob checkout handoff URL existed. That was a symptom, not the cause.

## 3. Exact Provider Request

For the original failing order, no Paymob provider request was emitted. The adapter failed before HTTP I/O because the seeded local payment intent lacked routing metadata.

Controlled comparison request after source repair, using the current decrypted tenant configuration:

```text
Method: POST
Endpoint: https://accept.paymob.com/v1/intention/
Authorization: Token <redacted>
Public key configured: YES
Secret key configured: YES
HMAC secret configured: YES
Secret decryptable: YES
MID: 645875
Integration ID: 3153090
Amount: 41000
Currency: EGP
Notification URL: http://localhost:5010/api/webhooks/paymob
Redirection URL: http://localhost:5010/api/webhooks/paymob/callback
Latency: 924 ms
Provider request ID: <null>
```

Request structure, excluding secrets:

```json
{
  "amount": 41000,
  "currency": "EGP",
  "payment_methods": [3153090],
  "items": [
    {
      "name": "Order ORD-83B3-2026-01014",
      "amount": 41000,
      "description": "3d58ccb9-c8c3-4278-9a39-e34de38663ef",
      "quantity": 1
    }
  ],
  "billing_data": {
    "apartment": "NA",
    "email": "1@g.com",
    "floor": "NA",
    "first_name": "xxx",
    "last_name": "User",
    "street": "1, Building 1",
    "building": "NA",
    "phone_number": "11111111",
    "shipping_method": "NA",
    "postal_code": "NA",
    "city": "1",
    "country": "EGY",
    "state": "1"
  },
  "customer": {
    "first_name": "xxx",
    "last_name": "User",
    "email": "1@g.com"
  },
  "extras": {
    "internal_reference": "3d58ccb9-c8c3-4278-9a39-e34de38663ef"
  },
  "special_reference": "3d58ccb9-c8c3-4278-9a39-e34de38663ef",
  "expiration": 3600,
  "notification_url": "http://localhost:5010/api/webhooks/paymob",
  "redirection_url": "http://localhost:5010/api/webhooks/paymob/callback"
}
```

## 4. Exact Provider Response

Controlled provider response:

```text
HTTP status: 404
Provider request ID: <null>
Body:
{"detail":"Integration ID/Name does not exist in our system . You can find the list of Integration ID'/Names from Merchant Dashboard under Developers -> Payment Integrations Tab"}
```

No `client_secret`, intention ID, or checkout URL was returned.

## 5. Credential Consistency

Observed through the secure local configuration path without printing secrets:

```text
Public key configured: YES
Secret key configured: YES
HMAC secret configured: YES
Secret decryptable: YES
MID configured: 645875
Credential/account consistency: FAIL/UNPROVEN
```

The Secret Key is syntactically usable enough to reach the Paymob Intention endpoint, but the current Secret Key/account/region/mode does not accept Integration ID `3153090`. This prevents certification that the Public Key, Secret Key, MID, and Integration ID are one coherent sandbox set.

## 6. Integration 3153090 Validation

Validation result: FAIL for the current decrypted tenant credential set.

Provider evidence:

```text
HTTP 404: Integration ID/Name does not exist in our system.
```

This is direct Paymob evidence. It proves dashboard visibility alone is not enough to certify `3153090` for this Intention API credential/account/environment. The correct integration ID cannot be inferred safely from source. It must be corrected in Paymob dashboard or tenant configuration using an integration accepted by the same Secret Key/account/region/mode.

## 7. Intention API Contract

Official Paymob documentation and the official Paymob wizard confirm the intended modern flow:

- Backend creates an Intention through `POST https://accept.paymob.com/v1/intention/`.
- Authentication uses `Authorization: Token <secret_key>`.
- `payment_methods` contains integration IDs.
- `amount` is sent in smallest currency units.
- Successful Intention creation returns a `client_secret`.
- Egypt Unified Checkout uses `https://accept.paymob.com/unifiedcheckout/?publicKey=<public_key>&clientSecret=<client_secret>`.
- Webhooks/callbacks require HMAC verification and are the source of payment finality.

References:

- https://developers.paymob.com/paymob-docs/integration-paths/apis
- https://developers.paymob.com/paymob-docs/developers/checkout-experiences
- https://wizard.paymob.com/
- https://raw.githubusercontent.com/api-evangelist/paymob/refs/heads/main/openapi/paymob-intentions-api-openapi.yml

## 8. Amount/Currency

Runtime order amount:

```text
410.00 EGP
```

Paymob request amount:

```text
41000
```

The conversion is correct for smallest-unit Intention payloads. No evidence of decimal amount, string amount, currency mismatch, or double conversion was found.

## 9. Callback/Webhook

Application/configured URLs in the controlled request:

```text
notification_url: http://localhost:5010/api/webhooks/paymob
redirection_url: http://localhost:5010/api/webhooks/paymob/callback
```

The URLs are absolute and point to Shopixy endpoints. They are not public HTTPS URLs, so they are acceptable only for local source/runtime testing. Real Paymob webhook delivery and payment finality cannot be certified until a public HTTPS ingress is available.

## 10. Unified Checkout Handoff

Defect fixed:

- The source previously allowed/constructed the stale host `https://eg.checkout.paymob.com/`.
- The official Egypt Unified Checkout URL is now enforced as `https://accept.paymob.com/unifiedcheckout/`.

Changed symbols:

- `PaymobRedirectFactory.EgyptUnifiedCheckoutUri`
- `PaymobRuntimeApplicationService` Paymob checkout URL validation
- Storefront `paymentSessionAuthority.ts` Paymob URL allowlist
- `PaymobPaymentForm.tsx` Paymob URL guard

Because provider Intention creation currently returns HTTP 404, no checkout handoff URL can be produced in live sandbox.

## 11. Frontend Navigation

The frontend does not open Paymob because the backend has no successful Intention result and no valid checkout URL. The current `/checkout/pending` navigation is a fallback after backend initialization failure, not proof of a Paymob processing state.

No frontend hardcoded Paymob URL was added. Redirect remains dependent on backend payment session authority.

## 12. Payment State Semantics

Observed persisted combination:

```text
OrderStatus: Pending
PaymentStatus: Pending
PaymentIntentStatus: Failed
```

This is legal for "order created but provider initialization failed before customer payment began." It must not be treated as a successful processing payment. The order is still pending, while the specific Paymob initialization attempt is failed.

## 13. PaymentIntent Persistence

Runtime forensic record:

```text
PaymentIntentId: fb9ad1b7-fc83-4af6-a891-b01207392b1b
Provider: Paymob
Status: Failed
ProviderIntentId: <null>
PaymentReference: PAY-20260814135152-3D58CCB9
FailureCode: PAYMOB_INTENT_CREATE_FAILED
FailureReason: Paymob routing metadata is missing or invalid.
ProviderMetadata: <null>
```

No client secret or provider secret was persisted in plaintext in this failed runtime record.

## 14. Root Cause

### Defect 1: Seeded Paymob payment intent missing routing metadata

Symptom:

`paymentIntentStatus = Failed` and storefront redirects to `/checkout/pending`.

First failing operation:

`PaymobProviderCheckpointStore.LoadAsync` rejects the existing pending local payment intent before provider I/O.

Source file/symbol:

`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`, existing pending-intent reuse path inside `CreatePaymentIntentAsync`.

Exact evidence:

```text
ProviderMetadata: <null>
FailureCode: PAYMOB_INTENT_CREATE_FAILED
FailureReason: Paymob routing metadata is missing or invalid.
```

Root cause:

`CheckoutService` created a transactional local pending payment intent before provider execution, but the Paymob adapter path requires routing metadata on any reused Paymob intent. The seeded intent did not have that metadata.

Impact:

Paymob Intention creation never ran for the live checkout.

Implementation:

The Paymob service now repairs a seeded Paymob intent that has no provider intent ID and missing/invalid routing metadata before adapter execution. It rebuilds routing metadata from tenant ID, order ID, storefront base URL, and a null protected Paymob session.

Regression test:

`P5SeededCheckoutIntent_MustRepairRoutingMetadataBeforeProviderExecution` was added as a source guard.

Runtime evidence:

The original failed payment intent shows no provider reference and no provider metadata, proving adapter failure before HTTP I/O.

### Defect 2: Unified Checkout URL host/path stale

Symptom:

Even if Intention succeeds, backend/frontend validation could reject or construct the wrong checkout URL.

First failing operation:

Checkout URL construction/validation after a successful Intention response.

Source file/symbol:

- `PaymobRedirectFactory.EgyptUnifiedCheckoutUri`
- `PaymobRuntimeApplicationService`
- `paymentSessionAuthority.ts`
- `PaymobPaymentForm.tsx`

Exact provider/document evidence:

Current Paymob Egypt Unified Checkout uses `https://accept.paymob.com/unifiedcheckout/` with `publicKey` and `clientSecret`.

Root cause:

The source still referenced `eg.checkout.paymob.com`, which does not match the current official Egypt Unified Checkout URL.

Impact:

Successful Intention responses could fail handoff validation or navigate to a stale checkout endpoint.

Implementation:

Backend construction and validation, plus storefront URL guards, now require `https://accept.paymob.com/unifiedcheckout/`.

Regression test:

Existing Paymob adapter/handoff certification tests were updated to assert the current Unified Checkout URL.

Runtime evidence:

Live provider initialization is still blocked before checkout handoff by Paymob HTTP 404, so this fix is source-verified but not live-checkout verified.

### Defect 3: Intention request missing explicit customer object

Symptom:

Potential mismatch with current Intention contract examples and provider expectations.

First failing operation:

Request serialization for Intention creation.

Source file/symbol:

`PaymobPaymentRequestFactory.Create` and CommerceHub Paymob request mapping.

Exact provider/document evidence:

Current Intention examples include both `billing_data` and `customer` fields.

Root cause:

The request had billing data but did not include the explicit `customer` object.

Impact:

Provider validation could reject or degrade customer association.

Implementation:

Added `PaymobIntentionCustomer` and populated it from the same sanitized customer identity as billing data.

Regression test:

`PaymobPaymentAdapterCertificationTests.RequestMapping_MustTranslateCanonicalActionToOfficialPaymobRequests` now asserts `Customer.Email`.

Runtime evidence:

The controlled provider request included the `customer` object.

### Defect 4: Integration/account mismatch remains

Symptom:

Paymob Intention creation returns HTTP 404.

First failing operation:

Provider Intention creation.

Source file/symbol:

Tenant Paymob configuration value for Integration ID `3153090`, resolved through `PaymobTenantConfigurationResolver`.

Exact provider evidence:

```text
HTTP 404
Integration ID/Name does not exist in our system.
```

Root cause:

The current Secret Key/account/region/mode does not recognize Integration ID `3153090`.

Impact:

No Intention, no client secret, no Unified Checkout URL, no sandbox payment, no callback/webhook finality.

Implementation:

No source-side replacement integration was fabricated. Tenant configuration must be corrected with a Paymob integration accepted by the current sandbox Secret Key/account/region/mode.

Regression test:

Configuration validation should include a live provider Intention probe or official integration lookup where Paymob access is available.

Runtime evidence:

Controlled provider comparison returned the exact 404 above.

## 15. Fixes

Implemented source changes:

- Repair missing Paymob routing metadata on seeded pending payment intents before adapter execution.
- Preserve failed initialization as failed, without forcing frontend redirect.
- Build current Egypt Unified Checkout URL with `accept.paymob.com/unifiedcheckout`.
- Enforce the same official Unified Checkout host/path in backend runtime validation.
- Enforce the same official Unified Checkout host/path in storefront handoff guards.
- Add explicit `customer` object to Paymob Intention payload.
- Add/update source guards and certification tests for the P5 failure mode and URL contract.

No secrets were added to source, logs, tests, snapshots, or frontend payloads.

## 16. Regression Tests

Added or updated:

- Paymob seeded checkout intent routing metadata source guard.
- Paymob adapter request mapping for `customer`.
- Paymob Unified Checkout URL expectations.
- Storefront Paymob URL validation source guard.

Focused test execution with redirected output was attempted, but the first run failed before assertions because the temp output path prevented solution-root discovery. A second run with `POINT_OF_SALE_SOLUTION_ROOT` set failed on file-copy access/lock errors in the temp output folder. These failures are test harness/output placement issues, not Paymob assertion failures.

## 17. Live Sandbox Drill

Completed:

1. Live failed order inspected.
2. First adapter failure identified from persisted runtime evidence.
3. Source repaired.
4. Controlled Paymob Intention request sent with the same tenant credentials and same order economics.
5. Provider response captured safely.

Blocked:

1. Intention creation still returns HTTP 404 for Integration ID `3153090`.
2. No `client_secret` was returned.
3. Browser cannot reach Unified Checkout.
4. Sandbox card payment cannot be attempted.
5. Callback/webhook finality cannot be proven.

## 18. Security

Verified:

- Secret Key was used server-side only.
- HMAC secret was not sent to browser.
- Secret values were not printed in this report.
- Client secret is only eligible for frontend exposure after successful Intention creation, per Paymob checkout requirement.
- Tenant/order correlation remains enforced by Paymob routing metadata.
- HMAC remains required for webhook finality.

## 19. Cross-Provider Regression

The Paymob changes are scoped to Paymob adapter/service/handoff validation. No COD or Manual Wallet finality logic was changed for this P5 remediation.

Cross-provider live drills were not completed in this pass because Paymob initialization remains blocked by provider configuration and the live API binary was locked during local test execution.

## 20. Build/EF

Executed:

```text
git status --short
git diff --stat
git diff --check
dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -p:UseAppHost=false -p:OutDir=C:\tmp\shopixy-p5-api-out\ -v:minimal
dotnet build PointOfSale.sln --no-restore -p:UseAppHost=false -p:OutDir=.codex-temp\p5-sln-out\ -v:minimal
npm run build
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build
```

Results:

- `git diff --check`: clean except existing line-ending warnings.
- API build: succeeded with 0 warnings and 0 errors using temp output.
- Solution build: succeeded with existing warnings and 0 errors using temp output.
- Storefront build: succeeded with existing Vite chunk/import warnings.
- EF: no pending model changes.
- Focused Paymob test slice: blocked by temp-output test harness issues described in Section 16.

## 21. Remaining Limitations

- Paymob Integration ID `3153090` is not accepted by the current decrypted sandbox Secret Key/account/region/mode.
- The correct integration ID cannot be safely inferred from source.
- Local callback/webhook URLs are not public HTTPS, so provider-delivered webhook finality cannot be certified from this machine.
- Existing live API process locks default build/test outputs, requiring temp output directories for compilation.

## 22. Final Certification

BLOCKED

Reason:

The source defect that prevented Paymob provider I/O is fixed, and the current Intention contract/handoff source path has been remediated. However, live sandbox Intention creation still fails with a real Paymob provider error:

```text
HTTP 404: Integration ID/Name does not exist in our system.
```

Until the Paymob sandbox tenant configuration uses an integration ID accepted by the same Secret Key/account/region/mode, the required final flow cannot complete:

```text
Intention success -> client_secret -> Unified Checkout -> sandbox payment -> webhook/HMAC -> payment finality
```
