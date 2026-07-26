# PAYMENT-END-TO-END-1 Certification

Date: 2026-07-17

Status: SOURCE CERTIFIED

Scope: Workflow output through Payment Intent execution, Order Domain mutation,
persistence, and existing Order Domain event propagation.

## Architecture Overview

PAYMENT-END-TO-END-1 connects the already certified payment layers without
redesigning them:

```text
Checkout
  -> Payment Runtime
  -> Lifecycle
  -> Workflow
  -> PaymentWorkflowExecutionService
  -> PaymentIntentDispatcher
  -> IPaymentIntentHandler
  -> Order aggregate
  -> SQL persistence
  -> IEventPublisher
  -> MassTransit EF outbox
  -> existing Order event consumers
```

Workflow remains pure and immutable. The new execution service only iterates
workflow output and delegates each canonical intent to the dispatcher. The
dispatcher remains routing only. Handlers remain provider-neutral and
channel-neutral.

## Execution Pipeline

`CodWorkflowOrchestrator` emits canonical workflow intents that implement
`IPaymentWorkflowIntent`.

`PaymentWorkflowExecutionService` consumes workflow output and dispatches each
intent in order:

- `AdvanceCollection`
- `CompleteAdmissionWorkflow`
- `AwaitEvidence`
- `QueueOperationalReview`
- `RejectAdmissionWorkflow`

`PaymentIntentDispatcher` resolves exactly one registered
`IPaymentIntentHandler` for each canonical intent type and rejects missing or
duplicate registrations.

## Order Integration

Handlers load the tenant-owned `Order` aggregate through
`IPaymentIntentOrderDomainStore`, validate the protected order-status boundary,
mutate only Order status fields, and persist through `ApplicationDbContext`.

Order effects are:

| Intent | Order effect |
|---|---|
| `AdvanceCollection` | `Pending` -> `Confirmed` |
| `CompleteAdmissionWorkflow` | Load-and-collapse completion checkpoint |
| `AwaitEvidence` | Active order -> `OnHold` |
| `QueueOperationalReview` | Active order -> `OnHold` |
| `RejectAdmissionWorkflow` | Active, non-money-recognized order -> `Cancelled` |

Handlers never mutate `PaymentStatus`. Payment lifecycle state remains owned by
`IPaymentLifecycleService` / `PaymentLifecycleService`.

## Consumer Integration

The phase reuses the existing Order event pipeline. Handlers publish through
`IEventPublisher`; they do not call consumers, MassTransit, RabbitMQ, SignalR,
notification services, UI code, Storefront, Affiliate, or CommerceHub directly.

Existing consumers continue to observe authoritative Order Domain events,
especially `OrderStatusChangedEvent`, through the already configured
MassTransit EF outbox.

This preserves convergence for:

- POS Admin order views.
- Storefront customer order and timeline reads.
- Affiliate order lineage and synchronization.
- Marketplace / CommerceHub order visibility.
- Future event-driven channels.

## Event Flow

Mutating handlers publish authoritative Order Domain events:

| Handler | Events |
|---|---|
| `AdvanceCollectionHandler` | `OrderAcceptedEvent`, `OrderStatusChangedEvent` |
| `AwaitEvidenceHandler` | `OrderAwaitingEvidenceEvent`, `OrderStatusChangedEvent` |
| `QueueOperationalReviewHandler` | `OrderOperationalReviewQueuedEvent`, `OrderStatusChangedEvent` |
| `RejectAdmissionHandler` | `OrderRejectedEvent`, `OrderStatusChangedEvent` |
| `CompleteAdmissionWorkflowHandler` | No event; completion is a no-op checkpoint after accepted advancement |

The pipeline does not duplicate projection, timeline, admin, customer,
affiliate, or marketplace consumers. All downstream visibility remains
event-driven from the existing Order event flow.

## Synchronization

Order status and timeline synchronization is achieved by the existing
`OrderStatusChangedEvent` path. POS Admin, Storefront, Affiliate Checkout, and
Marketplace Split Checkout converge because they observe the same SQL Order
state and the same Order Domain events, regardless of checkout channel.

Storefront COD, Affiliate COD, and Marketplace Split COD now share one
canonical route after workflow output:

```text
COD workflow intent
  -> PaymentWorkflowExecutionService
  -> PaymentIntentDispatcher
  -> canonical handler
  -> Order aggregate
  -> SQL
  -> Order events
```

## Replay Strategy

Replay is collapse-first:

- The dispatcher remains deterministic for registered intent type.
- Handlers resolve the target Order status before mutation.
- If the Order is already at the target status, execution returns before
  `SaveChanges`.
- No duplicate Order Domain event is published for an already-applied status
  intent.
- Terminal or money-recognized unsafe transitions still fail closed.

This strategy preserves deterministic convergence while allowing existing
outbox, consumer, and projection replay controls to continue operating as the
transport/read-model reliability layer.

## Idempotency

Idempotency is source-certified for repeated canonical intent execution:

- Accepted COD replay does not duplicate persistence.
- Accepted COD replay does not duplicate `OrderAcceptedEvent`.
- Accepted COD replay does not duplicate `OrderStatusChangedEvent`.
- Rejected COD replay does not duplicate cancellation persistence or events.
- Operational review replay does not duplicate hold persistence or events.

Durable cross-process intent-execution records remain a future operational
enhancement; current correctness is protected by current SQL Order state and
existing outbox/consumer idempotency.

## Known Gaps

- This is source certification, not live operational certification.
- No new provider activation is certified by this phase.
- No new UI, SignalR, RabbitMQ topology, notification path, or frontend
  behavior is certified by this phase.
- Durable intent-execution ledgering can be added later for richer replay
  forensics, but it must not replace SQL Order authority or the existing event
  pipeline.
- Official live channel drills, projection-lag dashboards, and environment
  evidence remain production-readiness work.

## Certification

| Question | Answer |
|---|---|
| Execution pipeline complete? | Yes, source-certified from workflow output to Order event publication. |
| Workflow connected? | Yes, via `PaymentWorkflowExecutionService` consuming immutable workflow intents. |
| Dispatcher connected? | Yes. Workflow output reaches `PaymentIntentDispatcher`. |
| Handlers connected? | Yes. All canonical workflow intent types have registered handlers. |
| Order Domain integrated? | Yes. Handlers load and mutate the SQL Order aggregate only. |
| Persistence integrated? | Yes. Mutations persist through `ApplicationDbContext` behind `IPaymentIntentOrderDomainStore`. |
| Domain events propagated? | Yes. Mutating handlers publish through `IEventPublisher`. |
| POS Admin synchronized? | Yes, through existing Order SQL reads and Order event flow. |
| Storefront synchronized? | Yes, through existing customer Order projection/read repair paths fed by Order events. |
| Affiliate synchronized? | Yes, through existing affiliate event-driven order lineage consumers. |
| Marketplace synchronized? | Yes, through existing marketplace/CommerceHub order event propagation. |
| Timeline synchronized? | Yes, via the existing `OrderStatusChangedEvent` timeline path. |
| Idempotency preserved? | Yes. Replayed already-applied intents return before persistence and events. |
| Provider neutrality preserved? | Yes. Execution uses canonical workflow intents only. |
| Channel neutrality preserved? | Yes. No Storefront, Affiliate, CommerceHub, POS Admin, UI, or frontend dependency exists in the execution layer. |
| Architecture preserved? | Yes. Workflow is pure, dispatcher routes only, handlers execute Order Domain actions, and existing consumers remain event-driven. |
| Payments ready for production evolution? | Yes, for architecture-led production evolution; live rollout still requires environment evidence. |
| Recommend PAYMENT-PROVIDERS-1? | Yes. |

Certification tests:

```text
PointOfSale.Tests.Architecture.PaymentIntentInfrastructureCertificationTests
PointOfSale.Tests.Architecture.PaymentEndToEnd1CertificationTests
```
