# MANUAL-CONVERGENCE-1

Date: 2026-07-16  
Scope: POS Admin and Storefront Manual Payment execution convergence  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and frozen

## Executive Summary

MANUAL-CONVERGENCE-1 activates the already-certified Manual Payment application
contracts for the existing Manual Wallet execution surface.

Manual Payment initialization now enters through
`ManualPaymentRuntimeApplicationService`, which delegates to
`ManualPaymentRuntimeBridge`, the certified provider adapter, and the frozen
Payment Runtime. Submitted proof metadata is admitted through
`ManualPaymentEvidenceApplicationService`, which delegates to the certified
evidence adapter and then to the same Runtime Bridge.

No Runtime behavior, Runtime Bridge behavior, provider adapter behavior, evidence
adapter behavior, advanced operation, lifecycle service, business rule, SQL
schema, persistence model, approval workflow, review workflow, proof upload
feature, callback, webhook, replay, recovery, checkpoint, or frontend behavior is
introduced by this convergence.

## Certified Execution Path

Initialization:

```text
Trusted caller
  -> PaymentService persisted intent authority
  -> ManualPaymentRuntimeApplicationService
  -> ManualPaymentRuntimeBridge
  -> ManualPaymentProviderAdapter
  -> PaymentRuntimeOrchestrator
  -> exact initialization Runtime outcome admission
```

Evidence admission:

```text
Trusted caller
  -> PaymentService persisted intent authority
  -> ManualPaymentEvidenceApplicationService
  -> ManualPaymentEvidenceAdapter
  -> ManualPaymentRuntimeBridge
  -> PaymentRuntimeOrchestrator
  -> exact evidence-admission Runtime outcome admission
```

The persisted Manual Wallet compatibility code remains `MANUALWALLET`. The
certified provider evidence source remains `manual_payment`.

## Removed Legacy Paths

Removed convergence bypass:

- `ManualWalletPaymentAdapter` is no longer registered as a generic
  `IPaymentAdapter`.
- Manual Wallet initialization no longer enters through generic adapter
  discovery and `IPaymentAdapter.InitiateAsync`.
- PaymentService no longer invokes a direct Manual Wallet provider adapter path.
- PaymentService does not invoke `PaymentRuntimeOrchestrator` directly.
- Controllers do not call Manual Payment provider adapters or Runtime directly.

The remaining `ManualWalletPaymentAdapter` source is not an active execution
path after this phase. It is not registered and is guarded against reactivation.

## Regression

Focused certification tests were added in:

```text
PointOfSale.Tests/Architecture/ManualPaymentConvergenceCertificationTests.cs
```

Coverage includes:

- initialization converged through `ManualPaymentRuntimeApplicationService`;
- evidence admission converged through `ManualPaymentEvidenceApplicationService`;
- Runtime Bridge enforcement;
- direct provider adapter path removal;
- direct Runtime invocation guard;
- controller adapter/Runtime bypass guard;
- certified layer responsibility guards.

Existing Manual Payment authority tests were updated so they no longer certify
the removed legacy adapter mutation path.

## Architecture Verification

Verified boundaries:

- Application owns orchestration.
- Runtime Bridge is the only Manual Payment bridge into Runtime.
- Provider Adapter remains translation/protocol only.
- Evidence Adapter remains evidence translation only.
- Runtime remains provider-neutral and unchanged.
- Lifecycle authority remains outside this convergence.
- SQL schema is unchanged.
- Frontend is unchanged.
- Backward compatibility is preserved through the existing `MANUALWALLET`
  persisted payment-method compatibility boundary.

## Certification Decision

| Question | Decision |
|---|---|
| Application execution converged? | Yes |
| Evidence execution converged? | Yes |
| Legacy execution removed? | Yes |
| Certified execution path enforced? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| Backward compatibility preserved? | Yes |
| Convergence certified? | Yes, source-level convergence certified |
| Recommend MANUAL-PRODUCTION-1? | Yes, proceed to production-readiness review without weakening the frozen architecture |

No new permanent cross-provider architecture truth was discovered. No
`.ai-context` update is required.
