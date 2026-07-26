# PAYMOB-CONTRACT-CLOSURE-2 Webhook Admission, Replay, and Single Writer Closure

Date: 2026-07-13

Status: APPLICATION WEBHOOK CONTRACT CERTIFIED

## Executive Summary

PAYMOB-CONTRACT-CLOSURE-2 closes the Paymob webhook/callback admission boundary between authenticated Paymob provider events and the certified Payment Runtime without modifying the frozen Runtime, Runtime Bridge, Provider Adapter, Webhook Adapter, controllers, frontend, DTOs, or business rules.

`PaymobWebhookRuntimeIntegrationService` is the application-owned admission layer around the certified Paymob webhook adapter. It owns replay detection, duplicate detection, prior evidence loading, outcome admission, Runtime invocation coordination, and replay-record persistence.

## Replay Architecture

`PaymobWebhookReplayRecord` is the durable application replay store. It persists tenant, provider, Paymob transaction/event id, canonical evidence id/hash/reference, provider reference, action id, correlation id, processing timestamp, local payment intent id, order id, event type, and admission classification.

The Runtime remains replay-agnostic and storage-free. The application rehydrates prior `AdmittedEvidence` from Paymob replay records and supplies it to the certified Runtime through the Paymob webhook adapter.

## Admission Pipeline

Certified flow:

```text
Authenticated Paymob ProviderEvidence
  -> Replay Check
  -> Duplicate Check
  -> Prior Evidence Load
  -> Outcome Admission
  -> PaymobWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> PaymentLifecycleService
  -> Paymob Replay Store persistence
```

Replay and duplicate callbacks are rejected through the Paymob webhook adapter hook path, producing rejected canonical evidence and a `RuntimeResult` without lifecycle mutation.

## Outcome Admission

Paymob outcome admission is application-owned:

| Paymob callback state | Classification | Runtime admission |
|---|---|---|
| pending transaction | Non-terminal | No |
| successful payment or authorization | Terminal | Yes |
| refunded transaction | Terminal | Yes |
| voided/error terminal negative | Terminal negative lifecycle | No Runtime success path; existing lifecycle writer handles failure |
| unmapped callback state | Ignored | No |

Provider-specific callback concepts terminate inside the Paymob application layer. Only canonical `ProviderEvidence` is admitted to the Runtime.

## Replay Store

The replay table is `PaymobWebhookReplayRecords`.

Required indexes:

```text
IX_PaymobWebhookReplayRecords_Tenant_Provider_Event
IX_PaymobWebhookReplayRecords_Tenant_ProviderReference
IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash
```

The unique replay identifier is tenant + provider + provider event id. For Paymob, the provider event id is the Paymob transaction id carried by the authenticated callback.

## Failure Flow

Replay:

```text
Replay
  -> Rejected ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

Duplicate:

```text
Duplicate
  -> Rejected ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

Invalid callback:

```text
Invalid Callback
  -> Rejected ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

If an invalid callback is too malformed to resolve local application context, it fails closed before lifecycle persistence and produces no payment mutation.

## Architecture Rules

1. Replay Store belongs to the Application layer.
2. Prior Evidence Loader belongs to the Application layer.
3. Outcome Admission belongs to the Application layer.
4. Runtime remains provider-neutral, persistence-free, and SQL-free.
5. Runtime never loads prior evidence.
6. Runtime never queries SQL.
7. Paymob callback protocol concepts terminate inside the Paymob application boundary.
8. The Paymob webhook adapter remains a translation/authentication boundary.
9. `PaymentRuntimeOrchestrator` remains the only Runtime sequencer.
10. `PaymentLifecycleService` remains the single application persistence authority for payment/order lifecycle mutation.
11. Replay records are persisted only after Runtime execution and application lifecycle persistence for admitted terminal evidence.
12. No Runtime, Runtime Bridge, Provider Adapter, controller, DTO, frontend, or business-rule mutation is authorized by this closure.

## Stripe Delta

Only protocol differences are documented:

| Stripe | Paymob |
|---|---|
| Official Stripe signature verification over raw event payload. | Paymob HMAC verification over Paymob canonical callback fields. |
| Provider event id is Stripe event id. | Provider event id is Paymob transaction id. |
| Stripe terminal success uses `payment_intent.succeeded` and refund uses `charge.refunded`. | Paymob terminal success/refund uses callback transaction flags. |
| Stripe PaymentIntent id resolves local payment intent. | Paymob order id, transaction id, or merchant order id resolves local payment intent. |

Architecture is identical: authenticated provider fact, application replay/duplicate/prior evidence/outcome admission, canonical Runtime, one lifecycle writer, durable replay record.

## Certification Decision

PAYMOB-CONTRACT-CLOSURE-2 is certified for application-owned Paymob webhook admission, replay protection, duplicate detection, prior-evidence loading, outcome admission, and replay persistence without modifying the frozen Payment Runtime or certified Paymob adapter boundaries.

## Success Answers

| Question | Answer |
|---|---|
| Replay Store implemented? | Yes. |
| Prior Evidence Loader implemented? | Yes. |
| Outcome Admission implemented? | Yes. |
| Webhook Pipeline certified? | Yes for Paymob application-owned webhook admission. |
| Replay Protection certified? | Yes through `PaymobWebhookReplayRecord` and adapter rejected-evidence execution. |
| Duplicate Detection certified? | Yes through replay store plus existing webhook log duplicate detection. |
| Single Writer preserved? | Yes. Existing `PaymentLifecycleService` remains the only payment/order lifecycle writer. |
| Runtime Independence preserved? | Yes. Runtime remains SQL-free and persistence-free. |
| Architecture boundaries preserved? | Yes. Runtime, bridge, adapter, controllers, frontend, DTOs, and business rules remain independent. |
| Production Contract Closure certified? | Yes for PAYMOB-CONTRACT-CLOSURE-2 scope. |
| Recommend next phase? | Yes. Proceed to provider operational rollout validation and then future provider closure phases using the same Stripe reference architecture. |

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.
