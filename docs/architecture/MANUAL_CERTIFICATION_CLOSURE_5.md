# Manual Certification Closure 5

## Executive Summary

Manual Payment Closure 5 addresses only MANUAL-FINAL-005.

The correction establishes `IManualPaymentRetentionAuthority` as the canonical
Manual Payment retention authority. Manual proof retention is now executable
application governance instead of hard-coded lifecycle/controller behavior.

## Board Finding

MANUAL-FINAL-005 was valid.

Manual Payment had retention fields and cleanup behavior, but configured proof
retention was not the single execution authority. Access, terminal retention,
and cleanup could interpret retention locally.

## Root Cause

Retention policy existed in configuration, while proof expiry was assigned by
hard-coded lifecycle durations and proof access only checked storage presence.
Cleanup deleted rows whose expiry had elapsed, but no shared authority classified
the artifact or validated the policy before access or deletion.

## Implemented Correction

`ManualPaymentRetentionAuthority` now:

- validates that Manual Payment retention policy is configured and positive;
- classifies supported proof artifact classes;
- computes retention deadlines from configured `ProofAccessTtl`;
- rejects missing policy, invalid policy, unsupported artifact classes, deleted
  proofs, expired proofs, and conflicting retention deadlines;
- returns executable retain/expire decisions for admission, access, and cleanup.

## Retention Authority

The canonical flow is:

```text
Retention Evaluation
  -> IManualPaymentRetentionAuthority
  -> Classify Artifact
  -> Retain / Expire / Reject Operation
  -> Audit Outcome through existing caller logs and lifecycle result
```

The authority is application-owned. Runtime, Runtime Bridge, Provider Adapter,
and Evidence Adapter do not participate.

## Policy Enforcement

Manual proof deadlines are now supplied to `PaymentLifecycleService` through
explicit lifecycle request fields. The lifecycle writer rejects proof-bearing
Manual Wallet creation, review admission, approval, and rejection when retention
authority is missing or invalid.

Admin proof access calls retention authority before signing a storage URL.
Cleanup calls retention authority before deleting an expired proof object.

## Regression

Focused certification tests were added in
`ManualPaymentCertificationClosure5Tests`.

Coverage includes:

- retention authority;
- configured policy enforcement;
- missing and invalid policy rejection;
- protected artifact retention;
- expired artifact access denial and cleanup expiration;
- unsupported/deleted artifact rejection;
- architecture guards for controller ownership and Runtime/adapters.

## Certification Decision

MANUAL-FINAL-005 is closed.

No implementation was added for MANUAL-FINAL-006, 007, or 009.

Runtime remains provider-neutral and frozen. Runtime Bridge, Provider Adapter,
Evidence Adapter, SQL schema, recovery, replay, telemetry, migration, sandbox,
production, webhook, callback, and lifecycle redesign remain unchanged.
