# MANUAL-3 Runtime Bridge

Date: 2026-07-16

Scope: Manual Payment Runtime Bridge execution delegation only.

Status: CERTIFIED - RUNTIME BRIDGE ONLY

## Executive Summary

MANUAL-3 implements the Manual Payment bridge between the application layer and
the frozen provider-neutral Payment Runtime.

The bridge accepts an already-authorized Manual Payment application request,
invokes the certified Manual Payment Provider Adapter once, forwards canonical
`ProviderEvidence` into `PaymentRuntimeOrchestrator`, and returns the exact
`RuntimeResult` plus execution mapping to the application layer.

No Runtime code, SQL, persistence, controller, upload, approval, review,
replay, recovery, checkpoint, webhook, callback, or lifecycle mutation is added.

## Bridge Responsibility

The bridge owns only:

- execution delegation;
- Manual Payment Provider Adapter invocation;
- provider evidence admission into the Runtime orchestrator;
- rejected evidence propagation;
- canonical provider error propagation as rejected Runtime input;
- RuntimeResult propagation;
- ExecutionResult mapping.

The bridge does not own:

- tenant configuration resolution;
- business policy;
- proof upload;
- approval, rejection, or review workflow;
- SQL, EF Core, persistence, replay, or checkpoint storage;
- lifecycle mutation;
- controller behavior;
- Runtime stages or Runtime artifacts.

## Execution Flow

```text
Application
  -> ManualPaymentRuntimeBridge
  -> IManualPaymentProviderAdapter.CreatePendingPayment
  -> ProviderEvidence or CanonicalProviderError
  -> PaymentRuntimeOrchestrator.Execute
  -> RuntimeResult
  -> ManualPaymentExecutionResult
  -> Application
```

Rejected provider evidence is forwarded unchanged into Runtime. Adapter failure
is translated to deterministic rejected provider evidence and then admitted to
Runtime so failure remains provider-neutral and fail-closed.

## Runtime Boundary

The frozen Runtime is unchanged. The bridge calls only
`PaymentRuntimeOrchestrator.Execute` and never calls individual Runtime stages.

Manual Payment provider details remain outside Runtime. Runtime source contains
no Manual Payment provider logic, channel logic, SQL, controller, upload,
approval, or review concern.

## Regression

Focused certification tests are in:

```text
PointOfSale.Tests/Architecture/ManualPaymentRuntimeBridgeCertificationTests.cs
```

Coverage includes:

- execution delegated;
- adapter invoked once;
- ProviderEvidence forwarded;
- rejected evidence propagated;
- CanonicalProviderError propagated;
- RuntimeResult preserved;
- ExecutionResult mapping preserved;
- architecture guards for no SQL, no persistence, no lifecycle authority, no
  controller coupling, and no proof/review workflow.

## Certification Decision

| Question | Answer |
|---|---|
| Runtime Bridge implemented? | Yes. |
| Execution delegation certified? | Yes. |
| ProviderEvidence flow certified? | Yes. |
| Rejected evidence handling certified? | Yes. |
| RuntimeResult propagation certified? | Yes. |
| ExecutionResult mapping certified? | Yes. |
| Architecture preserved? | Yes. |
| Runtime preserved? | Yes. No Runtime change. |
| Recommend MANUAL-4? | Yes. Proceed only to evidence-adapter certification; do not activate proof upload, approval, review, persistence, lifecycle mutation, or controllers in this bridge phase. |

No permanent cross-provider architecture truth was discovered. No `.ai-context`
update is required.
