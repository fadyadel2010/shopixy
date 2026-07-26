# Manual Certification Closure 1

## Executive Summary

Manual Payment Closure 1 addresses only Board Group A findings MANUAL-FINAL-001 and MANUAL-FINAL-008.

The correction establishes `ManualPaymentReviewApplicationService` as the application-owned operator workflow authority and preserves `PaymentLifecycleService` as the single durable lifecycle writer. Manual Payment continues to use the frozen provider-neutral Runtime defined by `PAYMENT_REFERENCE_FREEZE`.

## Board Finding

MANUAL-FINAL-001 was valid. Operator approval and rejection could bypass the application-owned review, evidence, capability, and Runtime admission path.

MANUAL-FINAL-008 was valid. Manual wallet lifecycle-affecting durable writes were still present outside the frozen lifecycle authority, including Manual wallet intent creation and review admission writes.

## Root Cause

The legacy Manual wallet flow split authority across controllers, `PaymentService`, Runtime-facing application services, and `PaymentLifecycleService`.

That allowed operator outcomes and durable lifecycle mutations to be admitted from more than one path, making Runtime admission and evidence admission optional instead of mandatory.

## Implemented Correction

Operator approval and rejection now converge through:

`AdminManualPaymentController` -> `ManualPaymentReviewApplicationService` -> validation -> capability gate -> Runtime evidence admission -> `PaymentLifecycleService`.

Manual wallet intent creation and review admission writes now converge through `PaymentLifecycleService` methods:

- `CreateManualWalletPaymentIntentAsync`
- `AdmitManualWalletReviewAsync`

Legacy routable wallet approval and rejection workflow was removed from `PaymentLifecycleController`. Legacy `PaymentService` operator methods are fail-closed compatibility stubs and no longer contain their own workflow.

## Workflow Authority

`ManualPaymentReviewApplicationService` is the single Manual Payment operator workflow authority for approval and rejection.

It validates review state, fails closed on expired payments, gates operator capability, records provider evidence through the frozen Runtime bridge, requires expected Runtime admission, and only then requests lifecycle persistence.

## Lifecycle Authority

`PaymentLifecycleService` remains the single durable lifecycle writer.

Manual Payment durable lifecycle mutation is centralized there for:

- Manual wallet `PaymentIntent` creation.
- Manual wallet review admission to awaiting verification.
- Approval and rejection lifecycle transitions requested after Runtime admission.

## Regression

Focused certification tests were added in `ManualPaymentCertificationClosure1Tests`.

The tests guard:

- Operator workflow convergence through `ManualPaymentReviewApplicationService`.
- Removal of routable legacy wallet workflow.
- Manual wallet lifecycle writes converging through `PaymentLifecycleService`.
- Absence of migration, direct SQL, webhook, callback, and Runtime expansion in closure-owned files.

## Certification Decision

Findings MANUAL-FINAL-001 and MANUAL-FINAL-008 are closed.

No implementation was added for MANUAL-FINAL-002, 003, 004, 005, 006, 007, or 009.

Runtime behavior remains provider-neutral and frozen. No schema migration, sandbox, production, webhook, callback, retention, replay, recovery, telemetry, or expiration workflow was implemented.
