# MANUAL-CONTRACT-CLOSURE-2 Evidence Integration Contract

Date: 2026-07-16  
Scope: Manual Payment evidence admission Application Contract only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and frozen

## Executive Summary

MANUAL-CONTRACT-CLOSURE-2 implements the application-owned boundary for preparing
Manual Payment evidence admission.

The new boundary resolves the current tenant Manual Payment configuration,
validates evidence-admission capability and environment, validates incoming proof
request shape, delegates evidence translation to `ManualPaymentEvidenceAdapter`,
and delegates Runtime execution to `ManualPaymentRuntimeBridge`.

This phase does not implement proof upload, approval, review, replay, recovery,
checkpoint persistence, webhook, callback, controller, frontend, SQL migration,
persistence, or lifecycle mutation.

## Application Boundary

The implemented boundary is:

```text
Trusted caller
  -> ManualPaymentEvidenceApplicationService
  -> ManualPaymentTenantConfigurationResolver
  -> ManualPaymentEvidenceAdapter
  -> ProviderEvidence
  -> ManualPaymentRuntimeBridge
  -> PaymentRuntimeOrchestrator
```

`ManualPaymentEvidenceApplicationService` owns request validation, current tenant
configuration resolution, capability admission, environment admission, evidence
adapter delegation, Runtime Bridge delegation, exact Runtime outcome admission,
result projection, and public application failure mapping.

It does not own durable payment lifecycle writes, proof transfer, approval,
review, retry, recovery, replay, checkpoint storage, webhook, callback,
controller behavior, frontend behavior, provider translation, or direct Runtime
invocation.

## Evidence Admission Flow

```text
Trusted application request
  -> validate tenant/payment/action/proof request shape
  -> resolve current tenant Manual Payment configuration
  -> require EvidenceAdmission capability
  -> require environment compatibility
  -> translate request facts through ManualPaymentEvidenceAdapter
  -> reject missing proof, missing required reference, unsupported channel, or
     provider-configuration mismatch before Runtime execution
  -> pass accepted ProviderEvidence to ManualPaymentRuntimeBridge
  -> require exact evidence-admission Runtime outcome
```

Accepted evidence remains evidence only. The application does not approve a
payment, start a review workflow, mutate lifecycle state, or persist evidence.

## Tenant Resolution

Tenant resolution reuses `ManualPaymentTenantConfigurationResolver` and the
current tenant-scoped `TenantPaymentMethods` row for `MANUALWALLET` compatibility.

Certified fail-closed behavior:

| Condition | Result |
|---|---|
| Missing tenant configuration | `MANUAL_PAYMENT_CONFIGURATION_MISSING` |
| Deleted tenant configuration | `MANUAL_PAYMENT_CONFIGURATION_DELETED` |
| Disabled tenant configuration | `MANUAL_PAYMENT_PROVIDER_DISABLED` |
| Tenant method lacks offline capability | `MANUAL_PAYMENT_CAPABILITY_DISABLED` |
| Sandbox/production mismatch | `MANUAL_PAYMENT_ENVIRONMENT_MISMATCH` |
| Invalid provider options | `MANUAL_PAYMENT_CONFIGURATION_INVALID` |
| Unsupported requested channel | `MANUAL_PAYMENT_CHANNEL_UNSUPPORTED` |

No global tenant fallback, global credential fallback, or provider-option fallback
is introduced. Manual Payment still has no external credential handoff in this
phase.

## Capability Validation

The Application Contract validates only the capability required for evidence
admission:

```text
ManualPaymentOperationalCapability.EvidenceAdmission
```

Required capability evidence:

- provider enabled and options valid;
- tenant-scoped configuration supported;
- evidence adapter certified;
- Runtime Bridge certified;
- instruction configuration supported;
- requested channel enabled and supported by capability flags;
- sandbox or production execution is compatible with feature flags and tenant row.

If evidence-adapter capability is disabled, the application rejects before
evidence adapter or Runtime Bridge delegation.

## Failure Mapping

Stable application failures are represented by `ManualPaymentApplicationException`.

Evidence rejection mapping:

| Evidence condition | Application code |
|---|---|
| Missing proof identifier or metadata | `MANUAL_PAYMENT_EVIDENCE_PROOF_MISSING` |
| Missing required reference number | `MANUAL_PAYMENT_EVIDENCE_REFERENCE_MISSING` |
| Unsupported channel | `MANUAL_PAYMENT_CHANNEL_UNSUPPORTED` |
| Evidence adapter configuration error | `MANUAL_PAYMENT_EVIDENCE_CONFIGURATION_INVALID` |
| Evidence adapter validation error | `MANUAL_PAYMENT_EVIDENCE_INVALID` |
| Runtime outcome mismatch | `MANUAL_PAYMENT_RUNTIME_OUTCOME_REJECTED` |

Provider-neutral `CanonicalProviderError` is mapped before it leaves the
application boundary. The mapping preserves safe provider code and retryability
without exposing proof content, provider wire objects, credentials, SQL state, or
Runtime internals.

## Regression

Focused certification tests were added in:

```text
PointOfSale.Tests/Architecture/ManualPaymentEvidenceApplicationContractCertificationTests.cs
```

Coverage includes:

- current tenant resolution;
- no global fallback;
- deleted, disabled, and capability-disabled tenant configuration rejection;
- environment mismatch rejection;
- missing proof metadata rejection;
- missing required reference rejection;
- evidence capability validation;
- application failure mapping;
- DTO compatibility;
- architecture guards for no SQL migration, persistence, lifecycle writer,
  controller, frontend, approval, review, replay, recovery, checkpoint, webhook,
  or callback activation;
- Runtime neutrality guards.

Certification commands:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentEvidenceApplicationContractCertificationTests -nologo -v minimal --no-restore
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ManualPaymentApplicationContractCertificationTests|FullyQualifiedName~ManualPaymentEvidenceAdapterCertificationTests|FullyQualifiedName~ManualPaymentRuntimeBridgeCertificationTests|FullyQualifiedName~ManualPaymentEvidenceApplicationContractCertificationTests" -nologo -v minimal --no-build
```

Observed result:

```text
Manual Payment evidence application contract: Passed 12, Failed 0, Skipped 0
Related Manual Payment contract/adapter/bridge slice: Passed 40, Failed 0, Skipped 0
```

## Certification Decision

| Question | Decision |
|---|---|
| Evidence Integration Contract implemented? | Yes |
| Tenant resolution certified? | Yes |
| Evidence validation certified? | Yes |
| Capability validation certified? | Yes |
| Failure mapping certified? | Yes |
| DTO compatibility preserved? | Yes |
| Runtime preserved? | Yes |
| Architecture preserved? | Yes |
| Recommend MANUAL-CONVERGENCE-1? | Yes |

## Final Verdict

MANUAL-CONTRACT-CLOSURE-2 is certified for Manual Payment evidence-admission
Application Contract only.

This phase activates only application orchestration for admitting Manual Payment
evidence into the certified evidence adapter and Runtime Bridge path. It does not
activate approval workflow, review workflow, replay, recovery, checkpoint,
webhook, callback, lifecycle mutation, controllers, frontend, SQL, or
persistence.

No new permanent cross-provider architecture truth was discovered. No
`.ai-context` update is required.
