# FAWRY-5 Advanced Operations Adapter Certification

Date: 2026-07-15  
Scope: Fawry Advanced Operations Adapter only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-5 implements only the Fawry Advanced Operations Adapter.

The adapter translates canonical advanced `PaymentAction` metadata into official Fawry HTTPS requests, calls the official Fawry endpoints, translates official responses into canonical `ProviderEvidence`, and translates provider failures into `CanonicalProviderError` plus rejected `ProviderEvidence`.

It does not implement Runtime integration, application activation, lifecycle authority, persistence, SQL, replay, checkpointing, credential lookup, configuration lookup, controllers, frontend behavior, business rules, or provider protocol changes.

## Official API Discovery

Official source reviewed: FawryPay Developers Guide at `https://developer.fawrystaging.com/`, including Server Side APIs, Refund API, Cancel Unpaid Order, Get Payment Status V2, and Authorize and Capture Payments.

## Supported Operations

| Canonical operation | Official Fawry API | Support decision |
|---|---|---|
| Refund | `POST ECommerceWeb/Fawry/payments/refund` | Supported. |
| Partial refund | `POST ECommerceWeb/Fawry/payments/refund` with `refundAmount` | Supported. |
| Void | `POST ECommerceWeb/api/payment/cancel` | Supported only as cancel authorization by `merchantRefNum`. |
| Capture | `POST ECommerceWeb/api/payment/capture` | Supported for authorized payments. `captureAmount` is optional for full capture. |
| Transaction inquiry | `GET ECommerceWeb/Fawry/payments/status/v2` | Supported by merchant reference number. |

## Unsupported Operations

| Operation | Decision |
|---|---|
| Settlement inquiry | Not supported. No official settlement inquiry API was discovered in the reviewed Fawry server API surface. |
| Operation status | Not supported. No separate operation-status API was discovered; payment status inquiry is not generalized into operation-status emulation. |
| Cancel unpaid order | Officially supported by Fawry at `POST ECommerceWeb/api/orders/cancel-unpaid-order`, but not mapped to canonical refund/capture/void semantics in this adapter. It remains documented capability-only until a separate canonical command is certified. |

Unsupported operations return `CanonicalProviderError.NotSupported` and rejected `ProviderEvidence`. The adapter does not emulate unsupported provider behavior.

## Capability Matrix

| Operation | Supported | Official operation | Notes |
|---|---:|---|---|
| Refund | Yes | `POST ECommerceWeb/Fawry/payments/refund` | Requires paid captured order. |
| Partial refund | Yes | `POST ECommerceWeb/Fawry/payments/refund` | Uses `refundAmount`. |
| Void | Yes | `POST ECommerceWeb/api/payment/cancel` | Cancel authorization only. |
| Capture | Yes | `POST ECommerceWeb/api/payment/capture` | Authorized payments only. |
| Transaction inquiry | Yes | `GET ECommerceWeb/Fawry/payments/status/v2` | Pulls payment/order status evidence. |
| Settlement inquiry | No | None discovered | Rejected as not supported. |
| Operation status | No | None discovered | Rejected as not supported. |
| Cancel unpaid order | No for canonical advanced adapter | `POST ECommerceWeb/api/orders/cancel-unpaid-order` | Official provider capability documented, not canonical advanced-operation mapping. |

## Canonical Request Mapping

| Canonical metadata | Fawry request field |
|---|---|
| `ProviderReference` | `referenceNumber`, `merchantRefNum`, or `merchantRefNumber` depending on operation. |
| `Amount` | `refundAmount` or `captureAmount`, invariant two-decimal format. |
| `Reason` | Refund `reason`, optional. |
| Explicit `FawryPaymentCredentials.MerchantCode` | `merchantCode` or `merchantAccount`. |
| Explicit `FawryPaymentCredentials.SecureKey` | SHA-256 signature source only; never emitted. |

## Signature Mapping

| Operation | Signature material |
|---|---|
| Refund / partial refund | `merchantCode + referenceNumber + refundAmount(two decimals) + reason(if exists) + secureKey` |
| Capture | `merchantRefNum + captureAmount(if exists, two decimals) + merchantCode + secureKey` |
| Void / cancel authorization | `merchantRefNum + merchantCode + secureKey` |
| Transaction inquiry | `merchantCode + merchantRefNumber + secureKey` |

## Response And Evidence Mapping

Official Fawry response fields are translated into canonical `ProviderEvidence`:

| Fawry field | Evidence mapping |
|---|---|
| `fawryRefNumber`, `referenceNumber`, `paymentReferenceNumber`, `paymentRefrenceNumber`, `merchantRefNum`, `merchantRefNumber` | Provider evidence reference, in precedence order. |
| `orderStatus` / `paymentStatus` | Provider status metadata and canonical-status metadata. |
| `paymentAmount`, `orderAmount`, `captureAmount` | Amount metadata. |
| `currencyCode` | Currency metadata. |
| `paymentMethod` | Payment method metadata. |
| `statusCode`, `statusDescription`, `code`, `description` | Provider diagnostic metadata only. |

Provider evidence remains candidate evidence only. It is not local finality and does not mutate SQL.

## Failure Flow

```text
HTTP failure / transport failure / timeout / malformed response / provider rejection
  -> CanonicalProviderError
  -> ProviderEvidence.Status = Rejected
  -> FawryAdvancedOperationResult.Succeeded = false
```

Provider failures are redacted. Merchant secrets, secure keys, request signatures, API keys, and authentication material are not logged or copied into evidence metadata.

## Architecture Preservation

| Boundary | FAWRY-5 decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime Bridge | Unchanged. |
| Runtime execution | Not invoked. |
| Application Services | Not modified. |
| Controllers / frontend | Not modified. |
| Credential lookup / configuration lookup | Not implemented. Credentials are explicit method input. |
| SQL / persistence / replay / checkpoint | Not implemented. |
| Lifecycle writer | Not referenced or invoked. |
| Business rules | Not implemented. |
| CommerceHub | Not modified. |

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryAdvancedOperationsAdapterCertificationTests.cs` cover refund mapping, partial refund mapping, void mapping, capture mapping, transaction inquiry mapping, capability matrix, rejected ProviderEvidence, CanonicalProviderError mapping, serialization, and architecture isolation.

## Certification Decision

| Question | Decision |
|---|---|
| Advanced Operations Adapter implemented? | Yes. |
| Supported operations certified? | Yes for refund, partial refund, void/cancel authorization, capture, and transaction inquiry. |
| Capability matrix certified? | Yes. Unsupported operations fail closed. |
| Canonical request mapping certified? | Yes. |
| Canonical response mapping certified? | Yes. |
| ProviderEvidence translation certified? | Yes, evidence-only. |
| CanonicalProviderError certified? | Yes, including `NotSupported`. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Advanced Operations Adapter certified? | Yes for translation-only scope after focused tests pass. |
| Recommend next phase? | Yes. Certify application-contract activation only if advanced operations are to become active commands; no Runtime change is authorized. |

No `.ai-context` file was modified because no new permanent cross-provider architectural truth was discovered.

## Explicit Exclusions

FAWRY-5 does not certify or implement active application commands, Runtime integration, webhook integration, controllers, frontend behavior, SQL persistence, replay, checkpoints, credential resolution, tenant configuration lookup, lifecycle mutation, business policy, live Sandbox validation, deployment readiness, settlement inquiry, or provider-operation-status emulation.
