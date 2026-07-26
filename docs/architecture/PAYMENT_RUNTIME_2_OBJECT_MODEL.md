# PAYMENT-RUNTIME-2 Object Model

Date: 2026-07-10
Scope: Canonical provider-neutral runtime object graph only
Decision: Certified for the immutable PAYMENT-RUNTIME-2 object-model scope

## Object Diagram

```text
PaymentSession
  1 -> * PaymentAttempt
  1 -> * ProviderSession

PaymentAttempt
  -> PaymentSessionId

ProviderSession
  -> PaymentSessionId
  -> PaymentAttemptId
```

`PaymentSession` is the aggregate root. `PaymentAttempt` and `ProviderSession` reference it by identifier only. Neither child owns or embeds the aggregate.

## Aggregate Model

`PaymentSession` contains provider-neutral identity, ownership, method, money, canonical status, timestamps, metadata, audit references, attempts, and provider sessions.

`PaymentAttempt` represents retryable execution identity under a session. It stores attempt identity, attempt number, provider-neutral reference, canonical attempt status, failure reason, retry count, and metadata.

`ProviderSession` represents an external provider or workflow conversation. It stores provider session identity, provider name, external reference/status, timestamps, and metadata. It is not a `PaymentSession`.

## Object Responsibilities

- `PaymentSession`: aggregate root and durable payment obligation identity.
- `PaymentAttempt`: immutable attempt data under a payment session.
- `ProviderSession`: provider conversation evidence reference under a session and attempt.
- Value objects: provider-neutral typed identifiers, money, currency, references, reasons, correlation ids, metadata, and audit references.
- Factories: valid object construction only.

## Relationships

- `PaymentSession` owns attempt and provider-session collections.
- `PaymentAttempt` stores `PaymentSessionId`; it never stores a `PaymentSession`.
- `ProviderSession` stores `PaymentSessionId` and `PaymentAttemptId`; it never stores a runtime aggregate.
- Collections are `IReadOnlyCollection<T>` and are materialized as arrays during factory construction.

## Value Objects

Implemented value objects:

- `Money`
- `Currency`
- `PaymentReference`
- `ProviderReference`
- `AttemptNumber`
- `FailureReason`
- `ReasonCode`
- `CorrelationId`
- `PaymentMetadata`
- `AuditReferences`

Existing foundation identifiers remain part of the runtime object model:

- `PaymentId`
- `PaymentSessionId`
- `PaymentAttemptId`
- `ProviderSessionId`

## Construction Rules

Factories perform invariant validation only:

- Required session, payment, attempt, provider-session, order, tenant, and store identifiers.
- Required payment method and provider name references.
- Required money and currency.
- Non-negative money amount.
- Positive attempt number.
- Non-negative retry count.
- Canonical non-unknown statuses.
- Last-updated and last-sync timestamps cannot precede creation timestamps.

No business validation, state transitions, policy decisions, provider calls, persistence writes, or evidence admission occur in these factories.

## Architecture Decisions

1. The object model lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. Runtime objects are provider-neutral and contain no provider-specific fields.
3. Runtime objects have no EF Core attributes, persistence annotations, API references, infrastructure references, or frontend references.
4. Runtime objects use init-only properties and read-only collection contracts.
5. Public parameterless constructors exist only to keep objects serialization-safe.
6. Factories are construction helpers only, not behavior services.
7. `ProviderSession` is a provider conversation record, not a payment aggregate.

## Certification Decision

PaymentSession implemented: Yes.

PaymentAttempt implemented: Yes.

ProviderSession implemented: Yes.

Aggregate root preserved: Yes.

Value objects implemented: Yes.

Factories implemented: Yes.

Architecture guards added: Yes.

Serialization covered: Yes.

Provider-neutral: Yes.

Production object model certified: Yes for the frozen object-model scope. Runtime behavior, providers, persistence, policy, verification, settlement, refunds, capture, and state transitions remain future phases.
