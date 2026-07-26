# PAYMENT-RUNTIME-7 Canonical Payment Transaction Engine

Date: 2026-07-10
Scope: Provider-neutral runtime financial transaction artifacts only
Decision: Certified for the PAYMENT-RUNTIME-7 transaction-engine scope

## Transaction Model

`PaymentTransaction` is the canonical immutable provider-neutral runtime financial artifact produced from a valid payment decision and an applied state transition.

It contains:

- `TransactionId`
- `TransactionType`
- `TransactionStatus`
- `TransactionAmount`
- `Currency`
- `CreatedUtc`
- `CorrelationId`
- `PaymentSessionId`
- `PaymentDecisionId`
- `TransitionId`
- `Metadata`
- `Reference`

`PaymentTransaction` is not accounting, not settlement, not provider execution, not persistence, not capture execution, and not refund execution.

## Transaction Runtime

`PaymentTransactionRuntime` receives an immutable `PaymentDecision`, an applied `StateTransition`, canonical transaction amount, canonical currency, and optional in-memory prior transaction records.

It returns `TransactionResult` with an optional immutable `PaymentTransaction`.

The runtime does not mutate `PaymentSession`, mutate `PaymentDecision`, mutate `StateTransition`, call providers, evaluate policy, persist records, settle money, post accounting, execute refunds, or execute captures.

## Relationships

```text
PaymentSession
  1 -> * PaymentTransaction

PaymentDecision
  1 -> * PaymentTransaction

StateTransition
  1 -> * PaymentTransaction
```

Relationships are identifier-only:

- `PaymentTransaction` stores `PaymentSessionId`.
- `PaymentTransaction` stores `PaymentDecisionId`.
- `PaymentTransaction` stores `TransitionId`.
- `PaymentTransaction` never owns or embeds `PaymentSession`, `PaymentDecision`, `StateTransition`, verification, provider, settlement, or accounting objects.

## Transaction Types

Canonical provider-neutral transaction types are:

- `Authorization`
- `Capture`
- `Sale`
- `Refund`
- `PartialRefund`
- `Void`
- `Adjustment`
- `Chargeback`
- `Reversal`
- `SettlementReservation`

`SettlementReservation` is a runtime reservation artifact only. It is not settlement execution.

## Value Objects

Implemented value objects:

- `TransactionId`
- `TransactionReference`
- `TransactionAmount`
- `TransactionReason`
- `TransactionTimestamp`
- `TransactionMetadata`
- `TransactionTraceId`
- `TransactionTrace`

Existing runtime identifiers remain part of the relationship model:

- `PaymentSessionId`
- `DecisionId`
- `TransitionId`
- `CorrelationId`
- `Currency`

## Result Model

Implemented result model:

- `TransactionResult`
- `TransactionStatus`
- `TransactionFailure`
- `TransactionTrace`
- `TransactionMetadata`

Failures are transaction-construction failures only. They do not execute money movement or decide business policy.

## Factories

Implemented factories:

- `PaymentTransactionFactory`
- `TransactionResultFactory`

Factories perform construction and invariant validation only. They contain no provider calls, settlement, persistence, accounting, policy, refund execution, capture execution, or business rules.

## Validation

Transaction production validates:

- Decision exists and is produced.
- Transition exists and has canonical identifiers and statuses.
- Transition belongs to the decision.
- Currency is present and canonical.
- Amount is non-negative.
- Required identifiers are present.
- Prior transactions belong to the same session.
- Replay by transition is detected.
- Duplicate prior transaction references are detected.

Replay and duplicate checks use caller-supplied prior transactions only. The runtime introduces no persistence or external data access.

## Architecture Decisions

1. The transaction runtime lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. Transactions are immutable runtime financial artifacts only.
3. Transactions are provider-neutral and contain no provider-specific names or fields.
4. Transactions reference sessions, decisions, and transitions by identifiers only.
5. `StateTransitionRuntime` remains the only certified runtime component authorized to produce an updated `PaymentSession` state.
6. `PaymentTransactionRuntime` never changes `PaymentSession`, `PaymentDecision`, `StateTransition`, verification, providers, settlement, or accounting.
7. Transaction type mapping is canonical runtime vocabulary, not provider adapter behavior.
8. Transaction runtime validates construction invariants only and does not evaluate business policy.

## Certification Decision

PaymentTransaction implemented: Yes.

Transaction Runtime implemented: Yes.

Relationships implemented: Yes.

Factories implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Financial boundary preserved: Yes.

Production transaction runtime certified: Yes for immutable provider-neutral transaction artifact production only. Settlement, accounting, provider execution, persistence, refund execution, capture execution, external APIs, and policy evaluation remain outside this certification.

Recommended next phase: PAYMENT-RUNTIME-8 persistence projection or orchestration only if the transaction artifact boundary remains immutable and provider-neutral.
