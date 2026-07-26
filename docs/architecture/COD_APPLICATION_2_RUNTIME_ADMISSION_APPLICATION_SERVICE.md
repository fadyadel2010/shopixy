# COD Application 2 Runtime Admission Application Service

Date: 2026-07-17

Status: Implemented

Scope: Coordinating Runtime Admission Application Service only.

## Executive Summary

COD Application 2 introduces the coordinating application service boundary for
canonical COD Runtime Admission preparation.

The service coordinates application-owned preparation, application validation,
Runtime Admission Bridge request translation, and canonical application result
return. It does not execute Runtime, own Runtime orchestration, mutate
lifecycle state, admit evidence, settle money, persist state, call controllers,
or integrate shipment, carrier, CommerceHub, or `PaymentService` paths.

The Runtime remains frozen. COD adapts to Runtime. Runtime never adapts to COD.

## Scope

Implemented:

- `CodRuntimeAdmissionApplicationService` coordination.
- `ICodRuntimeAdmissionApplicationService.CoordinateAdmissionAsync(...)`.
- Preparation before validation result return.
- Validation short-circuit before bridge delegation.
- Bridge request-translation delegation after successful preparation.
- Canonical result wrapper containing only `CodRuntimeAdmissionPreparationResult`
  or `CodRuntimeAdmissionOutcome`.
- Dependency injection certification through the existing application contract
  registration.
- Focused architecture certification tests.

Excluded:

- `PaymentRuntimeOrchestrator` changes or invocation.
- Runtime execution ownership.
- Runtime modifications.
- Lifecycle mutation.
- Settlement.
- Persistence.
- Evidence admission execution.
- Controllers.
- `PaymentService`.
- Carrier, shipment, CommerceHub, replay, recovery, and retention integration.

## Application Service Responsibilities

`CodRuntimeAdmissionApplicationService` owns coordination only.

It may:

- receive a canonical `CodRuntimeAdmissionRequest`;
- prepare admission through the Application 1 preparation contract;
- apply application-owned validation rules;
- stop before bridge delegation when preparation is invalid;
- delegate successful prepared requests to `ICodRuntimeAdmissionBridge` for
  canonical Runtime request translation;
- return canonical COD admission application result material.

It must not:

- invoke `PaymentRuntimeOrchestrator`;
- call Runtime stages;
- mutate payment lifecycle;
- settle COD money;
- admit evidence as executable payment truth;
- persist state;
- call controllers or payment services;
- own business workflow decisions.

## Pipeline Coordination

Certified pipeline:

```text
CodRuntimeAdmissionRequest
  -> PrepareAdmissionAsync
  -> CodRuntimeAdmissionApplicationValidation
  -> ICodRuntimeAdmissionBridge.TranslateRequest
  -> CodRuntimeAdmissionApplicationResult
       containing CodRuntimeAdmissionPreparationResult or CodRuntimeAdmissionOutcome only
```

Invalid application-owned input returns a
`CodRuntimeAdmissionPreparationResult` with validation issues and does not
delegate to the bridge.

Successful preparation delegates to the bridge for request translation only.
The application service does not run the translated Runtime request.

## Delegation

The Application Service delegates bridge-owned translation to:

```text
ICodRuntimeAdmissionBridge.TranslateRequest(...)
```

The bridge remains responsible for translating COD admission requests into
canonical Runtime object shapes. The application service does not inspect or
return Runtime object internals.

## Registration

Registration remains through:

```text
AddCodRuntimeAdmissionApplicationContract()
```

The application service is scoped and duplicate registration is rejected.

`Program.cs` preserves registration order:

```text
AddCodRuntimeAdmissionFoundation()
AddCodRuntimeAdmissionApplicationContract()
AddCodRuntimeAdmissionBridge()
```

This registration does not enable Runtime execution, lifecycle mutation,
settlement, evidence admission, replay, recovery, or retention.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodRuntimeAdmissionApplicationServiceCertificationTests
```

The tests certify:

- Application Service coordination exists.
- Pipeline order is preserved.
- Preparation is called.
- Validation is called.
- Invalid requests short-circuit before bridge translation.
- Bridge delegation is called for prepared requests.
- Runtime ownership is absent.
- Lifecycle dependency is absent.
- Settlement dependency is absent.
- Duplicate registration is rejected.
- Program registration order is preserved.
- Architecture guards preserve application coordination purity.

## Certification Decision

| Question | Decision |
|---|---|
| Application Service implemented? | Yes |
| Pipeline coordination certified? | Yes |
| Validation coordination certified? | Yes |
| Bridge delegation certified? | Yes |
| Registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Application coordination purity preserved? | Yes |
| Runtime execution certified? | No |
| Lifecycle mutation certified? | No |
| Settlement certified? | No |
| Evidence admission certified? | No |
| Recommend COD-RUNTIME-2? | Yes, only for the next ADR-bounded Runtime phase |

## Final Statement

The Application Service coordinates.

The Bridge translates.

The Runtime executes.

The Lifecycle decides.

No responsibilities are merged in COD Application 2.
