# COD Lifecycle 1 Canonical Admission Lifecycle

Date: 2026-07-17

Status: Implemented

Scope: Canonical Admission Lifecycle only.

## Executive Summary

COD Lifecycle 1 introduces the canonical Admission Lifecycle boundary for the
COD Reference Workflow.

The lifecycle consumes canonical `CodRuntimeAdmissionOutcome` values and returns
canonical lifecycle decisions. It does not execute Runtime, modify Runtime,
mutate payment lifecycle state, settle money, persist state, admit evidence,
call controllers, call `PaymentService`, or integrate shipment, carrier, or
CommerceHub workflows.

The Runtime executes. The Bridge translates. The Application coordinates. The
Lifecycle decides.

## Scope

Implemented:

- `ICodAdmissionLifecycle`
- `CodAdmissionLifecycle`
- `CodAdmissionLifecycleDecision`
- Canonical lifecycle decision statuses
- Canonical workflow states
- Outcome-to-decision transition rules
- Dependency injection registration with duplicate guard
- Focused certification tests

Excluded:

- Payment Runtime modification
- Runtime invocation
- Runtime Bridge changes
- Settlement
- Persistence
- Replay, recovery, and retention
- Shipment, carrier, CommerceHub, controllers, and `PaymentService`
- Evidence execution
- Business policy engine

## Lifecycle Responsibilities

The Admission Lifecycle owns only deterministic workflow-decision mapping after
canonical Runtime Admission has produced an outcome.

It may:

- consume `CodRuntimeAdmissionOutcome`;
- map canonical admission statuses to canonical workflow decisions;
- return a `CodAdmissionLifecycleDecision`.

It must not:

- invoke `PaymentRuntimeOrchestrator`;
- call Runtime stages;
- translate Runtime objects;
- mutate payment lifecycle;
- complete COD payment;
- settle COD money;
- write persistence;
- route business workflow side effects.

## Outcome Consumption

The lifecycle consumes the canonical admission outcome produced upstream:

```text
CodRuntimeAdmissionOutcome
  -> ICodAdmissionLifecycle.Decide
  -> CodAdmissionLifecycleDecision
```

The lifecycle does not inspect provider, carrier, shipment, settlement,
controller, database, or frontend state. Metadata is carried forward for
traceability only.

## Transition Rules

Canonical mapping:

| Admission Outcome | Lifecycle Decision | Workflow State |
|---|---|---|
| Accepted | Accepted | CollectionAccepted |
| Rejected | Rejected | CollectionRejected |
| Deferred | Deferred | CollectionDeferred |
| Incomplete | AwaitingEvidence | AwaitingCollectionEvidence |
| Unsupported | AwaitingReview | AwaitingOperationalReview |
| Unknown | AwaitingReview | AwaitingOperationalReview |

No transition performs payment execution, settlement, persistence, replay,
recovery, retention, or evidence admission.

## Registration

Registration:

```text
AddCodAdmissionLifecycle()
```

`Program.cs` registers the lifecycle after:

```text
AddCodRuntimeAdmissionBridge()
```

Duplicate registration is rejected.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodAdmissionLifecycleCertificationTests
```

The tests certify:

- lifecycle exists;
- canonical admission outcome consumption exists;
- accepted, rejected, deferred, incomplete, and unsupported mappings exist;
- canonical lifecycle decisions exist;
- duplicate registration is rejected;
- Program registration order is preserved;
- lifecycle source has no Runtime, lifecycle writer, settlement, persistence,
  controller, CommerceHub lifecycle, or `PaymentService` dependency;
- Runtime source has no COD lifecycle dependency.

## Certification Decision

| Question | Decision |
|---|---|
| Admission Lifecycle implemented? | Yes |
| Outcome consumption certified? | Yes |
| Transition rules certified? | Yes |
| Lifecycle decisions certified? | Yes |
| Registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Lifecycle purity preserved? | Yes |
| Runtime execution certified by lifecycle? | No |
| Lifecycle mutation certified? | No |
| Settlement certified? | No |
| Recommend COD-WORKFLOW-1? | Yes, only as the next bounded phase after lifecycle decision certification |

## Final Statement

Runtime executes.

Bridge translates.

Application coordinates.

Lifecycle decides.

Settlement remains outside. Business workflow remains outside. Runtime remains
frozen.
