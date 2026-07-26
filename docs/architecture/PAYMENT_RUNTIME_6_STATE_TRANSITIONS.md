# PAYMENT-RUNTIME-6 Canonical State Transition Runtime

Date: 2026-07-10
Scope: Provider-neutral state transition runtime only
Decision: Certified for the PAYMENT-RUNTIME-6 state-transition scope

## State Machine

Canonical provider-neutral payment states include:

- Created
- Pending
- AwaitingCustomer
- AwaitingProvider
- Verified
- Approved
- Rejected
- Cancelled
- Expired
- Completed
- Quarantined
- ManualReview
- Recovery
- SettlementPending
- Settled
- RefundPending
- Refunded

The runtime preserves earlier canonical runtime statuses for compatibility, but phase 6 introduces the explicit state-machine vocabulary above.

## Transition Runtime

`StateTransitionRuntime` is the only certified runtime component authorized to produce an updated `PaymentSession` state.

It receives:

- Immutable `PaymentDecision`
- Current immutable `PaymentSession`
- Optional in-memory prior `StateTransition` records for replay and uniqueness checks

It returns:

- `TransitionResult`
- Updated `PaymentSession` when the transition is applied
- Original `PaymentSession` when rejected or replayed
- Optional `StateTransition` when applied

The runtime does not call providers, execute SDKs, evaluate business policy, persist data, settle funds, capture funds, refund funds, or expose external APIs.

## Transition Rules

Legal positive progression is canonical and provider-neutral:

```text
Created -> Pending
Pending -> AwaitingCustomer
AwaitingCustomer -> Verified
AwaitingProvider -> Verified
Verified -> Approved
Approved -> Completed
Completed -> SettlementPending
SettlementPending -> Settled
RefundPending -> Refunded
```

Decision outcomes may also move eligible non-terminal states to:

- Rejected
- Cancelled
- Expired
- ManualReview
- Recovery
- Quarantined

Illegal transitions are rejected without changing the supplied session.

## Transition Model

Implemented model:

- `StateTransition`
- `TransitionId`
- `TransitionReference`
- `TransitionTimestamp`
- `TransitionTraceId`
- `TransitionReason`
- `TransitionTrace`
- `TransitionMetadata`
- `TransitionResult`
- `TransitionStatus`
- `TransitionFailure`

`StateTransition` records session ownership, decision ownership, source status, target status, reason, timestamp, trace, reference, and metadata. It is evidence of a state transition, not persistence and not a provider execution artifact.

## Value Objects

Implemented value objects:

- `TransitionId`
- `TransitionReference`
- `TransitionTimestamp`
- `TransitionTraceId`
- `TransitionReason`
- `TransitionMetadata`

Existing canonical identifiers remain part of the relationship model:

- `PaymentSessionId`
- `DecisionId`
- `CorrelationId`

## Factories

Implemented factories:

- `StateTransitionFactory`
- `TransitionResultFactory`

Factories perform construction and invariant validation only. They contain no provider calls, policy decisions, persistence, settlement, refund, capture, adapter behavior, or business rules.

## Validation

State transition execution validates:

- Current state
- Decision identity and production status
- Transition legality
- Transition ownership
- Transition uniqueness
- Replay safety

Replay and uniqueness checks use caller-supplied prior transitions only. The runtime introduces no persistence or external data access.

## Architecture Decisions

1. The state transition runtime lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. `StateTransitionRuntime` is the only certified runtime state authority for `PaymentSession`.
3. Decision runtime remains decision-only and cannot mutate payment sessions.
4. Verification runtime remains verification-only and cannot mutate payment sessions.
5. Providers, adapters, policy, persistence, settlement, capture, and refund execution are outside this phase.
6. Transitions are provider-neutral and contain no provider-specific fields.
7. Invalid or replayed transitions return the original immutable session.
8. Applied transitions produce a new immutable `PaymentSession` instance with updated status and audit reference.

## Certification Decision

StateTransitionRuntime implemented: Yes.

State machine implemented: Yes.

Transition rules implemented: Yes.

Transition validation implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Single state authority preserved: Yes.

Replay-safe: Yes.

Production transition runtime certified: Yes for the frozen state-transition scope. Provider execution, policy evaluation, persistence, settlement, refund execution, capture execution, adapters, and external APIs remain outside this certification.

Recommended next phase: PAYMENT-RUNTIME-7 persistence projection or execution orchestration only after the state authority boundary is explicitly preserved.
