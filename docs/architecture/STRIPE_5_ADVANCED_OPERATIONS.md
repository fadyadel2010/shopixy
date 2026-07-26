# STRIPE-5 Advanced Operations Adapter

Date: 2026-07-12

Scope: Canonical Stripe advanced operations adapter only.

Status: CERTIFIED - ADVANCED OPERATION TRANSLATION ONLY

## Supported Operations

`StripeAdvancedOperationsAdapter` supports:

- Capture.
- Partial capture.
- Cancel.
- Refund.
- Partial refund.
- Retrieve PaymentIntent.
- Retrieve Charge.

No Checkout runtime, webhook runtime, persistence, settlement, accounting, policy evaluation, or Payment Runtime modification is implemented.

## Operation Matrix

| Canonical input | Stripe operation | Canonical output |
|---|---|---|
| `PaymentAction` with `Operation = Capture` | `PaymentIntentService.CaptureAsync` | `ProviderEvidence` |
| `PaymentAction` with `Operation = Refund` | `RefundService.CreateAsync` | `ProviderEvidence` |
| `PaymentAction` with `Operation = Cancel` | `PaymentIntentService.CancelAsync` | `ProviderEvidence` |
| `PaymentAction` with `Operation = PartialRefund` | `RefundService.CreateAsync` with amount | `ProviderEvidence` |
| `PaymentAction` with `Operation = RetrievePaymentIntent` | `PaymentIntentService.GetAsync` | `ProviderEvidence` |
| `PaymentAction` with `Operation = RetrieveCharge` | `ChargeService.GetAsync` | `ProviderEvidence` |

## Request Mapping

Canonical `PaymentAction.Metadata` is translated into official Stripe SDK request options:

| Canonical field | Stripe request field |
|---|---|
| `Amount` | `AmountToCapture` or refund `Amount`, converted to minor units |
| `Currency` | Lower-case Stripe currency for refund or amount conversion |
| `ProviderReference` | PaymentIntent id or Charge id |
| `CorrelationId` | Canonical correlation carried in Stripe metadata |
| `Metadata` | Canonical metadata prefixed and normalized |
| `Reason` | Stripe cancel/refund reason |
| `IdempotencyKey` | `RequestOptions.IdempotencyKey` |
| `OperationReference` | Evidence metadata only |

## Response Mapping

Stripe `PaymentIntent`, `Refund`, and `Charge` responses are translated to `ProviderEvidence` only.

Mapped evidence includes operation id, provider reference, operation status, deterministic evidence hash, evidence timestamp, correlation id, idempotency key, operation reference, amount, currency, and Stripe response metadata.

## Idempotency

Every operation receives deterministic idempotency:

- Supplied canonical `IdempotencyKey` is used directly.
- Otherwise the key derives from provider, operation, session id, attempt id, action id, provider reference, amount, and correlation id.
- No random value or wall-clock timestamp participates.

## Error Translation

`StripeException` is caught inside the adapter and translated to `CanonicalProviderError`.

Supported categories:

- Validation.
- Already captured.
- Already refunded.
- Invalid state.
- Provider unavailable.
- Authentication.
- Authorization.
- Rate limit.
- Unknown provider error.

Stripe exceptions do not cross the adapter boundary.

## Architecture Rules

- Adapter never creates `PaymentDecision`.
- Adapter never creates `PaymentTransaction`.
- Adapter never creates `PaymentSettlement`.
- Adapter never changes Runtime.
- Adapter returns `ProviderEvidence` only on success.
- Runtime contains no Stripe operations.
- Stripe adapts to Runtime; Runtime never adapts to Stripe.

## Operation Invariants

- Capture never changes `PaymentSession`.
- Refund never changes `PaymentSession`.
- Cancel never changes `PaymentSession`.
- Every operation returns `ProviderEvidence`.
- Runtime owns all decisions.

## Certification Decision

STRIPE-5 is certified as the canonical Stripe Advanced Operations Adapter.

It is not a live Stripe rollout certification. Runtime activation, durable persistence, policy evaluation, reconciliation storage, settlement execution, accounting, dashboards, alerts, and production operations remain separate phases.

Recommended next phase: Stripe persistence and operational reconciliation boundary, preserving the frozen provider-neutral Payment Runtime.
