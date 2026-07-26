# Manual Certification Closure 4

## Executive Summary

Manual Payment Closure 4 addresses only MANUAL-FINAL-004.

The correction establishes one Manual Payment recovery policy: checkout payment
recovery may resume an existing durable Manual Wallet payment intent through
the certified payment service path, but it may not create a competing Manual
Wallet payment intent.

## Board Finding

MANUAL-FINAL-004 was valid.

Manual Wallet checkout recovery selected pending Manual Wallet intents as
incomplete, then treated every Manual Wallet intent as complete. The same file
also contained a Manual Wallet creation branch that was unreachable because the
missing-intent path rejected Manual Wallet as non-recoverable.

## Root Cause

Recovery had two contradictory policies:

- existing pending Manual Wallet facts were discovered but not resumed; and
- missing Manual Wallet facts had a dormant creation branch even though recovery
  was not authorized to recreate Manual Wallet payment authority.

That made the recovery semantics unclear and left room for a future alternate
recovery model.

## Implemented Correction

Checkout recovery now treats a pending Manual Wallet payment intent as the only
recoverable Manual Wallet crash-window state.

Recovery loads durable order and payment-intent facts, rejects ambiguous or
conflicting Manual Wallet facts, and calls
`IPaymentService.ResumeExistingPaymentIntentAsync(...)` with the existing
payment intent id. The payment service reuses the existing persisted-intent
readiness path, which executes Manual Payment Runtime initialization, evidence
admission, and `PaymentLifecycleService.AdmitManualWalletReviewAsync(...)`.

The Manual Wallet creation branch was removed from recovery. Missing Manual
Wallet payment facts fail closed.

## Recovery Authority

Manual Wallet recovery authority is:

```text
CheckoutPaymentIntentRecoveryService
  -> durable SQL order and payment-intent facts
  -> IPaymentService.ResumeExistingPaymentIntentAsync
  -> Manual Payment Runtime application/evidence services
  -> Runtime admission
  -> PaymentLifecycleService.AdmitManualWalletReviewAsync
```

Controllers do not own recovery. Runtime, Runtime Bridge, Provider Adapter, and
Evidence Adapter remain unchanged.

## Checkpoint Semantics

The durable checkpoint is the existing `PaymentIntent` row for the order and
tenant. Recovery requires exactly one active Manual Wallet intent. It rejects:

- missing durable Manual Wallet facts;
- multiple active Manual Wallet intents;
- active payment facts for another provider on a Manual Wallet order;
- invalid lifecycle transitions returned by `PaymentLifecycleService`.

Recovery never constructs a second Manual Wallet payment intent.

## Regression

Focused certification coverage was added in
`ManualPaymentCertificationClosure4Tests`.

Coverage includes:

- recovery resumes an existing Manual Wallet payment intent;
- missing durable Manual Wallet facts do not create a payment;
- duplicate and conflicting Manual Wallet recovery facts are guarded;
- recovery uses payment-service resume and lifecycle review admission;
- Runtime, Runtime Bridge, Provider Adapter, Evidence Adapter, and controller
  boundaries remain preserved.

## Certification Decision

MANUAL-FINAL-004 is closed.

No implementation was added for MANUAL-FINAL-005, 006, 007, or 009.

Runtime remains provider-neutral and frozen. Runtime Bridge, Provider Adapter,
Evidence Adapter, SQL schema, replay redesign, retention, telemetry, sandbox,
production, webhook, callback, approval redesign, and expiration redesign remain
unchanged.
