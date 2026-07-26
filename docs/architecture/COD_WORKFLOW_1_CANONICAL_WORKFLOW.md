# COD Workflow 1 Canonical Workflow

Date: 2026-07-17

Status: Implemented

Scope: Canonical Workflow Orchestrator only.

## Executive Summary

COD Workflow 1 introduces the canonical business workflow boundary for the COD
Reference Workflow.

The workflow consumes canonical `CodAdmissionLifecycleDecision` values and
produces canonical `CodWorkflowIntent` values. It does not execute Runtime,
mutate lifecycle, admit evidence, settle money, persist state, call controllers,
send notifications, call `PaymentService`, or integrate shipment, carrier,
CommerceHub, email, SMS, or webhook behavior.

The Runtime executes. The Lifecycle decides. The Workflow orchestrates business
intent only.

## Scope

Implemented:

- `ICodWorkflowOrchestrator`
- `CodWorkflowOrchestrator`
- `CodWorkflowContext`
- `CodWorkflowIntent`
- `CodWorkflowOrchestrationResult`
- Canonical workflow intent types
- Lifecycle-decision-to-workflow-intent mapping
- Dependency injection registration with duplicate guard
- Focused certification tests

Excluded:

- Runtime execution
- Runtime Bridge changes
- Lifecycle mutation
- Settlement
- Persistence
- Controllers
- Shipment, carrier, CommerceHub, and `PaymentService` integration
- Notification, email, SMS, webhook, evidence execution, and business policy
  engine behavior

## Workflow Responsibilities

The workflow owns only deterministic business-intent orchestration after the
Lifecycle has returned a canonical decision.

It may:

- consume `CodAdmissionLifecycleDecision`;
- consume immutable workflow context;
- produce canonical workflow intents.

It must not:

- invoke Runtime;
- mutate Lifecycle;
- complete COD payment;
- settle COD money;
- persist state;
- execute an intent or call side-effecting infrastructure.

## Lifecycle Consumption

Certified consumption:

```text
CodAdmissionLifecycleDecision
  -> ICodWorkflowOrchestrator.Orchestrate
  -> CodWorkflowOrchestrationResult
  -> IReadOnlyList<CodWorkflowIntent>
```

The workflow does not consume `CodRuntimeAdmissionOutcome` directly. Lifecycle
remains the decision boundary.

## Workflow Intents

Canonical intents:

- `AdvanceCollection`
- `AwaitEvidence`
- `QueueOperationalReview`
- `CompleteAdmissionWorkflow`
- `RejectAdmissionWorkflow`

Certified mapping:

| Lifecycle Decision | Workflow Intent |
|---|---|
| Accepted | `AdvanceCollection`, `CompleteAdmissionWorkflow` |
| Rejected | `RejectAdmissionWorkflow` |
| AwaitingEvidence | `AwaitEvidence` |
| Deferred | `QueueOperationalReview` |
| AwaitingReview | `QueueOperationalReview` |

No intent implementation, handler, worker, queue, notification, persistence, or
external integration is introduced.

## Workflow Context

`CodWorkflowContext` is immutable and carries:

- Tenant identity
- Order identity
- Collection identity
- Admission identity
- Correlation

The context is not persisted. It is not lifecycle state. It is not Runtime
state. It is orchestration input only.

## Registration

Registration:

```text
AddCodWorkflowOrchestrator()
```

`Program.cs` registers the workflow after:

```text
AddCodAdmissionLifecycle()
```

Duplicate registration is rejected.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodWorkflowOrchestratorCertificationTests
```

The tests certify:

- workflow exists;
- lifecycle decision consumption exists;
- accepted, rejected, await-evidence, deferred, and review mappings exist;
- workflow context and intents are immutable;
- duplicate registration is rejected;
- Program registration order is preserved;
- architecture guards preserve no Runtime, lifecycle writer, settlement,
  persistence, controller, CommerceHub lifecycle, `PaymentService`, email, SMS,
  webhook, or side-effect dependency.

## Certification Decision

| Question | Decision |
|---|---|
| Canonical Workflow implemented? | Yes |
| Lifecycle consumption certified? | Yes |
| Workflow intents certified? | Yes |
| Decision mapping certified? | Yes |
| Workflow context certified? | Yes |
| Registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Lifecycle preserved? | Yes |
| Workflow purity preserved? | Yes |
| Runtime execution certified by workflow? | No |
| Lifecycle mutation certified by workflow? | No |
| Settlement certified by workflow? | No |
| Recommend COD-WORKFLOW-2? | Yes, only as the next bounded workflow phase after intent certification |

## Final Statement

Runtime executes.

Lifecycle decides.

Workflow orchestrates.

Infrastructure performs side effects later.

No responsibilities are merged in COD Workflow 1.
