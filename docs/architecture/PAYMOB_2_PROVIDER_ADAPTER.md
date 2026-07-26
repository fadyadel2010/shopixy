# PAYMOB-2 Provider Adapter Certification

Date: 2026-07-13

Scope: canonical Paymob provider adapter translation only

Excluded: webhook, callback, runtime bridge, persistence, SQL mutation, controllers, frontend, settlement, refund, capture, void, replay, and Payment Runtime changes

## Executive Summary

PAYMOB-2 implements a provider-local adapter that translates canonical `PaymentAction` input into official Paymob HTTPS requests, executes the official Paymob API through the registered `HttpClient`, and translates the provider response into canonical `ProviderEvidence`.

The adapter is not a lifecycle authority. It does not execute the Payment Runtime, does not mutate SQL, does not write payment/order status, and does not introduce Paymob concepts into `PointOfSale.Core.Payments.Runtime`.

## Adapter Architecture

Certified flow:

```text
PaymentAction
  -> Paymob auth request
  -> Paymob order registration request
  -> Paymob payment key request
  -> official Paymob HTTPS API
  -> Paymob response DTOs
  -> ProviderEvidence
```

Provider-specific concepts remain inside `PointOfSale.API.Services.Payments.Paymob`.

## Request Mapping

Canonical metadata maps to official Paymob request fields:

| Canonical input | Paymob field |
|---|---|
| `Amount` | `amount_cents` |
| `Currency` | `currency` |
| canonical runtime identity or `MerchantOrderId` | `merchant_order_id` |
| configured API key | `api_key` |
| configured integration id | `integration_id` |
| customer billing metadata | `billing_data` |
| action reference or item name | order `items[].name` |
| optional expiration | `expiration` |
| configured iframe id | iframe URL preparation |

The adapter maps only provider request fields. It does not make Runtime decisions.

## Response Mapping

Paymob order/payment-key responses map into `ProviderEvidence`:

| Provider response | Canonical evidence |
|---|---|
| order id | `EvidenceReference` / provider reference |
| payment key token | transaction reference metadata |
| payment-key creation | external status metadata |
| iframe URL | metadata only |
| action correlation | canonical correlation |
| normalized request/response facts | evidence hash and metadata |

Only canonical `ProviderEvidence` crosses the adapter boundary.

## Idempotency

The adapter derives request identity from canonical runtime identity:

```text
session id + attempt id + action id + action type + action reference + correlation id
```

If a canonical `MerchantOrderId` is supplied, it is treated as the provider-facing idempotency value. Otherwise the adapter builds a deterministic Paymob `merchant_order_id` from canonical action identity. No provider-specific Runtime identity is introduced.

## Error Translation

Paymob HTTP, network, serialization, request-validation, and configuration failures translate to `CanonicalProviderError`.

Preserved diagnostics include:

- canonical error category;
- provider code or HTTP status;
- provider message when available;
- provider request id when available;
- bounded provider diagnostics in metadata.

Provider exceptions do not escape the adapter boundary.

## Architecture Rules

1. Paymob adapter must not modify `PointOfSale.Core.Payments.Runtime`.
2. Paymob adapter must not execute `PaymentRuntimeOrchestrator`.
3. Paymob adapter must not create decisions, transitions, transactions, or settlements.
4. Paymob adapter must not write SQL or mutate payment lifecycle state.
5. Paymob adapter must not implement callbacks or webhooks in PAYMOB-2.
6. Paymob concepts such as order registration, payment key, iframe id, HMAC, auth token, and redirect URL stay in the Paymob adapter layer.
7. The only runtime-facing output is canonical `ProviderEvidence`.

## Stripe Delta

Stripe remains the reference architecture, but Paymob differs at the provider protocol layer:

| Area | Stripe | Paymob |
|---|---|---|
| Authentication | SDK/API key credential model | auth-token request using API key |
| Primary create call | PaymentIntent create | order registration followed by payment key creation |
| Customer handoff | client secret | iframe/redirect URL prepared from iframe id and payment key |
| Idempotency transport | Stripe idempotency key header | deterministic `merchant_order_id` |
| Provider reference | PaymentIntent id | Paymob order id |
| Transaction reference | PaymentIntent/client-secret flow facts | payment key token as handoff reference |
| HMAC | webhook signature model | Paymob callback/webhook HMAC model, excluded from PAYMOB-2 |

These are provider differences only. They do not redesign the architecture.

## Certification Decision

PAYMOB-2 is certified as a provider adapter translation implementation for source-level request mapping, response mapping, deterministic idempotency, error translation, official HTTP dependency usage, provider isolation, and Runtime independence.

Operational production validation and runtime/lifecycle integration are separate future phases.

## Success Answers

| Question | Answer |
|---|---|
| Provider Adapter implemented? | Yes. |
| Canonical request mapping certified? | Yes. |
| Canonical response mapping certified? | Yes. |
| Idempotency certified? | Yes. |
| Error translation certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| ProviderEvidence translation certified? | Yes. |
| Production adapter certified? | Yes, for source-level translation only. |
| Recommend next phase? | Yes: Paymob callback/webhook evidence adaptation and runtime integration certification, without changing the frozen Runtime. |

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.
