# Manual Certification Closure 3

## Executive Summary

Manual Payment Closure 3 addresses only MANUAL-FINAL-003.

The correction establishes pending-payment expiration as an executable Manual
Payment lifecycle authority. Expiration is now checked before pending-review
presentation, before operator Runtime admission, and again inside
`PaymentLifecycleService` before wallet approval, rejection, or review
admission can mutate durable payment state.

## Board Finding

MANUAL-FINAL-003 was valid.

Manual Payment stored and presented expiration metadata, but expiration was not
an authoritative lifecycle boundary. An elapsed Manual Wallet payment could
remain visible for review and could still enter approval processing.

## Root Cause

Expiration was treated as descriptive metadata on `PaymentIntent.ExpiresAtUtc`.
The active review and lifecycle paths did not uniformly require a present,
valid, non-elapsed expiration before executable review or approval.

## Implemented Correction

`ManualPaymentExpirationAuthority` now owns Manual Payment expiration admission
at the application boundary.

Pending-review reads first expire elapsed Manual Wallet payments through
`PaymentLifecycleService.MarkPaymentExpiredAsync(...)`, then return only rows
with a present future expiration.

Operator approval and rejection call expiration admission before Runtime
execution. Expired payments are admitted only to lifecycle expiration, then the
review request is rejected. Missing, invalid, or clock-inconsistent expiration
fails closed without Runtime admission.

## Expiration Authority

Expiration fails closed for:

- missing expiration;
- default or invalid expiration;
- expiration earlier than durable intent creation;
- request clock earlier than durable intent creation;
- elapsed expiration.

Elapsed expiration is routed to the lifecycle writer as an expiration outcome.
It is not converted into approval, rejection, recovery, replay, retention,
telemetry, or evidence persistence work.

## Lifecycle Admission

`PaymentLifecycleService` remains the single durable lifecycle authority.

It now rejects wallet approval, wallet rejection, and Manual Wallet review
admission when expiration authority is missing, invalid, inconsistent, or
elapsed. `ManualWalletStateMachine` now permits
`AwaitingVerification -> Expired`, so expiration can be represented as a
durable terminal lifecycle state instead of remaining presentation-only.

## Regression

Focused certification tests were added in
`ManualPaymentCertificationClosure3Tests`.

Coverage includes:

- pending payment expiration through lifecycle authority;
- approval-after-expiry rejection before Runtime admission;
- missing expiration rejection;
- pending-review read expiration guard;
- lifecycle mutation expiration guard;
- architecture guards preserving Runtime, Runtime Bridge, Provider Adapter, and
  Evidence Adapter boundaries.

## Certification Decision

MANUAL-FINAL-003 is closed.

No implementation was added for MANUAL-FINAL-004, 005, 006, 007, or 009.

Runtime remains provider-neutral and frozen. Runtime Bridge, Provider Adapter,
Evidence Adapter, SQL schema, recovery, replay, retention, telemetry, sandbox,
production, webhook, and callback behavior remain unchanged.
