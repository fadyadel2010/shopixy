# FAWRY-2 Provider Adapter Certification

Date: 2026-07-14  
Scope: Fawry provider adapter only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-2 implements a translation-only provider adapter for the official Fawry payment initialization flow.

It does not implement lifecycle ownership, Runtime bridge integration, callback/webhook handling, advanced operations, persistence, SQL, configuration lookup, credential resolution, controllers, frontend behavior, or business rules.

## Execution Diagram

```text
Canonical PaymentAction
  -> IFawryPaymentAdapter.InitializePaymentAsync
  -> FawryPaymentRequestFactory
  -> official Fawry charge request
  -> System.Net.Http.HttpClient POST ECommerceWeb/Fawry/payments/charge
  -> official Fawry charge response
  -> FawryEvidenceFactory
  -> canonical ProviderEvidence
```

Provider failure path:

```text
HTTP / transport / malformed response / provider rejection
  -> FawryProviderErrorTranslator
  -> CanonicalProviderError
```

## Mapping Matrix

| Canonical input | Official Fawry request field | Rule |
|---|---|---|
| `PaymentAction.Metadata[Amount]` | `amount` | Invariant decimal formatted to two decimals. |
| `PaymentAction.Metadata[Currency]` | `currencyCode` | Uppercase ISO-style currency code. |
| `PaymentAction.Metadata[MerchantReference]` or action lineage | `merchantRefNum` | Supplied value or deterministic projection from session, attempt, action, type, action reference, and correlation. |
| One-operation credential handoff `MerchantCode` | `merchantCode` | Consumed from adapter method argument only. No lookup. |
| `PaymentAction.Metadata[CustomerReference]` | `customerProfileId` | Optional customer reference. |
| `PaymentAction.Metadata[PaymentMethod]` | `paymentMethod` | Defaults to `PAYATFAWRY`; maps reference aliases to the official method. |
| `PaymentAction.Metadata[ExpirationUtc]` | `paymentExpiry` | ISO UTC timestamp converted to Unix milliseconds. |
| `PaymentAction.Metadata[RedirectUrl]` | `returnUrl` | Optional redirect/payment return URL. |
| `PaymentAction.Metadata[OrderWebHookUrl]` | `orderWebHookUrl` | Optional provider order notification URL. |
| `PaymentAction.Metadata[CustomerMobile]` | `customerMobile` | Optional. |
| `PaymentAction.Metadata[CustomerEmail]` | `customerEmail` | Optional. |
| `PaymentAction.Metadata[Description]` | `description` | Optional. |
| Derived charge item | `chargeItems` | Single item representing the canonical payment amount. |
| SHA-256 signature material | `signature` | `merchantCode + merchantRefNum + customerProfileId(if exists) + paymentMethod + amount(two decimals) + secureKey`. |

## ProviderEvidence Mapping

| Official Fawry response | ProviderEvidence field / metadata |
|---|---|
| `referenceNumber`, `paymentReferenceNumber`, `fawryRefNumber`, or `merchantRefNum` | `EvidenceReference` in precedence order. |
| Provider name | `EvidenceSource = fawry`. |
| `orderStatus` | Metadata `provider_status` and mapped metadata `canonical_status`. |
| `paymentAmount` / `orderAmount` | Metadata `amount`. |
| `currencyCode` | Metadata `currency`. |
| `paymentMethod` | Metadata `payment_method`. |
| `paymentUrl` / `redirectUrl` | Metadata `payment_url`. |
| `expirationTime` | Metadata `expiration_time`. |
| Provider reference and canonical correlation | Deterministic evidence hash and evidence id. |

ProviderEvidence remains candidate evidence only. It is not local payment finality and does not mutate SQL.

## Status Mapping

| Fawry status | Canonical metadata status |
|---|---|
| `PAID`, `CAPTURED` | `Completed` |
| `AUTHORIZED` | `Authorized` |
| `NEW`, `UNPAID`, `PENDING` | `AwaitingCustomer` |
| `EXPIRED` | `Expired` |
| `CANCELED`, `CANCELLED` | `Cancelled` |
| `FAILED`, `DECLINED` | `Failed` |
| `REFUNDED` | `Refunded` |
| `PARTIAL_REFUNDED`, `PARTIALLY_REFUNDED` | `PartiallyRefunded` |
| Unknown | `ReconciliationRequired` |

The mapping is evidence metadata for later Runtime/application admission. The adapter does not perform lifecycle admission.

## Canonical Error Mapping

| Provider failure | CanonicalProviderError type |
|---|---|
| HTTP 401 | `Authentication` |
| HTTP 403 | `Authorization` |
| HTTP 429 | `RateLimited` |
| HTTP 400 / 422 | `Validation` |
| HTTP 5xx | `ProviderUnavailable` |
| Other non-success HTTP | `ProviderRejected` |
| Transport exception | `Network` |
| Timeout | `ProviderUnavailable` |
| Malformed response | `Validation` |
| Provider official rejection | `ProviderRejected`, `Validation`, or `Authentication` by safe provider code classification |

Provider-specific exceptions do not leave the adapter boundary.

## Architecture Preservation

| Boundary | FAWRY-2 decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime Bridge | Not implemented. |
| Webhook / callback adapter | Not implemented. |
| Advanced operations | Not implemented. |
| Credential resolution | Not implemented; credentials are explicit method input. |
| Configuration lookup | Not implemented. |
| SQL / persistence | Not implemented. |
| Logging | No adapter logging. |
| Business rules | Not implemented. |
| Controllers / frontend | Not modified. |
| CommerceHub | Not modified. |

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryProviderAdapterCertificationTests.cs` cover:

| Evidence | Coverage |
|---|---|
| Canonical request mapping | Amount, currency, merchant reference, customer reference, payment method, expiration, redirect/webhook URL, signature. |
| Canonical response mapping | Official response to `ProviderEvidence`. |
| ProviderEvidence translation | Reference, source, hash, payload reference, correlation, metadata. |
| Idempotency projection | Deterministic `merchantRefNum` projection. |
| Official endpoint usage | `HttpClient` POST to `ECommerceWeb/Fawry/payments/charge`. |
| Error translation | HTTP/provider failures to `CanonicalProviderError`. |
| Architecture isolation | No Runtime modification, SQL, configuration lookup, logging, lifecycle authority, or Stripe dependency. |

## Certification Decision

| Question | Decision |
|---|---|
| Provider Adapter implemented? | Yes. |
| Canonical request mapping certified? | Yes, source and focused tests. |
| Canonical response mapping certified? | Yes, source and focused tests. |
| ProviderEvidence translation certified? | Yes, candidate evidence only. |
| Idempotency certified? | Yes, deterministic merchant reference projection. |
| Error translation certified? | Yes, canonical provider error mapping. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Provider Adapter certified? | Yes for translation/protocol only after focused tests pass. |
| Recommend next phase? | Yes. Proceed to FAWRY-3 Runtime Bridge certification without Runtime changes. |

No `.ai-context` file was modified because no new permanent cross-provider architectural truth was discovered.
