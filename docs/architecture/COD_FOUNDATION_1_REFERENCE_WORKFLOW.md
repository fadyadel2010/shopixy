# COD Foundation 1 Reference Workflow

Date: 2026-07-16

Status: Implemented

Scope: Reference Workflow Foundation only.

## Executive Summary

COD Foundation 1 establishes the canonical Cash On Delivery reference workflow
identity accepted by `COD_ARCHITECTURE_DECISION_RECORD.md`.

This phase adds configuration, capability identity, authority contracts,
foundation registration, health checks, startup validation, and telemetry
definitions only.

It does not implement COD Runtime integration, evidence admission, workflow
application services, lifecycle mutation, settlement execution, controllers,
shipment behavior, CommerceHub behavior, replay, recovery, retention execution,
approval, or operational review execution.

## Scope

Implemented:

- Strongly typed COD Reference Workflow options.
- Disabled-by-default startup validation.
- Canonical capability model.
- Authority contract interfaces.
- Contract catalog registration.
- Foundation dependency injection registration with duplicate guard.
- Health contributor and health check.
- Telemetry meter, metric-name, activity-source, and event-name definitions.
- Focused architecture certification tests.

Excluded:

- Runtime Bridge.
- Provider Adapter.
- Evidence Adapter.
- Workflow Application Service.
- Settlement Service.
- Collection Service.
- Lifecycle Service.
- Controllers.
- PaymentService integration.
- Shipment, carrier, courier, driver, warehouse, treasury, and CommerceHub
  runtime behavior.

## Architectural Identity

COD remains a hybrid payment method, payment policy, and fulfillment-driven
collection reference workflow. It is not an external payment provider and is
not a Reference Provider.

The ADR remains authoritative. This phase preserves the accepted split:
standard COD payment truth belongs to `PaymentLifecycleService`, CommerceHub COD
payment truth belongs to `CommerceHubPaymentLifecycleAuthority`, evidence sources
produce evidence only, settlement is a separate authority, and Runtime remains
frozen.

## Implemented Foundation

Implementation files:

- `PointOfSale.API/Configuration/CodReferenceWorkflowOptions.cs`
- `PointOfSale.API/Services/Payments/Cod/CodFoundationModels.cs`
- `PointOfSale.API/Services/Payments/Cod/CodAuthorityContracts.cs`
- `PointOfSale.API/Services/Payments/Cod/CodFoundationService.cs`
- `PointOfSale.API/Services/Payments/Cod/CodFoundationServiceCollectionExtensions.cs`
- `PointOfSale.API/HealthChecks/CodReferenceWorkflowFoundationHealthCheck.cs`
- `PointOfSale.API/Instrumentation/CodReferenceWorkflowTelemetryDefinitions.cs`
- `PointOfSale.Tests/Architecture/CodReferenceWorkflowFoundationCertificationTests.cs`

`Program.cs` registers the foundation and health check only.

## Configuration Model

`CodReferenceWorkflowOptions` is bound from:

```text
Payments:CashOnDelivery:ReferenceWorkflow
```

The model includes:

- Enable/disable.
- Environment.
- Collection policy.
- Eligibility policy.
- Settlement policy.
- Evidence policy.
- Replay policy.
- Recovery policy.
- Retention policy.

Default configuration is disabled and valid. Enabling requires every canonical
policy to be explicitly configured. Execution flags for Runtime, evidence
admission, lifecycle mutation, settlement, replay, recovery, and retention fail
startup validation in this phase.

## Capability Model

`CodCapabilityModel.FoundationOnly` defines canonical capabilities:

- Payment method selection.
- Collection.
- Settlement.
- Evidence Admission.
- Replay.
- Recovery.
- Retention.
- Operational Review.
- Dispute.
- Reconciliation.

The model records capability identity only. Runtime integration, evidence
processing, lifecycle mutation, settlement execution, and workflow application
certification remain false.

## Authority Contracts

Interfaces only:

- `ICodEligibilityAuthority`
- `ICodCollectionAuthority`
- `ICodSettlementAuthority`
- `ICodEvidenceAuthority`
- `ICodReplayAuthority`
- `ICodRecoveryAuthority`
- `ICodRetentionAuthority`
- `ICodOperationalAuthority`

No authority implementation was created. `CodAuthorityContractCatalog` registers
contract identity for certification and health reporting only.

## Health

`CodReferenceWorkflowHealthContributor` evaluates configuration and verifies
disabled-by-default posture.

`CodReferenceWorkflowFoundationHealthCheck` reports:

- enabled state;
- disabled-by-default verification;
- workflow identity;
- non-provider posture;
- Runtime execution disabled;
- evidence admission disabled;
- lifecycle mutation disabled;
- settlement execution disabled;
- authority contract count.

The health check performs no storage, Runtime, payment lifecycle, shipment,
settlement, or CommerceHub calls.

## Telemetry

`CodReferenceWorkflowTelemetryDefinitions` registers definitions only:

- Meter: `PointOfSale.Payments.Cod.ReferenceWorkflow`
- Activity source: `PointOfSale.Payments.Cod.ReferenceWorkflow`
- Metric names for foundation, configuration, capability, contracts, and
  disabled-by-default verification.
- Event names for the same foundation concepts.

No event emission API or runtime metric recording was added.

## Regression

Focused certification coverage:

```text
PointOfSale.Tests.Architecture.CodReferenceWorkflowFoundationCertificationTests
```

The tests certify:

- Configuration binding.
- Disabled-by-default behavior.
- Startup/configuration validation.
- Canonical capability model.
- Interface-only authority contracts.
- Foundation DI registration.
- Duplicate registration rejection.
- Health contributor/check behavior.
- Telemetry definitions.
- Runtime freeze preservation.
- No Runtime Bridge, Provider Adapter, Evidence Adapter, workflow application
  service, settlement service, collection service, lifecycle service,
  controller, `DbContext`, or payment lifecycle coupling in the foundation
  source.

## Certification Decision

| Question | Decision |
|---|---|
| Reference Workflow Foundation implemented? | Yes |
| Configuration certified? | Yes |
| Capability model certified? | Yes |
| Authority contracts certified? | Yes |
| Foundation registration certified? | Yes |
| Health certified? | Yes |
| Telemetry definitions certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Reference Provider certified? | No |
| Runtime Integration certified? | No |
| Evidence processing certified? | No |
| Settlement certified? | No |
| Recommend COD-FOUNDATION-2? | Yes, for the next ADR-bounded foundation phase only |

## Final Statement

COD remains Reference Workflow, not Reference Provider. `PAYMENT_REFERENCE_FREEZE`
and the frozen Payment Runtime remain unchanged.
