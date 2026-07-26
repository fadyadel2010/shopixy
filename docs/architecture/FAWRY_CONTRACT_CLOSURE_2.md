# FAWRY-CONTRACT-CLOSURE-2 Webhook Runtime Integration Certification

Date: 2026-07-15  
Scope: Fawry POS Admin + Storefront application-owned webhook runtime integration only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-CONTRACT-CLOSURE-2 implements the application-owned webhook runtime integration through `IFawryWebhookRuntimeIntegrationService` and `FawryWebhookRuntimeIntegrationService`.

The integration accepts an official Fawry webhook payload, obtains verified provider evidence from the existing webhook evidence adapter, performs application-owned replay and duplicate admission, submits evidence to the frozen Payment Runtime, admits only exact positive Runtime outcomes for lifecycle persistence, and records durable application replay history only after successful application persistence.

No Payment Runtime, runtime bridge, provider adapter, webhook evidence adapter, advanced-operations adapter, controller, public DTO, frontend, business rule, CommerceHub, or additional lifecycle writer was modified.

## Webhook Execution Flow

```text
Official Fawry webhook
  -> IFawryWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync
       -> parse official callback shape
       -> load prior admitted evidence from FawryWebhookReplayRecords
       -> detect prior replay by provider event id
       -> detect duplicate completion by PaymentWebhookLogs
       -> resolve local Fawry payment intent
       -> build canonical Runtime session/action snapshot
       -> IFawryWebhookEvidenceAdapter.ProcessAsync
            -> signature verification
            -> canonical ProviderEvidence
       -> PaymentRuntimeOrchestrator.Execute
            -> Runtime admission, verification, decision, transition, transaction, settlement
       -> application outcome admission
       -> optional lifecycle request projection
       -> application persistence by existing single lifecycle writer
       -> FawryWebhookReplayRecords replay record
```

The integration constructs Runtime inputs around the provider evidence timestamp so the frozen Runtime receives a coherent webhook event timeline. Runtime remains the only authority for evidence admission and canonical payment outcome.

## Replay Flow

Replay authority is application-owned and stored in `FawryWebhookReplayRecords`.

```text
Webhook request id
  -> FawryWebhookPriorEvidenceLoader
       -> load prior records by tenant, provider, provider event id, or provider reference
       -> rehydrate AdmittedEvidence for Runtime prior-evidence input
       -> classify same provider event id as replay
  -> webhook adapter replay hook rejects evidence
  -> Runtime receives rejected evidence
  -> no lifecycle mutation
  -> no new replay record
```

Replay records contain canonical identifiers and hashes only. They do not store secrets, authentication material, raw signatures, raw secure keys, or sensitive provider tokens.

## Duplicate Flow

Duplicate detection remains application-owned and independent from Runtime storage.

```text
Webhook request id
  -> PaymentWebhookLogs lookup for completed/skipped Fawry event
  -> duplicate hook rejects evidence
  -> Runtime receives rejected evidence
  -> integration returns duplicate rejection
  -> no lifecycle mutation
  -> no replay append
```

Previously admitted evidence with the same provider reference is supplied to Runtime as prior evidence so Runtime can also enforce duplicate and ordering semantics without owning replay persistence.

## Outcome Admission

Application outcome admission is deliberately separate from provider evidence production and Runtime authority.

| Fawry status | Application classification | Runtime admission | Lifecycle persistence |
|---|---|---:|---:|
| `PAID`, `CAPTURED` | Terminal | Yes | Yes, only after exact Runtime success |
| `AUTHORIZED`, `REFUNDED` | Terminal | Yes | No |
| `NEW` | Non-terminal | No | No |
| `FAILED`, `EXPIRED`, `CANCELED`, `CANCELLED` | Terminal negative | No | No |
| Unknown | Ignored | No | No |

Lifecycle persistence is allowed only when Runtime returns:

- `OverallStatus.Succeeded`;
- `TransitionResult.Status.Applied`;
- `TransitionResult.Session.Status.Completed`;
- `TransactionResult.Status.Created`;
- `SettlementResult.Status.Pending`.

Any unexpected Runtime outcome is rejected by the application boundary and produces no lifecycle mutation.

## Persistence Ordering

Lifecycle is never mutated before Runtime admission.

```text
Verified ProviderEvidence
  -> RuntimeResult
  -> application exact outcome admission
  -> HandlePaymentResultRequest projection
  -> existing PaymentLifecycleService single writer
  -> SaveChanges succeeds
  -> PersistReplayRecordAsync appends FawryWebhookReplayRecord
  -> SaveChanges succeeds
```

`PersistReplayRecordAsync` only stages the replay record. The caller must persist lifecycle state first, then save the replay record. Replay history is therefore never ahead of successful application persistence.

## Architecture Preservation

| Boundary | Decision |
|---|---|
| Payment Runtime | Unchanged and storage-unaware. |
| Runtime Bridge | Unchanged. Webhook integration invokes the existing Runtime orchestrator directly with provider evidence. |
| Provider Adapter | Unchanged. |
| Webhook Evidence Adapter | Unchanged; it continues to produce evidence only. |
| Advanced Operations Adapter | Unchanged. |
| Controllers | Unchanged. Activation is outside this closure. |
| DTOs | Unchanged. |
| Frontend | Unchanged. |
| Business rules | Unchanged. |
| CommerceHub | Unchanged. |
| Lifecycle writer | Preserved. The integration creates a request for the existing lifecycle writer and does not call `HandlePaymentResultAsync`. |
| Replay store | New application-owned SQL entity and migration only. |
| `.ai-context` | Unchanged; no new permanent cross-provider architectural truth was discovered. |

No Architecture Exception was required.

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryContractClosure2CertificationTests.cs` cover:

| Evidence | Coverage |
|---|---|
| Replay store | Durable tenant-owned SQL model, DbContext mapping, and unique provider-event index |
| Prior evidence loader | Rehydrates previously admitted Runtime evidence from application replay records |
| Replay detection | Same provider event id rejects without lifecycle admission |
| Duplicate detection | Processed webhook log detection remains application-owned |
| Outcome admission | Positive Runtime outcome is required before lifecycle projection |
| Webhook ordering | Runtime snapshot timestamps are coherent with provider evidence time |
| Single writer | Integration does not call lifecycle writer directly |
| Architecture isolation | Runtime, bridge, adapters, controller, and CommerceHub remain free of replay-store dependencies |

## Certification Decision

FAWRY-CONTRACT-CLOSURE-2 is certified for application-owned webhook runtime integration, replay protection, duplicate detection, prior admitted evidence loading, outcome admission, and replay recording.

Production activation should proceed as a separate controller/application wiring phase that preserves the same boundaries and continues to route lifecycle mutation through the existing single lifecycle writer.
