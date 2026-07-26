# Manual Certification Closure 2

## Executive Summary

Manual Payment Closure 2 addresses only MANUAL-FINAL-002.

The correction establishes `IManualPaymentCapabilityAdmissionAuthority` as the
canonical Manual Payment capability admission authority. Active Manual Payment
ingress now requires current tenant configuration, environment validation,
provider enablement, operation capability, and certification gates before
execution can continue.

## Board Finding

MANUAL-FINAL-002 was valid. Declared feature flags, certification flags, and
capability configuration existed, but some active controller ingress did not
consume them as execution authority.

## Root Cause

Capability validation existed inside the Manual Payment application resolver,
but proof upload, proof clear, pending-review reads, and proof access were
controller-owned ingress paths that could proceed without the same admission
authority.

That made capability configuration partly documentary instead of uniformly
authoritative.

## Implemented Correction

`ManualPaymentTenantConfigurationResolver` now also implements
`IManualPaymentCapabilityAdmissionAuthority`.

The authority admits only a current tenant-scoped `MANUALWALLET` configuration
that is enabled, not deleted, offline-capable, environment-compatible, and
operation-certified.

The following active operations are admitted through this authority:

- reference-payment initialization;
- evidence admission;
- operator approval;
- operator rejection;
- proof upload;
- proof clear;
- pending-review read;
- proof access.

## Capability Authority

The capability authority is fail-closed.

Disabled provider, disabled tenant capability, disabled feature flag, disabled
certification flag, missing tenant configuration, environment mismatch, and
unknown operation all reject before downstream execution.

No global fallback or provider-option fallback is introduced.

## Admission Flow

```text
Incoming Request
  -> Tenant Resolution
  -> Environment Validation
  -> Capability Admission
  -> ManualPaymentRuntimeApplicationService
     or ManualPaymentEvidenceApplicationService
     or ManualPaymentReviewApplicationService
     or bounded controller read/proof operation
  -> Runtime Bridge where applicable
  -> Payment Runtime where applicable
```

Controllers do not call the Manual Payment Runtime Bridge, provider adapter, or
Payment Runtime directly.

## Regression

Focused certification tests were added in
`ManualPaymentCertificationClosure2Tests`.

Coverage includes:

- capability enabled;
- current tenant only;
- disabled provider;
- disabled tenant capability;
- disabled certification gate;
- environment mismatch;
- unknown operation;
- controller admission guard;
- Runtime and controller bypass architecture guards.

## Certification Decision

MANUAL-FINAL-002 is closed.

No implementation was added for MANUAL-FINAL-003, 004, 005, 006, 007, or 009.

Runtime remains provider-neutral and frozen. Lifecycle behavior, SQL schema,
migrations, expiration, recovery, replay, retention, telemetry, sandbox,
production, webhook, and callback behavior are unchanged.
