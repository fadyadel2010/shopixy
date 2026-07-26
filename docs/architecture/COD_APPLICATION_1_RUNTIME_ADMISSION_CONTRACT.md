# COD Application 1 Runtime Admission Contract

Date: 2026-07-17

Status: Implemented

Scope: Application-owned Runtime Admission Contract only.

## Executive Summary

COD Application 1 establishes the Application-owned contract used to prepare
Runtime Admission requests for the COD Reference Workflow.

This phase prepares canonical admission requests, validates only
application-owned invariants, and delegates only to the inert Runtime Admission
Foundation catalog.

Runtime execution remains disabled. Runtime admission remains disabled. Runtime
Bridge, lifecycle mutation, payment completion, settlement, replay, recovery,
retention, evidence admission, controllers, shipment integration, carrier
integration, CommerceHub integration, and PaymentService changes remain out of
scope.

## Scope

Implemented:

- Application admission preparation contract.
- Canonical preparation result DTO.
- Application metadata and context DTOs.
- Application-owned validation rules.
- Delegation to `ICodRuntimeAdmissionCatalog`.
- Dependency injection registration with duplicate guard.
- Focused architecture certification tests.

Excluded:

- Runtime execution.
- Runtime admission.
- Runtime Bridge.
- Payment Runtime modification.
- Evidence Adapter.
- Evidence admission.
- Lifecycle mutation.
- Payment completion.
- Settlement.
- Replay, recovery, and retention.
- Controllers.
- Workflow, carrier, shipment, CommerceHub, and PaymentService integration.

## Application Contract

Implementation file:

- `PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionApplicationService.cs`

Contract:

- `ICodRuntimeAdmissionApplicationService`

The contract exposes only:

```text
PrepareAdmissionAsync(CodRuntimeAdmissionRequest)
```

The contract prepares an application-level admission result. It does not admit
evidence, invoke Runtime, call a Runtime Bridge, mutate lifecycle, complete
payment, settle money, or write persistence.

## DTOs

Application DTOs added:

- `CodRuntimeAdmissionContext`
- `CodRuntimeAdmissionMetadata`
- `CodRuntimeAdmissionPreparationMetadata`
- `CodRuntimeAdmissionValidationIssue`
- `CodRuntimeAdmissionPreparationResult`

The contract consumes the Foundation 3 boundary request:

- `CodRuntimeAdmissionRequest`

These DTOs are boundary shapes only. They contain no Runtime execution,
admission, lifecycle, payment completion, settlement, persistence, controller,
or integration behavior.

## Validation

Application validation is limited to application-owned invariants:

- Tenant present.
- Environment present.
- Evidence identity present.
- Evidence type present.
- Evidence source present.
- Payment identity present.
- Collection identity present.
- At least one correlation, request, or trace identity present.
- Evidence metadata present.
- Source reference present.
- Evidence version present.

Validation does not perform Runtime validation, business validation, lifecycle
validation, settlement validation, payment completion validation, carrier
validation, shipment validation, CommerceHub validation, or persistence
validation.

## Foundation Delegation

The application boundary delegates only to:

```text
ICodRuntimeAdmissionCatalog
```

The delegation reads canonical outcome definitions and disabled posture from
the Foundation boundary. It does not invoke `PaymentRuntimeOrchestrator`, a COD
Runtime Bridge, a lifecycle writer, a settlement service, a controller, a
provider adapter, an evidence adapter, or persistence.

## Registration

Registration:

```text
AddCodRuntimeAdmissionApplicationContract()
```

`Program.cs` registers the application contract after the Runtime Admission
Foundation.

Registration is disabled-by-default in effect because it exposes preparation
only and preserves Foundation disabled posture:

- Runtime execution disabled.
- Runtime modification disabled.
- Evidence admission disabled.
- Lifecycle mutation disabled.
- Settlement disabled.

Duplicate registration is rejected.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodRuntimeAdmissionApplicationContractCertificationTests
```

The tests certify:

- Application DTOs exist.
- Application contract exists.
- Preparation returns canonical admission metadata.
- Application-owned validation rules reject missing invariants.
- Delegation targets the Foundation catalog only.
- Duplicate registration is rejected.
- Program registration exists.
- Architecture guards preserve no Runtime execution, no Runtime Bridge, no
  PaymentService coupling, no lifecycle coupling, no settlement service, no
  controller, and no persistence coupling.

## Certification Decision

| Question | Decision |
|---|---|
| Runtime Admission Application Contract implemented? | Yes |
| Application DTOs certified? | Yes |
| Application contract certified? | Yes |
| Application validation certified? | Yes |
| Foundation delegation certified? | Yes |
| Registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Runtime execution certified? | No |
| Runtime admission certified? | No |
| Runtime Bridge certified? | No |
| Lifecycle mutation certified? | No |
| Payment completion certified? | No |
| Settlement certified? | No |
| Recommend COD-APPLICATION-2? | Yes, only for the next ADR-bounded Application phase |

## Final Statement

The Application prepares. The Foundation defines. The Runtime executes later.

No boundary is mixed in this phase.
