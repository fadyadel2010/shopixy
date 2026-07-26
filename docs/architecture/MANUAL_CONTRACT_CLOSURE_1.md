# MANUAL-CONTRACT-CLOSURE-1 Application Contract

Date: 2026-07-16  
Scope: Manual Payment reference-payment initialization Application Contract only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and frozen

## Executive Summary

MANUAL-CONTRACT-CLOSURE-1 implements the application-owned boundary for preparing
Manual Payment reference-payment initialization.

The new boundary resolves the current tenant Manual Payment configuration,
validates provider capability and environment, maps application failures to stable
application error codes, prepares a `ManualPaymentExecutionRequest`, and delegates
to the certified `ManualPaymentRuntimeBridge`.

This phase does not implement proof upload, approval, review, replay, recovery,
checkpoint persistence, webhook, callback, controller, frontend, SQL migration, or
lifecycle mutation.

## Application Boundary

The implemented boundary is:

```text
Trusted caller
  -> ManualPaymentRuntimeApplicationService
  -> ManualPaymentTenantConfigurationResolver
  -> ManualPaymentRuntimeBridge
  -> ManualPaymentProviderAdapter
  -> PaymentRuntimeOrchestrator
```

`ManualPaymentRuntimeApplicationService` owns request validation, Runtime session
and action construction, canonical Manual Payment request construction, Runtime
outcome admission, result projection, and public application failure mapping.

It does not own durable payment lifecycle writes, proof transfer, approval,
review, retry, recovery, replay, checkpoint storage, webhook, callback, controller
behavior, frontend behavior, or provider translation.

## Tenant Resolution

Tenant resolution is implemented in `ManualPaymentTenantConfigurationResolver`.

Resolution uses the current tenant-scoped `TenantPaymentMethods` row for the
existing persisted Manual Wallet payment-method code `MANUALWALLET`. This
preserves shipped tenant configuration compatibility while the provider evidence
source and provider model remain `manual_payment`.

Certified fail-closed behavior:

| Condition | Result |
|---|---|
| Missing tenant configuration | `MANUAL_PAYMENT_CONFIGURATION_MISSING` |
| Deleted tenant configuration | `MANUAL_PAYMENT_CONFIGURATION_DELETED` |
| Disabled tenant configuration | `MANUAL_PAYMENT_PROVIDER_DISABLED` |
| Tenant method lacks offline capability | `MANUAL_PAYMENT_CAPABILITY_DISABLED` |
| Sandbox/production mismatch | `MANUAL_PAYMENT_ENVIRONMENT_MISMATCH` |
| Invalid foundation/options configuration | `MANUAL_PAYMENT_CONFIGURATION_INVALID` |
| Unsupported requested channel | `MANUAL_PAYMENT_CHANNEL_UNSUPPORTED` |

No global credentials or global tenant fallback are introduced. Manual Payment has
no external credential handoff in this phase.

## Capability Validation

The Application Contract validates only the capability required for
reference-payment initialization:

```text
ManualPaymentOperationalCapability.ReferencePaymentInitialization
```

Required capability evidence:

- provider enabled and options valid;
- tenant-scoped configuration supported;
- provider adapter certified;
- Runtime Bridge certified;
- instruction configuration supported;
- expiration policy supported;
- requested channel enabled and supported by capability flags;
- sandbox or production execution is compatible with feature flags and tenant row.

Unsupported capability or channel fails before Runtime Bridge delegation.

## Failure Mapping

Stable application failures are represented by `ManualPaymentApplicationException`.

Provider-neutral `CanonicalProviderError` is mapped before it leaves the
application boundary. The mapping preserves safe provider code and retryability
without exposing credentials, proof artifacts, provider wire objects, or Runtime
internals.

Runtime outcome admission accepts only the exact initialization outcome used by
the Stripe, Paymob, and Fawry application-contract pattern: evidence admitted,
verified, approved, transitioned to `AwaitingCustomer`, and transaction/settlement
rejected as non-financial initialization artifacts.

## Regression

Focused certification tests were added in:

```text
PointOfSale.Tests/Architecture/ManualPaymentApplicationContractCertificationTests.cs
```

Coverage includes:

- tenant resolution;
- current tenant configuration only;
- no global fallback;
- disabled provider rejection;
- deleted configuration rejection;
- missing configuration rejection;
- environment mismatch rejection;
- unsupported channel rejection;
- invalid capability/configuration rejection;
- stable provider failure mapping;
- DTO/result compatibility;
- architecture guards for no SQL migration, lifecycle writer, controller,
  frontend, proof upload, approval, review, replay, recovery, checkpoint, webhook,
  or callback activation;
- Runtime neutrality guards.

Certification command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentApplicationContractCertificationTests -nologo -v minimal --no-restore
```

Observed result:

```text
Passed: 12, Failed: 0, Skipped: 0
```

## Certification Decision

| Question | Decision |
|---|---|
| Application Contract implemented? | Yes |
| DTO compatibility preserved? | Yes |
| Tenant configuration resolution certified? | Yes |
| Capability validation certified? | Yes |
| Failure mapping certified? | Yes |
| Backward compatibility preserved? | Yes |
| Runtime preserved? | Yes |
| Architecture preserved? | Yes |
| Recommend MANUAL-CONTRACT-CLOSURE-2? | Yes |

## Final Verdict

MANUAL-CONTRACT-CLOSURE-1 is certified for Manual Payment reference-payment
initialization Application Contract only.

This phase activates only application preparation and Runtime Bridge delegation.
It does not activate proof workflow, approval, review, replay, recovery,
checkpoint persistence, webhook, callback, lifecycle mutation, controllers,
frontend, SQL, or persistence.

No new permanent cross-provider architecture truth was discovered. No
`.ai-context` update is required.
