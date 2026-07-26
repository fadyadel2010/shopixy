# COD Foundation 3 Runtime Admission

Date: 2026-07-17

Status: Implemented

Scope: Runtime Admission Foundation only.

## Executive Summary

COD Foundation 3 establishes the canonical Runtime Admission Boundary for the
COD Reference Workflow.

This phase defines admission contracts, request identity, metadata, immutable
runtime context, outcome definitions, and disabled-by-default registration only.

It does not implement Runtime execution, Runtime modification, Runtime Bridge,
evidence admission, lifecycle mutation, payment completion, settlement, replay,
recovery, retention, workflow services, controllers, `PaymentService`, or
payment lifecycle integration.

## Scope

Implemented:

- Strongly typed admission request model.
- Canonical admission outcome model.
- Interface-only admission contracts.
- Immutable runtime context model.
- Foundation dependency injection registration with duplicate guard.
- Focused architecture certification tests.

Excluded:

- Runtime Bridge.
- Runtime execution.
- Payment Runtime modification.
- Evidence Admission.
- Evidence Adapter.
- Lifecycle.
- Settlement.
- Replay.
- Recovery.
- Retention.
- Workflow Service.
- Controllers.
- `PaymentService`.
- Payment lifecycle services.
- Runtime orchestrator modification.

## Admission Request

Implementation files:

- `PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionContracts.cs`
- `PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionModels.cs`
- `PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionCatalog.cs`

The request model defines:

- Admission Id
- Correlation
- Evidence Identity
- Evidence Version
- Evidence Metadata
- Collection Reference
- Requested Outcome
- Admission Timestamp
- Runtime Context

The request is a canonical boundary object only. It does not execute Runtime.

## Admission Outcomes

Canonical outcome definitions:

- Accepted
- Rejected
- Deferred
- Incomplete
- Unsupported

These are definitions only. No outcome processing, lifecycle routing, payment
completion, settlement, or Runtime result interpretation is implemented.

## Admission Contracts

Interface-only contracts:

- `ICodRuntimeAdmission`
- `ICodAdmissionRequest`
- `ICodAdmissionOutcome`
- `ICodAdmissionMetadata`
- `ICodAdmissionContext`

No admission implementation was created.

## Admission Context

`CodAdmissionRuntimeContext` is immutable and provider-neutral. It carries:

- Tenant
- Environment
- Payment Identity
- Evidence Identity
- Correlation

The context has no Runtime, provider, persistence, lifecycle, or settlement
coupling.

## Registration

`AddCodRuntimeAdmissionFoundation()` registers `ICodRuntimeAdmissionCatalog` and
rejects duplicate registration.

The catalog reports disabled-by-default posture:

- Runtime execution disabled.
- Runtime modification disabled.
- Evidence admission disabled.
- Lifecycle mutation disabled.
- Settlement disabled.

`Program.cs` registers the foundation catalog only.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodRuntimeAdmissionFoundationCertificationTests
```

The tests certify:

- Admission request model exists.
- Outcome model exists.
- Contracts exist and are interface-only.
- Admission context exists and is immutable.
- DI registration works.
- Duplicate registration is rejected.
- Architecture guards preserve no Runtime execution, Runtime Bridge, evidence
  adapter, evidence admission, lifecycle, settlement, controller, or persistence
  coupling.

## Certification Decision

| Question | Decision |
|---|---|
| Runtime Admission Foundation implemented? | Yes |
| Admission request certified? | Yes |
| Admission outcome certified? | Yes |
| Admission contracts certified? | Yes |
| Admission context certified? | Yes |
| Foundation registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Runtime immutability preserved? | Yes |
| Runtime execution certified? | No |
| Evidence admission certified? | No |
| Payment completion certified? | No |
| Settlement certified? | No |
| Recommend COD-CONTRACT-1? | Yes, for the next ADR-bounded contract phase only |

## Final Statement

The Runtime remains frozen. COD adapts to Runtime. Never the reverse.
