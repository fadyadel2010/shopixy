# COD Foundation 2 Collection Evidence

Date: 2026-07-17

Status: Implemented

Scope: Collection Evidence Foundation only.

## Executive Summary

COD Foundation 2 establishes the canonical Collection Evidence model accepted
by `COD_ARCHITECTURE_DECISION_RECORD.md`.

This phase defines identity, contracts, source catalog, evidence type catalog,
metadata, correlation, version, integrity, status, validation-rule definitions,
and disabled-by-default registration only.

It does not implement evidence admission, Runtime integration, Runtime Bridge,
evidence adapters, lifecycle mutation, payment completion, settlement, replay,
recovery, retention, workflow services, controllers, carrier integration,
driver integration, POS Admin integration, or CommerceHub integration.

## Scope

Implemented:

- Strongly typed COD collection evidence records.
- Canonical evidence source definitions.
- Canonical evidence type definitions.
- Interface-only evidence contracts.
- Validation-rule definitions.
- Foundation dependency injection registration with duplicate guard.
- Focused architecture certification tests.

Excluded:

- Evidence Adapter.
- Runtime.
- Runtime Bridge.
- Evidence Admission.
- Lifecycle.
- Settlement.
- Replay.
- Recovery.
- Retention.
- Workflow Service.
- Controllers.
- Carrier, driver, POS Admin, or CommerceHub integration.

## Evidence Model

Implementation files:

- `PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceContracts.cs`
- `PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceModels.cs`
- `PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceCatalog.cs`

The model defines:

- `CodCollectionId`
- `CodEvidenceId`
- `CodCollectionEvidenceSource`
- `CodCollectionEvidenceType`
- `CodCollectionAmount`
- `CodCurrency`
- `CollectedAt`
- `RecordedAt`
- `CodEvidenceReference`
- `CodCollectionEvidenceMetadata`
- `CodEvidenceCorrelation`
- `CodEvidenceVersion`
- `CodEvidenceIntegrity`
- `CodCollectionEvidenceStatus`

Collection evidence is evidence. It is not payment completion.

## Evidence Sources

Canonical sources:

- Driver
- Courier
- Carrier
- POS Admin
- CommerceHub
- Manual Entry

Every source is registered as an evidence source and explicitly rejects payment
provider or lifecycle-writer authority.

## Evidence Types

Canonical evidence categories:

- Cash Collection
- Collection Attempt
- Collection Failure
- Partial Collection
- Customer Refusal
- Carrier Confirmation
- Admin Confirmation
- Dispute
- Reconciliation Marker

These are definitions only. They do not admit evidence and do not mutate state.

## Evidence Contracts

Interface-only contracts:

- `ICodCollectionEvidence`
- `ICodEvidenceSource`
- `ICodEvidenceMetadata`
- `ICodEvidenceValidationContract`

No implementation was created for evidence admission or validation execution.

## Validation Model

Validation-rule definitions:

- Required Amount
- Required Currency
- Required Evidence Source
- Required Reference
- Required Timestamp
- Required Integrity Marker
- Required Collection Identity

These are catalog rules only. No validator, execution pipeline, Runtime
admission, SQL persistence, or lifecycle mutation was introduced.

## Registration

`AddCodCollectionEvidenceFoundation()` registers
`ICodCollectionEvidenceCatalog` and rejects duplicate registration.

The registration exposes disabled state for admission, Runtime integration,
lifecycle mutation, and settlement.

`Program.cs` registers the foundation catalog only.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodCollectionEvidenceFoundationCertificationTests
```

The tests certify:

- Evidence model exists.
- Evidence source catalog exists.
- Evidence type catalog exists.
- Evidence contracts exist and are interface-only.
- Validation model exists as definitions only.
- DI registration works.
- Duplicate registration is rejected.
- Architecture guards preserve no Runtime, bridge, adapter, admission,
  lifecycle, settlement, controller, or `DbContext` coupling.

## Certification Decision

| Question | Decision |
|---|---|
| Collection Evidence Foundation implemented? | Yes |
| Evidence model certified? | Yes |
| Evidence sources certified? | Yes |
| Evidence types certified? | Yes |
| Evidence contracts certified? | Yes |
| Validation model certified? | Yes |
| Foundation registration certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Evidence admission certified? | No |
| Payment completion certified? | No |
| Settlement certified? | No |
| Recommend COD-FOUNDATION-3? | Yes, for the next ADR-bounded foundation phase only |

## Final Statement

Collection Evidence is not Payment Completion. Evidence Sources are not Payment
Authorities. Evidence creation does not mutate lifecycle.

`PAYMENT_REFERENCE_FREEZE` remains frozen. COD remains Reference Workflow.
