# PAYMENT-RUNTIME-3 Actions And Evidence

Date: 2026-07-10
Scope: Canonical provider-neutral runtime actions and provider evidence only
Decision: Certified for the immutable PAYMENT-RUNTIME-3 action/evidence scope

## Action Model

`PaymentAction` is the canonical provider-neutral behavior object for runtime interactions that require a customer, operator, provider, carrier, or system-facing step.

Supported action categories are provider-neutral:

- Redirect
- Embedded checkout
- SDK confirmation
- Reference payment
- QR payment
- Wallet approval
- Receipt upload
- Receive shipment
- Manual review
- Status refresh
- Action retry
- Action cancel

`PaymentAction` does not call providers, execute SDKs, evaluate policy, mutate payment state, persist itself, settle money, capture funds, refund funds, or verify evidence.

## Evidence Model

`ProviderEvidence` is the canonical provider-neutral evidence object for facts observed around an action.

Supported evidence categories are provider-neutral:

- Webhook
- Callback
- Receipt
- Status inquiry
- Driver confirmation
- Carrier confirmation
- Settlement report
- Refund evidence
- Manual approval
- Provider notification

`ProviderEvidence` records evidence identity, action ownership, evidence type, status, source, timestamp, hash, payload reference, correlation, and metadata. It never modifies runtime state.

## Relationships

The canonical relationship chain is:

```text
PaymentSession
  1 -> * PaymentAttempt
  1 -> * PaymentAction
  1 -> * ProviderEvidence
```

Ownership is by immutable identifiers only:

- `PaymentAction` stores `PaymentSessionId` and `PaymentAttemptId`.
- `ProviderEvidence` stores `PaymentActionId`.
- `ProviderEvidence` does not store or own `PaymentSession`, `PaymentSessionId`, `PaymentAttempt`, or `PaymentAttemptId`.

## Value Objects

Implemented value objects include:

- `PaymentActionId`
- `ActionReference`
- `ActionCorrelationId`
- `EvidenceId`
- `EvidenceReference`
- `EvidenceHash`
- `EvidenceSource`
- `EvidenceTimestamp`
- `ProviderPayloadReference`
- `PaymentActionMetadata`
- `EvidenceMetadata`
- `PaymentActionResult`

Existing runtime identifiers remain part of the relationship model:

- `PaymentSessionId`
- `PaymentAttemptId`
- `CorrelationId`

## Factories

Only two factories are introduced:

- `PaymentActionFactory`
- `ProviderEvidenceFactory`

Factories perform invariant validation and object construction only. They contain no runtime logic, provider calls, policy decisions, verification, persistence, settlement, refund, capture, state machine, or adapter behavior.

## Construction Rules

Construction validates:

- Required action and evidence identifiers.
- Required session and attempt ownership for actions.
- Required action ownership for evidence.
- Required action and evidence references.
- Required action and evidence timestamps.
- Required action and evidence types.
- Required action and evidence statuses.
- Evidence attached to a `PaymentAction` must belong to that action.
- Action last-updated timestamp cannot precede action creation timestamp.

No business validation is performed.

## Architecture Decisions

1. The model lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. Objects are immutable through init-only public properties.
3. Public parameterless constructors exist only for serialization safety.
4. Collections are `IReadOnlyCollection<T>` and are materialized as arrays during factory construction.
5. The model has no EF Core attributes, persistence annotations, API references, infrastructure references, frontend references, or provider-specific fields.
6. Provider evidence is admitted data only. It is not payment state authority.
7. Provider-specific providers and adapters must translate into this model rather than leaking provider-specific fields into runtime objects.

## Certification Decision

PaymentAction implemented: Yes.

ProviderEvidence implemented: Yes.

Relationships implemented: Yes.

Value objects implemented: Yes.

Factories implemented: Yes.

Architecture guards added: Yes.

Serialization covered: Yes.

Provider-neutral: Yes.

Production action/evidence model certified: Yes for the frozen action/evidence scope. Verification, decision engine, policy evaluation, provider adapters, persistence, state machine, settlement, refund, capture, and runtime mutation remain future phases.
