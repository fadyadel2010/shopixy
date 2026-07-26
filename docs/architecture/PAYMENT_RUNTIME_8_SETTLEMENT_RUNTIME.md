# PAYMENT-RUNTIME-8 Canonical Settlement Runtime

Date: 2026-07-10
Scope: Provider-neutral runtime settlement artifacts only
Decision: Certified for the PAYMENT-RUNTIME-8 settlement-runtime scope

## Settlement Model

`PaymentSettlement` is the final immutable financial artifact produced by the canonical payment runtime.

It contains:

- `SettlementId`
- `SettlementType`
- `SettlementStatus`
- `SettlementAmount`
- `Currency`
- `CreatedUtc`
- `CorrelationId`
- `PaymentSessionId`
- `PaymentTransactionId`
- `SettlementReference`
- `Metadata`

`PaymentSettlement` is settlement intent and lifecycle only. It is not accounting, not provider execution, not persistence, not ledger posting, not money transfer, not treasury execution, not bank integration, and not external settlement.

## Settlement Runtime

`PaymentSettlementRuntime` receives an immutable `PaymentTransaction` and optional in-memory prior settlement records.

It returns `SettlementResult` with an optional immutable `PaymentSettlement`.

The runtime does not mutate `PaymentSession`, mutate `PaymentTransaction`, mutate `PaymentDecision`, call providers, evaluate policy, persist records, move money, post accounting, execute bank settlement, or expose external APIs.

Settlement identity and reference are deterministically derived from the transaction identity and canonical transaction type. The same transaction produces the same settlement identity and settlement reference.

## Settlement Types

Canonical provider-neutral settlement types are:

- `AuthorizationSettlement`
- `CaptureSettlement`
- `SaleSettlement`
- `RefundSettlement`
- `ChargebackSettlement`
- `AdjustmentSettlement`
- `ReservationSettlement`
- `ReversalSettlement`
- `ManualSettlement`
- `CODSettlement`

`ManualSettlement` and `CODSettlement` are canonical vocabulary only. They do not introduce provider adapters, external APIs, collection execution, or money movement.

## Relationships

```text
PaymentTransaction
  1 -> * PaymentSettlement

PaymentSession
  1 -> * PaymentSettlement
```

Relationships are identifier-only:

- `PaymentSettlement` stores `PaymentSessionId`.
- `PaymentSettlement` stores `PaymentTransactionId`.
- `PaymentSettlement` never owns or embeds `PaymentSession`, `PaymentTransaction`, `PaymentDecision`, providers, accounting, bank records, or persistence records.

## Value Objects

Implemented value objects:

- `SettlementId`
- `SettlementReference`
- `SettlementTimestamp`
- `SettlementReason`
- `SettlementMetadata`
- `SettlementTraceId`
- `SettlementTrace`
- `SettlementAmount`

Existing runtime identifiers remain part of the relationship model:

- `PaymentSessionId`
- `TransactionId`
- `CorrelationId`
- `Currency`

## Result Model

Implemented result model:

- `SettlementResult`
- `SettlementStatus`
- `SettlementFailure`
- `SettlementTrace`
- `SettlementMetadata`

Failures are settlement-construction failures only. They do not execute money movement, provider settlement, bank settlement, accounting, or business policy.

## Factories

Implemented factories:

- `PaymentSettlementFactory`
- `SettlementResultFactory`

Factories perform construction and invariant validation only. They contain no provider calls, persistence, accounting, bank settlement, treasury execution, policy, adapter behavior, or business rules.

## Validation

Settlement production validates:

- Transaction exists.
- Required settlement identifiers are canonical.
- Amount is non-negative.
- Currency is present and canonical.
- Prior settlements belong to the same session and transaction.
- Replay by transaction is detected.
- Duplicate prior settlement references are detected.
- Canonical transaction and settlement references are present.

Replay and duplicate checks use caller-supplied prior settlements only. The runtime introduces no persistence or external data access.

## Architecture Decisions

1. The settlement runtime lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. Settlement is the final immutable financial artifact produced by the runtime.
3. Settlements are provider-neutral and contain no provider-specific names or fields.
4. Settlements reference sessions and transactions by identifiers only.
5. `StateTransitionRuntime` remains the only certified runtime component authorized to produce an updated `PaymentSession` state.
6. `PaymentTransactionRuntime` remains the only certified runtime component authorized to produce transaction artifacts.
7. `PaymentSettlementRuntime` never changes `PaymentSession`, `PaymentTransaction`, `PaymentDecision`, providers, accounting, bank systems, treasury systems, or persistence.
8. Settlement type mapping is canonical runtime vocabulary, not provider adapter behavior.
9. Settlement runtime validates construction invariants only and does not evaluate business policy.
10. Settlement runtime is replay-safe and deterministic for a given immutable transaction.

## Settlement Invariants

- `PaymentSettlement` is immutable.
- Same `PaymentTransaction` produces the same `PaymentSettlement` identity and reference.
- Settlement Runtime never moves money.
- Settlement Runtime never posts accounting.
- Settlement Runtime never executes providers.
- Settlement Runtime never mutates runtime state.
- Settlement Runtime is replay-safe.
- Settlement Runtime is deterministic.

## Canonical Financial Flow

```text
Authorization
  -> Authorization Transaction
  -> Authorization Settlement
```

```text
Sale
  -> Sale Transaction
  -> Sale Settlement
```

```text
Refund
  -> Refund Transaction
  -> Refund Settlement
```

```text
COD
  -> Collection Transaction
  -> COD Settlement
```

## Certification Decision

PaymentSettlement implemented: Yes.

Settlement Runtime implemented: Yes.

Settlement relationships implemented: Yes.

Factories implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Financial boundary preserved: Yes.

Settlement boundary preserved: Yes.

Production settlement runtime certified: Yes for immutable provider-neutral settlement artifact production only. Accounting, provider execution, bank integration, treasury execution, persistence, money transfer, external APIs, and policy evaluation remain outside this certification.

Recommended next phase: PAYMENT-RUNTIME-9 reporting/projection or orchestration only if the settlement artifact boundary remains immutable and provider-neutral.
