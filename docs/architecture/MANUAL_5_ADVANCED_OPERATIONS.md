# MANUAL-5 Advanced Operations Adapter

Date: 2026-07-16  
Scope: Manual Payment Advanced Operations Adapter only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and frozen

## Executive Summary

MANUAL-5 defines the Manual Payment advanced-operations adapter as a
translation-only provider boundary.

The adapter maps canonical advanced-operation requests to Manual Payment
provider-model operations and returns canonical `ProviderEvidence` or
`CanonicalProviderError`. It does not execute provider I/O, business policy,
approval, review, lifecycle mutation, SQL, persistence, replay, recovery,
checkpointing, controllers, or Runtime activation.

Manual Payment remains a Reference Provider model for backend-owned reference
payments. Unsupported provider capabilities are never emulated.

## Capability Matrix

| Canonical operation | Manual provider operation | Decision | Scope |
|---|---|---|---|
| Cancel Pending Manual Payment | `CancelPendingManualPayment` | Supported | Translation only |
| Expire Pending Manual Payment | `ExpirePendingManualPayment` | Supported | Translation only |
| Payment Status Inquiry | `PaymentStatusInquiry` | Supported | Translation only |
| Evidence Status Inquiry | `EvidenceStatusInquiry` | Supported | Translation only |
| Capability Discovery | `CapabilityDiscovery` | Supported | Translation only |
| Withdraw Submitted Evidence | `Unsupported` | Not supported | Rejected evidence |
| Refund | `Unsupported` | Out of scope | Rejected evidence |
| Partial Refund | `Unsupported` | Out of scope | Rejected evidence |
| Capture | `Unsupported` | Out of scope | Rejected evidence |
| Settlement | `Unsupported` | Out of scope | Rejected evidence |
| Approval Execution | `Unsupported` | Out of scope | Rejected evidence |
| Review Execution | `Unsupported` | Out of scope | Rejected evidence |

## Supported Operations

Supported operations are provider-model facts only:

- cancel pending manual payment;
- expire pending manual payment;
- payment status inquiry;
- evidence status inquiry;
- capability discovery.

Successful translations return `ProviderEvidence` with `EvidenceStatus.Recorded`
and metadata that identifies the canonical operation, Manual provider operation,
provider reference, operation reference, canonical status, support flag, and
`translation_only = True`.

Cancel and expiration require supplied current provider-state facts compatible
with a pending Manual Payment model. If the supplied state is not pending, the
adapter fails closed with rejected evidence.

## Unsupported Operations

Unsupported operations return `EvidenceStatus.Rejected`.

Unsupported capability rejection is represented as:

```text
Canonical advanced request
  -> Manual provider operation = Unsupported
  -> Rejected ProviderEvidence
  -> CanonicalProviderError.NotSupported where the operation itself is unsupported
```

The adapter does not emulate unsupported Manual Payment provider capabilities.
Withdraw submitted evidence is not certified because the Manual Payment provider
model has evidence submission translation, but no provider-owned evidence
withdrawal capability.

Refund, partial refund, capture, settlement, approval execution, and review
execution remain out of scope. They require separately certified application,
workflow, lifecycle, or financial authority if ever activated.

## Canonical Translation

```text
Canonical Advanced Request
  -> ManualPaymentAdvancedOperationsAdapter
  -> Manual Provider Operation
  -> ProviderEvidence
```

Failure mapping:

```text
Unsupported operation
  -> Rejected ProviderEvidence
  -> CanonicalProviderError.NotSupported

Invalid state transition
  -> Rejected ProviderEvidence

Configuration mismatch
  -> CanonicalProviderError.Configuration
```

The adapter accepts current provider/evidence status as supplied canonical facts.
It does not load state, persist state, query tenant configuration, or decide
business policy.

## Regression

Focused certification tests are in:

`PointOfSale.Tests/Architecture/ManualPaymentAdvancedOperationsAdapterCertificationTests.cs`

Coverage includes:

- operation mapping;
- capability matrix;
- unsupported operation rejection;
- invalid state rejection;
- canonical provider error for configuration mismatch;
- rejected evidence;
- serialization without workflow/runtime types;
- architecture guards proving no Runtime, SQL, lifecycle, controller, refund,
  capture, provider I/O, or persistence ownership.

## Certification Decision

| Question | Decision |
|---|---|
| Advanced Operations Adapter implemented? | Yes |
| Supported operations certified? | Yes, translation only |
| Capability matrix certified? | Yes |
| Canonical mapping certified? | Yes |
| Rejected evidence certified? | Yes |
| CanonicalProviderError certified? | Yes |
| Architecture preserved? | Yes |
| Runtime preserved? | Yes |
| SQL/persistence/workflow excluded? | Yes |
| Recommend next phase? | Yes: `MANUAL-CONTRACT-CLOSURE-1` |

No `.ai-context` update is required. MANUAL-5 discovered no new permanent
cross-provider architectural truth; it applies the existing
`PAYMENT_REFERENCE_FREEZE.md` advanced-adapter rule to Manual Payment.
