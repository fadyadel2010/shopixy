# COD Runtime 1 Runtime Admission Bridge

Date: 2026-07-17

Status: Implemented

Scope: Canonical Runtime Admission Bridge only.

## Executive Summary

COD Runtime 1 creates the application-owned bridge between the COD Application
Runtime Admission Contract and the frozen provider-neutral Payment Runtime
object model.

The bridge translates COD admission requests into canonical Runtime request
objects and translates Runtime results or failures back into COD admission
responses.

Runtime remains frozen. The bridge adapts COD to Runtime; Runtime does not
adapt to COD.

## Scope

Implemented:

- `ICodRuntimeAdmissionBridge`
- `CodRuntimeAdmissionBridge`
- COD admission request to canonical Runtime request translation
- Runtime result to COD admission response translation
- Runtime failure to COD admission failure translation
- Dependency injection registration with duplicate guard
- Focused certification tests

Excluded:

- Runtime modification
- Runtime execution ownership
- `PaymentRuntimeOrchestrator` invocation in bridge source
- Payment lifecycle mutation
- Settlement
- Replay, recovery, and retention
- Evidence admission logic
- Business validation
- Shipment, carrier, CommerceHub, controller, and `PaymentService` integration

## Bridge Responsibilities

The bridge owns translation only.

It maps the application admission boundary into canonical Runtime objects:

- `RuntimeExecutionContext`
- `PaymentSession`
- `PaymentAction`
- `ProviderEvidence`

It maps Runtime outputs back into:

- `CodRuntimeAdmissionOutcome`

It does not call Runtime stages, invoke the orchestrator, write persistence,
perform lifecycle mutation, settle money, admit evidence, or route workflow.

## Request Translation

`CodRuntimeAdmissionBridge.TranslateRequest(...)` maps:

```text
CodRuntimeAdmissionRequest
-> RuntimeExecutionContext
-> PaymentSession
-> PaymentAction
-> ProviderEvidence
```

The mapping preserves admission identity, correlation, evidence identity,
evidence source, collection reference, requested outcome, admission timestamp,
and metadata.

Runtime identifiers are deterministically derived from canonical application
identity strings. This keeps translation stable without adding persistence,
random identity generation, or Runtime behavior.

## Response Translation

`CodRuntimeAdmissionBridge.TranslateResponse(...)` maps:

```text
RuntimeResult
-> CodRuntimeAdmissionOutcome
```

Runtime success becomes a canonical accepted admission response. Runtime failure
delegates to error translation. The bridge does not interpret lifecycle meaning,
route decisions, or mutate payment state.

## Error Translation

`CodRuntimeAdmissionBridge.TranslateError(...)` maps:

```text
RuntimeFailure
-> CodRuntimeAdmissionOutcome(Status = Rejected)
```

The failure stage, failure reason, and Runtime failure metadata are preserved as
COD admission metadata. The bridge does not invent Runtime behavior or convert a
failure into business workflow.

## Registration

Registration:

```text
AddCodRuntimeAdmissionBridge()
```

The registration exposes `ICodRuntimeAdmissionBridge` as scoped and rejects
duplicate registration.

`Program.cs` registers the bridge after:

```text
AddCodRuntimeAdmissionApplicationContract()
```

The bridge registration does not enable Runtime execution, lifecycle mutation,
settlement, evidence admission, replay, recovery, or retention.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodRuntimeAdmissionBridgeCertificationTests
```

The tests certify:

- Bridge exists.
- Request translation maps into canonical Runtime request objects.
- Response translation maps Runtime success to canonical admission acceptance.
- Error translation maps Runtime failure to canonical admission rejection.
- Duplicate bridge registration is rejected.
- Program registration exists in the expected order.
- The bridge source has no lifecycle, settlement, persistence, controller,
  `PaymentService`, or `PaymentRuntimeOrchestrator` dependency.
- Runtime source has no COD bridge dependency.

## Certification Decision

| Question | Decision |
|---|---|
| Runtime Admission Bridge implemented? | Yes |
| Request translation certified? | Yes |
| Response translation certified? | Yes |
| Error translation certified? | Yes |
| Bridge registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Runtime immutability preserved? | Yes |
| Bridge purity certified? | Yes |
| Lifecycle mutation certified? | No |
| Settlement certified? | No |
| Evidence admission certified? | No |
| Runtime execution certified by bridge source? | No |
| Recommend COD-APPLICATION-2? | Yes, only for the next ADR-bounded Application phase |

## Final Statement

The Bridge translates. The Runtime executes. The Lifecycle decides.

The Runtime remains frozen.
