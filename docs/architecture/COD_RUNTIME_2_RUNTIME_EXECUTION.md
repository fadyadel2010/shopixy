# COD Runtime 2 Runtime Execution

Date: 2026-07-17

Status: Implemented

Scope: Runtime Admission Execution through the frozen Payment Runtime only.

## Executive Summary

COD Runtime 2 performs the first COD Runtime invocation using the Canonical
Runtime Admission Bridge and the existing frozen Payment Runtime public
execution surface.

The Application coordinates. The Bridge translates. The Runtime executes. The
Bridge translates the Runtime response or failure back to the canonical COD
admission outcome.

No Runtime behavior, Runtime stage, lifecycle service, settlement service,
controller, shipment path, CommerceHub path, `PaymentService`, persistence,
replay, recovery, retention, or evidence execution ownership was modified.

## Scope

Implemented:

- Application Service invokes the existing `PaymentRuntimeOrchestrator.Execute`
  public Runtime surface.
- Runtime invocation uses only the canonical request translated by
  `ICodRuntimeAdmissionBridge.TranslateRequest`.
- Runtime response returns through
  `ICodRuntimeAdmissionBridge.TranslateResponse`.
- Runtime failure returns through `ICodRuntimeAdmissionBridge.TranslateError`
  via the bridge response translation.
- Focused certification coverage for invocation, ordering, response
  translation, failure translation, source immutability, lifecycle absence,
  settlement absence, and architecture guards.

Excluded:

- Payment Runtime modification.
- Runtime stage modification.
- `PaymentRuntimeOrchestrator` modification.
- Lifecycle mutation.
- Settlement.
- Persistence.
- Replay.
- Recovery.
- Retention.
- Evidence execution ownership.
- Business rules.
- Controllers.
- Shipment, carrier, CommerceHub, and `PaymentService` integration.

## Execution Flow

Certified flow:

```text
CodRuntimeAdmissionApplicationService
  -> PrepareAdmissionAsync
  -> CodRuntimeAdmissionApplicationValidation
  -> ICodRuntimeAdmissionBridge.TranslateRequest
  -> PaymentRuntimeOrchestrator.Execute
  -> ICodRuntimeAdmissionBridge.TranslateResponse
  -> CodRuntimeAdmissionApplicationResult.FromOutcome
```

Invalid application-owned input still short-circuits before bridge translation
and before Runtime execution.

## Runtime Invocation

The Application Service invokes the frozen Runtime through:

```text
PaymentRuntimeOrchestrator.Execute(
    RuntimeExecutionContext,
    ProviderEvidence,
    PaymentAction,
    PaymentSession)
```

Those four inputs come only from `CodRuntimeAdmissionBridge.TranslateRequest`.
The Application Service does not construct Runtime objects itself and does not
call individual Runtime stages.

## Response Flow

Successful Runtime execution is translated by:

```text
ICodRuntimeAdmissionBridge.TranslateResponse(...)
```

The returned value is the canonical `CodRuntimeAdmissionOutcome`. The
Application Service does not interpret lifecycle meaning and does not mutate
payment state.

## Failure Flow

Runtime failure remains Runtime-owned and bridge-translated:

```text
RuntimeResult.Failure
  -> ICodRuntimeAdmissionBridge.TranslateResponse
  -> ICodRuntimeAdmissionBridge.TranslateError
  -> CodRuntimeAdmissionOutcome(Status = Rejected)
```

No additional application interpretation is added.

## Registration

COD Runtime 2 uses existing services only.

`Program.cs` already registers payment foundations that provide
`PaymentRuntimeOrchestrator`. COD registration remains:

```text
AddCodRuntimeAdmissionFoundation()
AddCodRuntimeAdmissionApplicationContract()
AddCodRuntimeAdmissionBridge()
```

No Runtime registration was added or changed by COD Runtime 2.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodRuntimeAdmissionApplicationServiceCertificationTests
PointOfSale.Tests.Architecture.CodRuntimeAdmissionApplicationContractCertificationTests
PointOfSale.Tests.Architecture.CodRuntimeAdmissionBridgeCertificationTests
```

The tests certify:

- Runtime invocation occurs after bridge request translation.
- Bridge translation precedes Runtime execution.
- Runtime response is translated to canonical COD admission outcome.
- Runtime failure is translated to canonical COD admission rejection.
- Runtime source has no COD dependency.
- COD Application Service has no lifecycle, CommerceHub lifecycle, settlement,
  persistence, controller, or `PaymentService` dependency.
- Bridge remains translation-only.

## Certification Decision

| Question | Decision |
|---|---|
| Runtime execution implemented? | Yes |
| Runtime invocation certified? | Yes |
| Execution flow certified? | Yes |
| Response translation certified? | Yes |
| Failure translation certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Runtime immutability preserved? | Yes |
| Execution authority preserved? | Yes |
| Lifecycle mutation certified? | No |
| Settlement certified? | No |
| Evidence execution ownership certified? | No |
| Recommend COD-LIFECYCLE-1? | Yes, only as the next bounded phase after Runtime execution certification |

## Final Statement

Runtime executes.

Bridge translates.

Application coordinates.

Lifecycle decides later.

COD adapts to Runtime. Runtime never adapts to COD.
