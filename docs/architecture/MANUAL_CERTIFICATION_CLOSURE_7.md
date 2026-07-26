# Manual Certification Closure 7

## Executive Summary

Manual Payment Closure 7 addresses only MANUAL-FINAL-007.

The correction establishes `IManualPaymentOperationalTelemetryAuthority` as the
canonical Manual Payment operational telemetry authority. Manual Payment
application-owned operations now emit canonical low-cardinality telemetry
through one authority before metrics are recorded.

## Board Finding

MANUAL-FINAL-007 was valid.

Operational telemetry definitions existed, but execution paths did not emit
through one governed authority. That allowed future incomplete, inconsistent,
or locally emitted telemetry.

## Root Cause

Telemetry was implemented as metric definitions and recorder methods. It did
not own canonical event admission, event schema validation, duplicate emission
suppression, or the boundary rule that only Manual Payment application
authorities may emit operational events.

## Implemented Correction

`ManualPaymentOperationalTelemetry` now implements
`IManualPaymentOperationalTelemetryAuthority`.

The authority:

- admits only canonical Manual Payment operational event names;
- validates required schema fields before metric emission;
- maps canonical events to the existing low-cardinality metrics;
- suppresses duplicate emission by stable operation emission key;
- keeps recorder methods private so production callers use `Emit(...)`;
- is registered once by the Manual Payment foundation.

## Telemetry Authority

Certified flow:

```text
Manual Payment application operation
  -> IManualPaymentOperationalTelemetryAuthority
  -> canonical telemetry event
  -> metric counter
  -> OpenTelemetry meter registration
  -> audit/runbook correlation through bounded tags and reason codes
```

Controllers do not own telemetry. Runtime, Runtime Bridge, Provider Adapter,
Evidence Adapter, SQL schema, lifecycle, retention, replay, recovery, sandbox,
and production deployment behavior remain unchanged.

## Canonical Event Model

Canonical event names:

- `ManualPayment.Initialized`
- `ManualPayment.EvidenceSubmitted`
- `ManualPayment.RuntimeRejected`
- `ManualPayment.ConfigurationRejected`
- `ManualPayment.CapabilityRejected`
- `ManualPayment.ChannelRejected`
- `ManualPayment.EnvironmentRejected`

Required event schema:

- canonical event name;
- trusted tenant id, emitted only as `tenant_bucket` metric tag;
- bounded operation name;
- stable operation id for duplicate suppression, not metric tagging;
- event-specific bounded fields: `channel`, `outcome`, `capability`, or
  `reason_code`.

Unknown events reject. Invalid schemas reject. Duplicate emission suppresses.
Unsupported operation emission rejects through schema or capability failure.

## Regression

Focused certification tests were added in
`ManualPaymentCertificationClosure7Tests`.

Coverage includes:

- canonical telemetry authority;
- canonical event names and stable schema;
- MeterListener-backed canonical metric emission;
- duplicate suppression;
- unknown event rejection;
- invalid schema rejection;
- architecture guards proving no controller-owned telemetry;
- architecture guards proving Runtime, Runtime Bridge, Provider Adapter, and
  Evidence Adapter do not participate.

## Certification Decision

MANUAL-FINAL-007 is closed.

No implementation was added for MANUAL-FINAL-009.

Runtime remains provider-neutral and frozen. Runtime Bridge, Provider Adapter,
Evidence Adapter, SQL schema, lifecycle redesign, recovery redesign, retention
redesign, replay redesign, migration, sandbox, production, and business-rule
behavior remain unchanged.
