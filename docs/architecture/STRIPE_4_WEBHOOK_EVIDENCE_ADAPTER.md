# STRIPE-4 Webhook Evidence Adapter

Date: 2026-07-12

Scope: Canonical Stripe webhook evidence adapter only.

Status: CERTIFIED - WEBHOOK TRANSLATION ONLY

## Webhook Architecture

`StripeWebhookEvidenceAdapter` receives a Stripe webhook payload, verifies it with the official Stripe SDK, translates the verified Stripe event into canonical `ProviderEvidence`, invokes `PaymentRuntimeOrchestrator`, and returns adapter evidence plus `RuntimeResult`.

The adapter owns translation only. It does not execute Checkout Sessions, captures, refunds, settlement, persistence, state transitions, transactions, policy evaluation, or accounting.

Canonical flow:

```text
Stripe webhook request
  -> Stripe-Signature verification
  -> Stripe Event parse
  -> ProviderEvidence translation
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

## Signature Verification

Signature verification uses the official Stripe SDK:

```text
EventUtility.ConstructEvent(rawPayload, signatureHeader, webhookSecret, tolerance)
```

Certified checks:

- `Stripe-Signature` header is required.
- Webhook secret is required.
- Timestamp tolerance must be positive.
- Invalid signatures fail closed.
- Expired timestamps fail closed.
- Malformed payloads fail closed.
- Stripe SDK exceptions are translated to `CanonicalProviderError` and do not cross the adapter boundary.

No custom signature cryptography is implemented by the adapter.

## Event Translation Matrix

| Stripe Event | Canonical Output |
|---|---|
| `payment_intent.created` | `ProviderEvidence` |
| `payment_intent.processing` | `ProviderEvidence` |
| `payment_intent.requires_action` | `ProviderEvidence` |
| `payment_intent.succeeded` | `ProviderEvidence` |
| `payment_intent.payment_failed` | `ProviderEvidence` |
| `payment_intent.canceled` | `ProviderEvidence` |
| `charge.succeeded` | `ProviderEvidence` |
| `charge.refunded` | `ProviderEvidence` |
| `charge.dispute.created` | `ProviderEvidence` |
| `charge.dispute.closed` | `ProviderEvidence` |
| `checkout.session.completed` | `ProviderEvidence` |

No Stripe webhook event maps directly to `PaymentDecision`, `PaymentSession`, `PaymentTransaction`, or `PaymentSettlement`.

## Evidence Mapping

The adapter maps:

- Stripe Event Id to `EvidenceReference` and metadata `event_id` / `provider_event_id`.
- Stripe object id to provider reference metadata and `ProviderPayloadReference`.
- Stripe Event type to metadata `event_type`.
- Stripe Event created time to `EvidenceTimestamp`.
- Runtime action correlation id to `CorrelationId`.
- Stripe object metadata to `stripe_metadata_*`.
- Raw payload digest to metadata `event_payload_hash`.
- Adapter evidence digest to `EvidenceHash` and metadata `evidence_hash`.
- Deterministic evidence id from event id, event type, object id, and evidence hash.
- Provider metadata such as API version, livemode, and account when present.

## Replay Protection

Replay protection is deterministic and runtime-compatible:

- Stripe Event Id is stable and becomes the evidence reference.
- Evidence hash is deterministic for the immutable event payload and mapping.
- Evidence id is deterministic for the same Stripe event and object.
- Duplicate detection is performed by the frozen runtime when prior admitted evidence is supplied.
- Duplicate event id, duplicate evidence reference, or duplicate evidence hash fails closed in verification.

The adapter does not add persistence. Durable duplicate storage remains a separate integration phase.

## Error Translation

The adapter translates provider and parsing failures to `CanonicalProviderError`:

| Failure | Canonical Error |
|---|---|
| Missing signature | `STRIPE_WEBHOOK_SIGNATURE_MISSING` |
| Invalid signature | `STRIPE_WEBHOOK_SIGNATURE_INVALID` |
| Expired timestamp | `STRIPE_WEBHOOK_TIMESTAMP_EXPIRED` |
| Malformed payload | `STRIPE_WEBHOOK_MALFORMED_PAYLOAD` |
| Unknown event | `STRIPE_WEBHOOK_EVENT_UNKNOWN` |
| Missing object id | `STRIPE_WEBHOOK_OBJECT_ID_MISSING` |
| Missing webhook secret | `STRIPE_WEBHOOK_SECRET_MISSING` |
| Invalid tolerance | `STRIPE_WEBHOOK_TOLERANCE_INVALID` |

Provider errors become rejected `ProviderEvidence` and are submitted to the runtime so failure remains canonical.

## Architecture Boundaries

Certified boundaries:

- Webhook adapter never creates `PaymentDecision`.
- Webhook adapter never creates `PaymentTransaction`.
- Webhook adapter never creates `PaymentSettlement`.
- Webhook adapter never mutates runtime state.
- Webhook adapter invokes only `PaymentRuntimeOrchestrator`.
- Frozen runtime contains no Stripe dependency.
- Stripe adapts to the provider-neutral runtime.
- Runtime remains the only payment authority.

## Webhook Invariants

- Webhook never changes `PaymentSession`.
- Webhook never creates `PaymentDecision`.
- Webhook never creates `PaymentTransaction`.
- Webhook never creates `PaymentSettlement`.
- Webhook always produces canonical `ProviderEvidence` or rejected provider-error evidence.
- Runtime owns all payment decisions and state outcomes.

## Certification Decision

STRIPE-4 is certified as the canonical Stripe webhook evidence adapter.

It is not a live Stripe rollout certification. Persistence, durable webhook logs, runtime activation, provider operation execution, refund workflow, capture workflow, Checkout runtime, settlement, accounting, dashboards, alerts, and operational runbooks remain separate phases.

Recommended next phase: Stripe durable persistence and operational webhook integration, keeping Stripe evidence subordinate to the frozen provider-neutral runtime.
