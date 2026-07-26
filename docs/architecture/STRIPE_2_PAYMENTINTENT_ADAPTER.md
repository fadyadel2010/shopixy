# STRIPE-2 PaymentIntent Adapter

Date: 2026-07-11

Scope: Canonical Stripe PaymentIntent adapter only.

Status: CERTIFIED - ADAPTER ONLY

## Adapter Architecture

`StripePaymentIntentAdapter` adapts canonical `PaymentAction` input into official Stripe `PaymentIntentCreateOptions`, calls the official Stripe SDK through `StripePaymentIntentSdkClient`, and translates the Stripe `PaymentIntent` response into canonical `ProviderEvidence`.

The adapter lives outside `PointOfSale.Core.Payments.Runtime`. The frozen Runtime remains provider-neutral and contains no Stripe SDK types, Stripe statuses, Stripe references, provider branches, persistence, policy evaluation, settlement execution, or API calls.

No Checkout Session, webhook processing, refund, capture, Customer Portal, Payment Runtime activation, policy evaluation, persistence, accounting, settlement, or lifecycle mutation was implemented.

## Request Mapping

Canonical fields are read from `PaymentAction` and `PaymentAction.Metadata`.

| Canonical field | Stripe PaymentIntent field |
|---|---|
| `Amount` | `PaymentIntentCreateOptions.Amount`, converted to Stripe minor units |
| `Currency` | `PaymentIntentCreateOptions.Currency`, lower-case ISO code |
| `CorrelationId` | Stripe metadata `correlation_id` |
| `Description` | `PaymentIntentCreateOptions.Description` |
| canonical metadata | Stripe metadata with `canonical_` prefix for non-adapter fields |
| `CustomerReference` | `PaymentIntentCreateOptions.Customer` |
| `PaymentMethodTypes` | `PaymentIntentCreateOptions.PaymentMethodTypes` |
| `CaptureMode` | `PaymentIntentCreateOptions.CaptureMethod` |
| `IdempotencyKey` | `RequestOptions.IdempotencyKey` |
| `StatementDescriptor` | `PaymentIntentCreateOptions.StatementDescriptor` |
| `ReceiptEmail` | `PaymentIntentCreateOptions.ReceiptEmail` |

## Response Mapping

Stripe `PaymentIntent` is translated to `ProviderEvidence` only.

| Stripe response | Canonical evidence |
|---|---|
| `PaymentIntent.Id` | `EvidenceReference`, `ProviderPayloadReference`, metadata `provider_reference` |
| `PaymentIntent.Status` | metadata `provider_status` |
| deterministic evidence id/hash | `EvidenceId`, `EvidenceHash` |
| `PaymentIntent.Created` | `EvidenceTimestamp` |
| `PaymentAction.CorrelationId` | `CorrelationId` and metadata `correlation_id` |
| `PaymentIntent.Metadata` | `EvidenceMetadata` with `stripe_metadata_` prefix |

## Idempotency

`StripeIdempotencyKey` is deterministic and replay-safe.

If the canonical action supplies `IdempotencyKey`, it is used after trimming to Stripe's maximum length. Otherwise the adapter derives a stable key from the action session id, attempt id, action id, action type, action reference, and correlation id. No random GUIDs or wall-clock values participate.

## Error Translation

`StripeException` is caught inside the adapter boundary and translated into `CanonicalProviderError`.

Mapped error categories include validation, authentication, authorization, rate limiting, provider unavailable, provider rejected, configuration, and unknown provider error. Stripe exceptions do not escape the adapter boundary.

## Architecture Boundaries

Certified boundaries:

- Adapter does not mutate Runtime.
- Adapter does not create `PaymentDecision`.
- Adapter does not create `PaymentTransaction`.
- Adapter does not create `PaymentSettlement`.
- Adapter returns canonical `ProviderEvidence` on success.
- Adapter returns canonical provider errors on failure.
- Runtime contains no Stripe references.
- Stripe adapts to Runtime; Runtime never adapts to Stripe.

## Certification Decision

STRIPE-2 is certified for PaymentIntent adapter translation only.

It is not a live Stripe rollout certification. Runtime activation, persistence integration, webhooks, refunds, capture, Checkout Sessions, Customer Portal, settlement, accounting, operational alerting, and policy execution remain separate phases.

Recommended next phase: `STRIPE-3 PaymentIntent Runtime Integration Boundary`, focused on connecting certified adapter evidence to the frozen runtime without changing runtime authority.
