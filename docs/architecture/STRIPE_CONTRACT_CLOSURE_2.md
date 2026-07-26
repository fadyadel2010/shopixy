# STRIPE-CONTRACT-CLOSURE-2 Webhook Admission, Replay, and Single Writer Closure

Date: 2026-07-12

Status: APPLICATION WEBHOOK CONTRACT CERTIFIED

## Certification Decision

Stripe webhook production contract gaps are closed at the application integration boundary without modifying Payment Runtime, Stripe adapters, provider contracts, canonical runtime objects, or the policy engine.

The closure introduces:

- Application-owned durable replay storage.
- Prior evidence loading before Runtime execution.
- Outcome admission before canonical adapter execution.
- Runtime invocation only for admitted webhook evidence.
- Application persistence only after Runtime execution.
- Existing payment lifecycle service as the single application writer.

## Replay Architecture

`StripeWebhookReplayRecord` is the durable application replay store. It persists:

- Stripe provider event id.
- Canonical admitted evidence hash.
- Evidence id.
- Processing timestamp.
- Correlation id.
- Provider reference.
- Event type, action id, payment intent id, order id, and admission classification for reconstruction and audit.

Runtime remains storage-unaware. The application loads prior rows and reconstructs canonical `AdmittedEvidence` before invoking `StripeWebhookEvidenceAdapter`.

## Outcome Admission

`StripeWebhookOutcomeAdmissionService` classifies webhook events before Runtime execution:

| Stripe event | Classification | Runtime admission |
|---|---|---|
| `payment_intent.processing` | Non-terminal | No |
| `payment_intent.requires_action` | Non-terminal | No |
| `payment_intent.created` | Non-terminal | No |
| `payment_intent.succeeded` | Terminal | Yes |
| `payment_intent.payment_failed` | Terminal | Yes |
| `payment_intent.canceled` | Terminal | Yes |
| `charge.refunded` | Terminal | Yes |
| `checkout.session.completed` with `payment_status=paid` | Terminal | Yes |
| `checkout.session.completed` unpaid | Non-terminal | No |
| dispute events | Disputed | No |
| unknown events | Ignored | No |

Decision: unknown events are classified as `Ignored` rather than admitted. This preserves provider-neutral Runtime behavior and prevents unmapped Stripe events from becoming executable canonical evidence.

## Webhook Pipeline

Certified application flow:

```text
Webhook
  -> Signature verification
  -> Replay Store duplicate check
  -> Prior Evidence Loader
  -> Outcome Admission
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Application Persistence
  -> Replay Store persistence
```

Rejected, ignored, non-terminal, and disputed evidence does not enter Runtime. Duplicate/replay events are collapsed before application persistence.

## Replay Storage

The replay table has a tenant/provider/event unique index:

```text
IX_StripeWebhookReplayRecords_Tenant_Provider_Event
```

This is separate from `PaymentWebhookLogs`. `PaymentWebhookLogs` remains the raw webhook audit/idempotency log for the existing application workflow; `StripeWebhookReplayRecord` is the canonical Runtime prior-evidence store.

## Single Writer Rules

The application still writes orders, payment intent status, payment history, settlement-related effects, and transaction-related effects through the existing payment lifecycle service.

Runtime produces canonical artifacts only. It does not persist, post ledger entries, create SQL rows, or mutate order/payment state. The new integration service does not call lifecycle mutation methods; `StripePaymentService` invokes Runtime admission first and then delegates persistence to the existing lifecycle writer.

No legacy SQL authority competes with Runtime because Runtime is not a SQL writer. No second payment/order writer was introduced.

## Architecture Rules

1. Replay Store belongs to Application Layer.
2. Prior Evidence Loader belongs to Application Layer.
3. Outcome Admission belongs to Application Layer.
4. Runtime remains provider-neutral and storage-free.
5. Stripe adapter remains a translation boundary.
6. Unknown, non-terminal, disputed, duplicate, and replay evidence must not become executable Runtime evidence.
7. Application persistence happens only after Runtime execution for admitted terminal evidence.
8. Existing lifecycle service remains the sole application writer for payment/order persistence.

## Known Exclusions

This closure does not implement:

- Advanced Stripe capture/refund/cancel Runtime coordination.
- CommerceHub direct Stripe payment/refund path cutover.
- Production dashboard, alert, runbook, key rotation, or Stripe Workbench endpoint verification.
- Full sandbox/live signed webhook drill with real Stripe delivery.

Those remain later rollout and integration phases.

## Certification Decision

| Question | Answer |
|---|---|
| Replay Store implemented? | Yes. |
| Prior Evidence Loader implemented? | Yes. |
| Outcome Admission implemented? | Yes. |
| Webhook Pipeline certified? | Yes for storefront Stripe webhook application integration. |
| Replay Protection certified? | Yes for durable application replay storage and prior-evidence loading. |
| Duplicate Detection certified? | Yes through replay store plus existing webhook log uniqueness. |
| Single Writer preserved? | Yes. Existing application lifecycle persistence remains the only SQL writer. |
| Runtime Independence preserved? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Production Contract Closure certified? | Yes for STRIPE-CONTRACT-CLOSURE-2 scope. |
| Recommend next phase? | Yes. STRIPE-CONTRACT-CLOSURE-3 should close advanced-operation Runtime coordination and CommerceHub Stripe path convergence without Runtime modification. |

