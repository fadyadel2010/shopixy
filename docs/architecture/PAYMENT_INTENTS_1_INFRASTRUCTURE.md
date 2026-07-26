# PAYMENT-INTENTS-1 Payment Intent Infrastructure

Date: 2026-07-17

Status: Implemented and source-certified

Scope: Provider-neutral infrastructure dispatch and Order Domain intent handlers

## Architecture

PAYMENT-INTENTS-1 adds the first side-effecting layer after canonical workflow
orchestration:

```text
Workflow Intent
  -> PaymentWorkflowExecutionService
  -> PaymentIntentDispatcher
  -> registered handler
  -> Order aggregate load
  -> validation
  -> Order Domain mutation
  -> persistence
  -> domain event publication through IEventPublisher
```

Workflow remains immutable and pure. The new infrastructure executes admitted
workflow intent inside the Order Domain only. It does not call Runtime, provider
adapters, UI, Storefront, CommerceHub, Affiliate, notification services, or
RabbitMQ directly.

## Dispatcher

`PaymentWorkflowExecutionService` consumes workflow output and dispatches each
canonical intent in order. It owns no business rules, persistence, provider
logic, or event publication.

`PaymentIntentDispatcher` owns handler resolution only.

It validates:

- exactly one handler is registered per intent type;
- the dispatched intent is non-null;
- a handler exists for the incoming canonical intent type.

It does not load orders, persist state, publish events, inspect providers,
evaluate policy, or perform business actions.

## Handler Contracts

Handlers implement `IPaymentIntentHandler` and consume provider-neutral
`IPaymentWorkflowIntent`.

Implemented handlers:

- `AdvanceCollectionHandler`
- `AwaitEvidenceHandler`
- `QueueOperationalReviewHandler`
- `CompleteAdmissionWorkflowHandler`
- `RejectAdmissionHandler`

The existing COD workflow intent now adapts to `IPaymentWorkflowIntent`, but the
dispatcher and handlers depend only on the canonical payment workflow contract.

## Execution Flow

Handler execution is:

1. Receive immutable workflow intent.
2. Load the tenant-owned `Order` aggregate by canonical tenant and order ids.
3. Validate terminal status and payment recognition constraints.
4. Execute one Order Domain status action.
5. Persist through `ApplicationDbContext` behind `IPaymentIntentOrderDomainStore`.
6. Publish authoritative Order Domain events through `IEventPublisher`.
7. Return without knowing event consumers.

Current status effects:

| Intent | Order action | Event strategy |
|---|---|---|
| `AdvanceCollection` | `Pending` -> `Confirmed`; idempotent otherwise unless terminal. | `OrderAcceptedEvent`, plus `OrderStatusChangedEvent` when status changed. |
| `AwaitEvidence` | Active order -> `OnHold`. | `OrderAwaitingEvidenceEvent`, plus `OrderStatusChangedEvent` when status changed. |
| `QueueOperationalReview` | Active order -> `OnHold`. | `OrderOperationalReviewQueuedEvent`, plus `OrderStatusChangedEvent` when status changed. |
| `CompleteAdmissionWorkflow` | Loads the Order aggregate and completes without additional mutation. | No event; completion is idempotent after `AdvanceCollection`. |
| `RejectAdmissionWorkflow` | Active, non-money-recognized order -> `Cancelled`. | `OrderRejectedEvent`, plus `OrderStatusChangedEvent` when status changed. |

`RejectAdmissionHandler` refuses to reject an order after money recognition. It
does not mutate `PaymentStatus`; the existing payment lifecycle writer remains
the only payment-state authority.

When an intent resolves to the current Order status, execution returns before
`SaveChanges` and before event publication. Replay therefore collapses without
duplicate persistence or duplicate Order Domain events.

## Registration

Registration is provided by:

```text
AddPaymentIntentInfrastructure()
```

`Program.cs` registers it after:

```text
AddCodWorkflowOrchestrator()
```

Duplicate infrastructure registration is rejected.

## Domain Event Strategy

Handlers publish authoritative Order Domain events through `IEventPublisher`.
They do not know or call event consumers. They do not call MassTransit,
RabbitMQ, notification services, Storefront projections, CommerceHub, or
Affiliate surfaces.

Events introduced:

- `OrderAcceptedEvent`
- `OrderAwaitingEvidenceEvent`
- `OrderOperationalReviewQueuedEvent`
- `OrderRejectedEvent`

Existing compatibility event:

- `OrderStatusChangedEvent`

## Provider Neutrality

The dispatcher and handlers consume `IPaymentWorkflowIntent` and
`PaymentWorkflowIntentType`. They contain no Stripe, Paymob, Fawry, Manual
Payment, COD provider, SDK, credential, webhook, HMAC, or provider-status
knowledge.

Future providers can produce the same canonical workflow intent contract
without handler changes.

## Channel Neutrality

The infrastructure acts on the canonical Order aggregate and tenant/order
identity only. It does not reference Storefront, Affiliate, Marketplace,
CommerceHub, UI, controller, or channel-specific services.

Storefront checkout, affiliate checkout, marketplace split checkout, and future
channels may converge through the same canonical intent contract.

## Extension Points

Future phases may add:

- richer Order Domain methods as the aggregate evolves;
- replay/idempotency persistence around intent execution;
- operational review queue persistence beyond the current Order Domain hold;
- additional provider/workflow producers of the same canonical intent contract.

Extensions must keep dispatcher resolution free of business logic and keep
handlers provider-neutral, channel-neutral, and event-consumer agnostic.

## Certification

| Question | Answer |
|---|---|
| Dispatcher implemented? | Yes. `PaymentIntentDispatcher` resolves registered handlers and rejects missing or duplicate registrations. |
| Workflow execution service implemented? | Yes. `PaymentWorkflowExecutionService` executes workflow output by dispatching each canonical intent in order. |
| Handlers implemented? | Yes. `AdvanceCollectionHandler`, `AwaitEvidenceHandler`, `QueueOperationalReviewHandler`, `CompleteAdmissionWorkflowHandler`, and `RejectAdmissionHandler` execute or collapse canonical Order Domain actions. |
| Workflow remains pure? | Yes. Workflow still produces immutable intents and does not execute side effects. |
| Provider neutrality preserved? | Yes. Handlers consume canonical intent contracts only and have no provider dependency. |
| Channel neutrality preserved? | Yes. No Storefront, CommerceHub, Affiliate, marketplace, UI, or controller dependency exists in handlers. |
| Infrastructure boundary preserved? | Yes. Execution is limited to Order Domain load, validation, mutation, persistence, and event publication. |
| Order Domain ownership preserved? | Yes. Order state changes use the authorized Order status boundary; payment lifecycle state is not directly mutated. |
| Events published correctly? | Yes. Mutating handlers publish authoritative Order Domain events through `IEventPublisher` and do not know consumers. |
| Replay collapse implemented? | Yes. Already-applied status intents return before persistence or event publication. |
| No UI dependency? | Yes. No UI/controller/frontend dependency exists. |
| No provider dependency? | Yes. No Stripe, Paymob, Fawry, Manual Payment, COD provider, or SDK dependency exists. |
| Architecture certified? | Yes, for PAYMENT-INTENTS-1 source scope. |
| Recommend PAYMENT-END-TO-END-CERTIFICATION? | Complete. See `PAYMENT_END_TO_END_1_CERTIFICATION.md`. |

Certification tests:

```text
PointOfSale.Tests.Architecture.PaymentIntentInfrastructureCertificationTests
```

This phase does not certify live provider operation, payment lifecycle mutation,
settlement, replay storage, notification delivery, or end-to-end channel
activation.
