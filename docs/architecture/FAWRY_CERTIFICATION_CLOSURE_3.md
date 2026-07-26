# FAWRY-CERTIFICATION-CLOSURE-3 — Charge Response Integrity

Date: 2026-07-15  
Finding: `FAWRY-FINAL-002` only  
Scope: Active POS Admin + Storefront PayAtFawry charge-response processing  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Preserved and unchanged

## Certification Decision

The Board finding was **valid**. Before this closure, an HTTP-success response
could become recorded `ProviderEvidence` after only loose provider-status and
reference checks. The response model omitted the official response signature
and several fields used by that signature, used the request-side
`merchantRefNum` spelling instead of the documented response-side
`merchantRefNumber`, and did not correlate the signed response amount,
merchant reference, payment method, or recognized currency/merchant extensions
with the submitted request and active merchant credentials.

A minimum bounded correction was implemented entirely inside the Fawry
Provider Adapter. No Application orchestration, Runtime Bridge, Payment Runtime,
lifecycle, business rule, controller, frontend, persistence, SQL, migration, or
entity source was changed by this closure.

## Official Documentation Evidence

Evidence was re-read from the current official FawryPay documentation on
2026-07-15:

- [Create Payment Requests Using FawryPay Reference Number](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis)

The current response table and successful response example establish:

| Concern | Official PayAtFawry response contract |
|---|---|
| Response type | `type` is `ChargeResponse`. |
| Fawry reference | `referenceNumber` is the FawryPay-issued transaction/reference number. |
| Merchant reference | `merchantRefNumber` echoes the merchant-issued request reference. |
| Amount | `orderAmount` and `paymentAmount` are two-decimal values. |
| Method and status | `paymentMethod` and `orderStatus` are response facts; the response example uses `PayAtFawry` and `PENDING`, while the field description also documents `PAID`. |
| Optional signed fields | `fawryFees`, `shippingFees`, `authNumber`, `customerMail`, and `customerMobile` participate when present. |
| Response signature | `signature` is SHA-256 over reference number (if present), merchant reference, payment amount, order amount, order status, payment method, optional fees/auth/customer fields, and the secure key, in the documented order. |
| Provider status | `statusCode` is `200` for the documented successful response. |

The official response contract does **not** define `currencyCode` or
`merchantCode` as required response fields and does not include either in the
documented response signature. This closure therefore does not manufacture a
requirement that would reject the documented response when those fields are
absent. If Fawry returns either recognized extension, the adapter requires exact
agreement with the submitted request or active merchant credentials and does
not allow the returned value to establish a different authority.

## Root Cause

The original provider-adapter work modeled a repository-tolerant response with
multiple possible reference/status aliases. It treated HTTP success, a loosely
accepted status code, and any provider-or-merchant reference as sufficient for
evidence creation. The official response-authentication contract was not
modeled, so the adapter could not reconstruct or verify the response signature.

`FawryRuntimeApplicationService` correctly delegated provider execution through
the Runtime Bridge and performed exact canonical Runtime-outcome admission, but
that could not repair unauthenticated provider facts already admitted by the
adapter. Protocol authentication and request/response correlation belong before
evidence creation in the Provider Adapter.

## Current Implementation Review

### `FawryPaymentAdapter`

Before correction, the active sequence was:

```text
HTTP success
  -> deserialize partial response
  -> loose status/reference check
  -> recorded ProviderEvidence
  -> Runtime Bridge
```

The response model did not include `signature`, official
`merchantRefNumber`, fees, shipping fees, authorization number, or signed
customer fields. `referenceNumber` could also fall back to unrelated aliases or
the merchant reference. Amount, currency, merchant identity, payment method,
and merchant reference were not compared with the request.

After correction, recorded evidence is created only after:

1. exact `ChargeResponse` type and successful `200` status validation;
2. required Fawry reference and official merchant reference validation;
3. strict two-decimal payment/order amount parsing;
4. documented `PayAtFawry` method and documented charge-status validation;
5. reconstruction of the complete documented response-signature input;
6. SHA-256 verification using a fixed-time byte comparison;
7. exact merchant-reference and payment/order-amount correlation;
8. optional returned currency and merchant-code correlation;
9. optional returned customer-field correlation;
10. rejection of missing, malformed, conflicting, or unexpected recognized values.

The official response example represents some numeric fields as JSON numbers
although the table describes them as strings/decimals. The adapter now accepts
official string or numeric JSON tokens for those fields, normalizes signed
decimal values to exactly two places, and rejects invalid precision or types.
The Fawry-issued authority is now only `referenceNumber`; merchant or legacy
reference aliases cannot substitute for it.

### `FawryRuntimeApplicationService`

No source correction was required. It continues to own orchestration and exact
Runtime-outcome admission. It receives recorded evidence only after adapter
integrity succeeds. Adapter failures remain canonical provider failures and are
mapped through the existing application error boundary.

### Rejected evidence path

The existing bridge behavior was verified and preserved:

```text
Fawry response integrity failure
  -> CanonicalProviderError
  -> rejected ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> Runtime admission failure
  -> application failure
```

No provider success can reach executable Runtime processing through the active
charge path without first passing the response-integrity validator.

## Implemented Correction

Modified in scope:

- `PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs`
- `PointOfSale.Tests/Architecture/FawryProviderAdapterCertificationTests.cs`
- `PointOfSale.Tests/Architecture/FawryCertificationClosure3Tests.cs`
- this certification record

Explicitly unchanged by this closure:

- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs`
- `PointOfSale.Core/Payments/Runtime/**`
- lifecycle services and business rules
- controllers, frontend, persistence, SQL, migrations, entities, and DTOs

No new permanent cross-provider architectural truth was discovered. The
existing Provider Adapter, canonical error, rejected evidence, Outcome
Admission, and frozen Runtime rules already govern this correction. Therefore
`.ai-context` is not modified by this closure.

## Focused Certification Tests

`FawryCertificationClosure3Tests` proves:

- a complete valid signed response is accepted;
- a signature mismatch is rejected before recorded evidence;
- a provider-signed amount mismatch is rejected;
- a provider-signed merchant-reference mismatch is rejected;
- a returned currency mismatch is rejected;
- a returned merchant-identity mismatch is rejected;
- a missing Fawry reference is rejected;
- every required official response field is fail-closed;
- malformed JSON is rejected;
- unexpected order-status and success-code values are rejected;
- integrity failure becomes `CanonicalProviderError`, rejected
  `ProviderEvidence`, and Runtime admission failure;
- protocol validation remains in the adapter and Runtime remains provider-neutral.

## Regression

Executed in this closure session:

| Command / slice | Result |
|---|---:|
| `FawryCertificationClosure3Tests` | **20 passed**, 0 failed |
| Closure-3 + affected adapter/application/Closure-2 + webhook adapter + frozen Payment Runtime | **156 passed**, 0 failed |
| Frozen Payment Runtime subset within that gate | **94 passed**, 0 failed |
| API/test compilation during the regression gate | **Passed**; repository-existing warnings remain |
| Complete Fawry aggregate after final correction | **119 passed, 7 failed, 0 skipped** |

An initial complete aggregate run exposed an over-narrowed shared webhook status
mapping during this closure. It was corrected without weakening the strict
charge-response status validator, and the affected webhook adapter test is
included in the final 156/156 green gate. The final complete aggregate contains
only the same seven pre-existing failures documented by Final Re-Certification
and Closure 2:

- four `FawryWebhookReliabilityTests` cannot seed the stale local PostgreSQL
  schema because `Orders.BillingAddressAdmissionEvidenceId` is absent;
- three `FawryPaymentAuthorityCertificationTests` assert superseded historical
  source strings and the intentionally absent permanent `PASS SF-PAY7`
  `.ai-context` marker.

Those conditions are outside `FAWRY-FINAL-002` and were not modified under this
one-finding mission. The response-integrity correction, affected Fawry slices,
webhook mapping regression, and frozen Runtime regression are green. An
unqualified whole-Fawry aggregate remains withheld for the documented unrelated
conditions.

## Final Certification Matrix

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** |
| Official response contract satisfied? | **Yes, at source for the active PayAtFawry charge-response path.** |
| Response integrity certified? | **Yes.** Recorded evidence is emitted only after structural, signature, status, and request-correlation validation. |
| Signature validation certified? | **Yes.** The complete documented field order is reconstructed and compared in fixed time. |
| Reference validation certified? | **Yes.** The official merchant reference must match the submitted reference, and the signed Fawry `referenceNumber` is mandatory and cannot fall back to another identity. |
| Amount validation certified? | **Yes.** Both signed amounts must be valid two-decimal values and equal the submitted amount. |
| Currency validation certified? | **Yes within the official contract boundary.** Currency is not an official response/signature field; absence is valid, while any returned recognized currency must exactly match the request and cannot create new authority. |
| Merchant identity certified? | **Yes within the official contract boundary.** The tenant secure-key signature plus exact merchant reference authenticates the response for the active merchant; any returned merchant code must also match. |
| Response status certified? | **Yes.** Only the documented charge response type, success code, method, and charge statuses are admitted. |
| Regression passed? | **Yes for Closure-3, affected Fawry behavior, webhook mapping, and frozen Runtime. Whole-Fawry aggregate remains withheld only for seven documented unrelated failures.** |
| Runtime preserved? | **Yes. 94/94 Runtime tests pass and Runtime source was unchanged.** |
| Architecture preserved? | **Yes. Protocol knowledge remains in the Provider Adapter; application orchestration and Runtime authority are unchanged.** |
| Recommend Closure-4? | **Yes, as a separately bounded review of `FAWRY-FINAL-003`. This closure does not implement that or any later finding.** |

## Closure Decision

`FAWRY-FINAL-002` is **CLOSED at source**. The active PayAtFawry adapter now
authenticates and correlates a successful charge response before recorded
provider evidence exists. Integrity failures fail closed through the existing
canonical error, rejected evidence, and frozen Runtime path.

This closure does not claim Sandbox validation, deployment readiness, live
provider acceptance, whole-provider software certification, or closure of any
finding other than `FAWRY-FINAL-002`.
