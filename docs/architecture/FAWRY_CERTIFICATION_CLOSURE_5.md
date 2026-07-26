# FAWRY-CERTIFICATION-CLOSURE-5 - Durable Charge Registration Checkpoint

Date: 2026-07-15  
Mission finding: the durable outbound Fawry charge-registration checkpoint only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Finding Identifier Note

The mission calls this item `FAWRY-FINAL-005`, or the next remaining High
finding concerning the durable charge-registration/provider-mutation
checkpoint. `FAWRY_FINAL_RECERTIFICATION.md` labels that substantive finding
`FAWRY-FINAL-003`. `FAWRY_CERTIFICATION_CLOSURE_4.md` records the numbering
mismatch and explicitly defers this checkpoint finding to Closure-5. This
closure follows the mission's substantive title, objective, named components,
and confirmation matrix. It closes only the outbound charge checkpoint; it does
not review or implement another Board finding.

## Certification Decision

The Board's substantive finding was **valid**.

Before this closure, `PaymentService` committed the application-owned
`PaymentIntent` before entering `FawryRuntimeApplicationService`, but it did not
commit a mutation-started checkpoint before the provider charge request. The
Fawry reference number was persisted only after the provider application call
returned. If Fawry accepted the charge and the response was lost, the process
crashed, or local persistence failed, the next retry saw an incomplete intent
and could execute charge creation again. The stable merchant reference reduced
the risk, but no official create-charge idempotency guarantee made it safe to
rely on the provider to suppress the duplicate.

The minimum Application-layer correction now commits an explicit
`charge_registration_started` fence before the first provider mutation. A retry
can reuse an accepted provider reference, finalize an interrupted local commit,
or fail closed behind that fence; it cannot automatically issue a second charge.

## Official Fawry Documentation Evidence

Official FawryPay documentation was re-read on 2026-07-15:

- [Create Payment Requests Using FawryPay Reference Number](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis)
- [Get Payment Status V2](https://developer.fawrystaging.com/docs/sdks/payment-notifications/get-payment-status-v2)

The create-reference-number API is a server-side `POST` that accepts the
merchant's reference and authenticated charge data and returns the provider's
Fawry reference number and expiration data. The merchant reference is described
as a unique merchant-side order reference. The official page does not document
an idempotency key, replay header, or guarantee that replaying a timed-out create
request returns the original charge rather than creating another charge.

The status API provides a signed inquiry path for an existing merchant/provider
identity. The architectural consequence is an inference from those documented
contracts: the application must durably record that mutation began, must retain
an accepted provider identifier before later local work, and must reconcile an
ambiguous submission through existing provider/webhook/inquiry evidence. It
must not treat transport uncertainty as permission to replay charge creation.

## Root Cause

The previous sequence was:

```text
commit PaymentIntent
  -> call Fawry charge creation
  -> receive reference number
  -> write provider fields and PaymentIntentCreated event
```

There was no durable state distinguishing "provider mutation has not started"
from "provider mutation may have succeeded but no response was retained." Both
states looked like an incomplete `PaymentIntent`, so retry and background
recovery could re-enter the provider create operation.

## Current Implementation Review

### `PaymentService`

The existing `PaymentIntent` is still the application-owned registration
intent. It is committed before provider execution. Fawry readiness now takes a
serializable order lock, reloads that same intent, validates its checkpoint, and
commits `charge_registration_started` in a separate transaction before calling
`InitiateFawryPersistedPaymentIntentAsync`.

The provider call is outside the checkpoint transaction and occurs only after
that SQL commit succeeds. There is no provider execution from an uncommitted or
indeterminate checkpoint.

### `FawryRuntimeApplicationService`

The service remains the existing Application-to-Runtime execution boundary. It
continues to build the Fawry Runtime request and return canonical reference,
expiration, URL, evidence, Runtime result, and execution result. Its source and
contract are unchanged. Checkpoint state does not enter Runtime.

### Checkpoint persistence

`FawryProviderCheckpointStore` owns the provider-specific Application
checkpoint in the existing SQL `PaymentIntent.ProviderMetadata` column. No new
entity, table, migration, DTO, or schema change is required.

The permitted state progression is:

```text
persisted PaymentIntent
  -> charge_registration_started
  -> provider_reference_accepted_runtime_completed_event_pending
  -> provider_reference_committed
```

Rules:

1. `charge_registration_started` contains no provider reference artifacts and
   is committed before the irreversible create-charge request.
2. A returned reference number, expiration, and optional payment URL are
   immediately saved as an accepted-reference checkpoint.
3. The accepted artifacts remain in checkpoint metadata until they and the
   `PaymentIntentCreated` outbox event can be finalized together in one SQL
   transaction.
4. Completion projects the same reference into `ReferenceCode` and
   `ProviderIntentId`; it never invents a replacement identity.
5. Checkpoint ownership binds provider, tenant, order, and payment-intent IDs.
   Malformed, partial, unknown, or conflicting state is rejected with
   `FAWRY_PROVIDER_CHECKPOINT_INVALID`.
6. Timestamps are normalized to PostgreSQL microsecond precision so a durable
   reload cannot falsely diverge from the committed checkpoint.

### Retry, recovery, and rehydration

All retry and recovery paths converge on the persisted intent and the same
checkpoint decision:

```text
completed checkpoint
  -> return existing provider reference; no provider call

accepted reference awaiting finalization
  -> project the existing reference and persist the event; no provider call

registration started without accepted reference
  -> reject FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN; no provider call

no checkpoint and no provider artifacts
  -> commit the one permitted started checkpoint, then create the charge

incomplete, malformed, or conflicting state
  -> reject FAWRY_PROVIDER_CHECKPOINT_INVALID; no provider call
```

`CheckoutPaymentIntentRecoveryService` already routes an incomplete Fawry intent
back to `PaymentService`. Because accepted artifacts remain durably available in
the checkpoint while the normal projection is incomplete, restart recovery
finalizes that exact reference. A registration-started ambiguity remains fenced
for provider inquiry, webhook, or operator reconciliation. Rehydration does not
create a replacement local intent or a replacement provider charge.

## Implemented Correction

Production source changed only in the permitted Application/checkpoint surface:

- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryProviderCheckpointStore.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryApplicationServiceCollectionExtensions.cs`

Focused and directly affected certification tests:

- `PointOfSale.Tests/Architecture/FawryCertificationClosure5Tests.cs`
- `PointOfSale.Tests/Architecture/FawryConvergence1CertificationTests.cs`
- `PointOfSale.Tests/Architecture/FawryPaymentAuthorityCertificationTests.cs`

Explicitly unchanged by this closure:

- `PointOfSale.Core/Payments/Runtime/**`
- `FawryRuntimeBridge`
- `FawryPaymentAdapter` and provider protocol contracts
- payment lifecycle services and business rules
- controllers, webhook protocol, routes, DTOs, and frontend
- SQL entities, EF mappings, migrations, and database schema

No new permanent cross-provider architectural truth was discovered. This is a
provider-specific enforcement of the existing `PAYMENT_REFERENCE_FREEZE` rule
that Application owns durable mutation checkpoints. No `.ai-context` file is
modified.

## Focused Certification Tests

`FawryCertificationClosure5Tests` proves:

- the persisted registration intent and `charge_registration_started`
  checkpoint exist before mocked provider execution;
- successful completion persists the accepted reference and retry reuses it
  with exactly one provider create call;
- an ambiguous provider response retains the started fence and a retry fails
  with `FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN`, without a second provider call;
- restart/recovery finalizes a durable accepted reference with zero provider
  calls;
- partial provider projection without a valid checkpoint fails closed;
- architecture guards keep checkpoint state out of Runtime, Runtime Bridge, and
  the provider adapter.

## Regression

Executed on 2026-07-15:

| Validation | Result |
|---|---:|
| Closure-5 focused certification | **5 passed, 0 failed, 0 skipped** |
| Closure-2/3/4/5 + Fawry application contracts + frozen Runtime | **154 passed, 0 failed, 0 skipped** |
| API compilation | **Passed, 0 errors**; 84 repository-existing warnings |
| Complete Fawry aggregate | **135 passed, 7 failed, 0 skipped** |

The complete aggregate retains the same seven failures documented by Closure-4:

- four `FawryWebhookReliabilityTests` cannot seed the stale local PostgreSQL
  schema because `Orders.BillingAddressAdmissionEvidenceId` is absent;
- three `FawryPaymentAuthorityCertificationTests` retain historical assertions
  for superseded provider-adapter/webhook source strings and the intentionally
  absent permanent `PASS SF-PAY7` context marker.

None exercises the new checkpoint behavior. The five focused checkpoint tests,
the combined certification/Runtime regression, and compilation are green. An
unqualified whole-Fawry aggregate remains withheld only for those documented
pre-existing conditions.

## Final Certification Matrix

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** Provider mutation could begin without a durable mutation-started fence. |
| Durable checkpoint certified? | **Yes.** The application commits registration-started, accepted-reference, and completed states in SQL. |
| Duplicate provider charge prevented? | **Yes for automatic create/retry/recovery.** Accepted identity is reused; ambiguity and invalid state fail closed instead of recreating. |
| Recovery certified? | **Yes.** An accepted reference is finalized without provider execution; an ambiguous started state requires reconciliation. |
| Retry certified? | **Yes.** Completed and accepted checkpoints are reused, while indeterminate state is rejected. |
| Regression passed? | **Yes for Closure-5, the combined certification/application/Runtime slice, and compilation.** The whole-Fawry aggregate retains seven unrelated baseline failures. |
| Runtime preserved? | **Yes.** Runtime source and contracts are unchanged. |
| Architecture preserved? | **Yes.** Application owns checkpoints, provider code owns protocol, Runtime owns payment authority, and lifecycle ownership is unchanged. |
| Recommend Closure-6? | **Yes, only as a separately bounded review of the next Board-assigned finding.** |

## Closure Decision

The durable outbound Fawry charge-registration finding is **CLOSED at source**.
Before any Fawry create-charge mutation, a durable application-owned checkpoint
now exists. Retry and recovery reuse durable accepted identifiers, and incomplete
or indeterminate state fails closed without another provider charge.

This decision does not claim live Sandbox acceptance, deployment readiness,
whole-provider certification, or closure of any other Board finding.
