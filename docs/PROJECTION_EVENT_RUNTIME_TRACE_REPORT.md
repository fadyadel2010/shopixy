# Projection Event Runtime Trace Report

## Executive Summary

This is a surgical runtime trace for product `Samsung Galaxy Note 10`.

- ProductId: `d7160ce9-5bce-141e-61de-8e0f36db6085`
- TenantId: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- Fresh trace id: `projection-trace-96d1f1d08eb142af8ac123763445c1b2`
- Runtime save result: SQL row version advanced from `728333` to `728832`.
- SQL authority result: product exists, active, online visible, searchable, published, and projection mutation version advanced to `5`.
- Mongo result: product is still absent from `ecommerce_read_db.products`.
- Elasticsearch result: product document is still absent from `products`.
- First proven failing stage: the canonical `PointOfSale.Core.Events.ProductUpdatedEvent` did not reach RabbitMQ.

The runtime evidence separates two similarly named event streams:

- Marketplace event: `PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent`
- Canonical projection event: `PointOfSale.Core.Events.ProductUpdatedEvent`

The marketplace event was published and consumed for the fresh trace. The canonical projection event exchange did not receive any publish for the same product save.

## Complete Runtime Diagram

```text
Product Save
  |
  v
ProductAuthoringOrchestrator.StageAuthoritativeOutboxAsync
  |
  +--> Marketplace ProductUpdatedEvent
  |      |
  |      v
  |    Marketplace outbox -> RabbitMQ -> Marketplace/cache consumers
  |      |
  |      v
  |    Durable ProcessedEvents rows captured
  |
  +--> Canonical ProductUpdatedEvent
         |
         v
       MassTransit EF Bus Outbox / dispatcher
         |
         v
       RabbitMQ exchange PointOfSale.Core.Events:ProductUpdatedEvent
         |
         x
       No publish observed; projection consumers never receive the canonical event
         |
         x
       Mongo ProductDocument missing
         |
         x
       Elasticsearch ProductSearchDocument missing
```

## Stage Ownership Table

| Stage | Owner | Source file | Input | Output | Failure conditions | Retry behavior |
|---|---|---|---|---|---|---|
| Product Save | Product Authoring API | `PointOfSale.API/Controllers/ProductsController.cs` | `PUT /api/products/{id}` with idempotency and expected version headers | `ProductAuthoringCommand` | missing idempotency key, stale expected version, validation failure | request-level retry only through client idempotency |
| StageAuthoritativeOutboxAsync | Product Authoring Orchestrator | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs` | accepted product mutation | marketplace event and canonical product event publish calls | event publish exception aborts transaction | no internal retry; transaction aborts on exception |
| EF Outbox Commit | ApplicationDbContext transaction | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`, `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs` | tracked product mutation plus outbox entities | committed product row and outbox rows | `SaveChangesAsync`/commit exception | no retry inside authoring command |
| Bus Outbox Dispatcher | MassTransit EF Bus Outbox | `PointOfSale.API/Program.cs`, `PointOfSale.Infrastructure/Instrumentation/MarketplacePublishFilter.cs` | `OutboxMessage` rows | RabbitMQ publish | dispatcher cannot publish, outbox storage failure | MassTransit bus outbox polling, query delay 1s |
| RabbitMQ | Broker topology | `PointOfSale.API/Program.cs` | canonical `PointOfSale.Core.Events:ProductUpdatedEvent` exchange publish | fanout to projection queues | publish absent, missing exchange/binding, broker failure | MassTransit retry intervals: 1s, 5s, 15s, 30s |
| Projection Consumer Receive | Product projection consumers | `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs`, `PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs` | canonical product event | SQL overlay/projection writer call | no message, invalid tenant/product id, consumer exception | MassTransit retry intervals |
| SQL Authority Overlay | Projection writer | `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs` | product id, tenant id, event version | visible projection candidate or tombstone decision | SQL overlay missing/rejected, visibility false | called per consumer delivery |
| Projection Writer | Projection writer | `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs` | overlay candidate and current Mongo document | upsert/tombstone/ignored/skipped | mutation fence stale, writer exception | consumer delivery retry on exception |
| Mongo Upsert | Product read repository | `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs` | `ProductDocument` | write to `ecommerce_read_db.products` | duplicate/stale write, Mongo exception | consumer retry on exception |
| Search Consumer | Search index consumers | `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs` | canonical product event plus Mongo projection | search document hydration | Mongo projection missing, ES exception | throws `ProjectionNotReadyException`; MassTransit retry intervals |
| Elasticsearch | Product search index service | `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs` | `ProductSearchDocument` | document in `products` index | index/delete failure | consumer retry on exception |

## Product Lifecycle

### Product Save

Captured runtime evidence:

```text
Trace=projection-trace-96d1f1d08eb142af8ac123763445c1b2
BeforeRowVersion=728333
AfterRowVersion=728832
```

The save succeeded and advanced SQL authority state:

```text
SQL_PRODUCT Found=True Id=d7160ce9-5bce-141e-61de-8e0f36db6085 TenantId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5 Name="Samsung Galaxy Note 10" IsDeleted=False IsActive=True OnlineVisible=True PublishStatus=1 Searchable=True ProjectionMutationVersion=5 CreatedAt=2026-07-31T18:49:06.7344270Z UpdatedAt=2026-07-31T20:57:38.7870620Z
```

### Outbox

Captured durable evidence:

```text
SQL_OUTBOX_MATCH_COUNT=0
SQL_OUTBOX_STATE_COUNT=0
SQL_INBOX_STATE_COUNT=0
```

No retained MassTransit EF outbox rows, outbox state rows, or inbox state rows were present for the probe window. This does not by itself prove a failed outbox dispatch because MassTransit can remove delivered rows, so RabbitMQ exchange counters were used as the decisive boundary.

### RabbitMQ

Captured before/after counter evidence for the fresh save:

```text
CoreBeforeIn=0
CoreAfterIn=0
CoreBeforeOut=0
CoreAfterOut=0
MarketplaceBeforeIn=10
MarketplaceAfterIn=11
MarketplaceBeforeOut=20
MarketplaceAfterOut=20
```

Captured RabbitMQ exchange evidence:

```text
PointOfSale.Core.Events:ProductUpdatedEvent
message_stats absent; publish_in observed as 0

PointOfSale.Core.Events.Marketplace:ProductUpdatedEvent
publish_in=11
```

Captured canonical bindings:

```text
RABBIT_BINDING Source=ProductUpdatedEvent Destination=PointOfSale.Core.Events:IntegrationEvent DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=affiliate-product-intelligence-projection DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=affiliate-product-policy-projection DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=commercehub-projection DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=product-cache-invalidation DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=product-updated-event DestinationType=exchange
RABBIT_BINDING Source=ProductUpdatedEvent Destination=product-updated-search-index DestinationType=exchange
```

Captured relevant queue state:

```text
RABBIT_QUEUE Name=product-updated-event Status=200 State=running Consumers=1 Messages=0 Ready=0 Unack=0
RABBIT_QUEUE Name=product-updated-search-index Status=200 State=running Consumers=1 Messages=0 Ready=0 Unack=0
RABBIT_QUEUE Name=product-created-event Status=200 State=running Consumers=1 Messages=0 Ready=0 Unack=0
RABBIT_QUEUE Name=product-search-index Status=200 State=running Consumers=1 Messages=0 Ready=0 Unack=0
RABBIT_QUEUE Name=product-visibility-lifecycle-projection Status=200 State=running Consumers=1 Messages=0 Ready=0 Unack=0
```

### Consumer

No canonical product projection consumer receipt was captured for `projection-trace-96d1f1d08eb142af8ac123763445c1b2`.

Captured durable processed events for the fresh trace are marketplace/cache consumers only:

```text
SQL_PROCESSED_EVENT EventId=91d6b53b-f7c1-4015-8cd1-196620bfb0a8 ConsumerName="MarketplaceProductUpdatedConsumer" EventType="PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent" EntityId=d7160ce9-5bce-141e-61de-8e0f36db6085 TenantId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5 CorrelationId=projection-trace-96d1f1d08eb142af8ac123763445c1b2 ProcessedAtUtc=2026-07-31T20:57:40.8534280Z
SQL_PROCESSED_EVENT EventId=91d6b53b-f7c1-4015-8cd1-196620bfb0a8 ConsumerName="RedisInvalidationConsumer:ProductUpdatedEvent" EventType="PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent" EntityId=d7160ce9-5bce-141e-61de-8e0f36db6085 TenantId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5 CorrelationId=projection-trace-96d1f1d08eb142af8ac123763445c1b2 ProcessedAtUtc=2026-07-31T20:57:40.7206130Z
```

Captured EventIds:

```text
Marketplace ProductUpdatedEvent EventId=91d6b53b-f7c1-4015-8cd1-196620bfb0a8 CorrelationId=projection-trace-96d1f1d08eb142af8ac123763445c1b2
Canonical ProductUpdatedEvent EventId=not captured in durable runtime artifacts because the canonical event did not reach RabbitMQ
```

### Projection Writer

No canonical projection writer decision was captured for the fresh trace. Because the canonical event did not reach RabbitMQ, this stage was not reached.

### Mongo

Captured runtime evidence:

```text
MONGO_PRODUCT Found=False Database=ecommerce_read_db Collection=products
```

### Search

No canonical search consumer receipt was captured for the fresh trace. Because the canonical event did not reach RabbitMQ, this stage was not reached.

### Elasticsearch

Captured runtime evidence:

```text
ES_DOCUMENT Status=404 DocumentId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5_d7160ce9-5bce-141e-61de-8e0f36db6085 Index=products
```

## Possible Failure Matrix

| Candidate | Evidence | Result |
|---|---|---|
| Event never left outbox | `SQL_OUTBOX_MATCH_COUNT=0`, `SQL_OUTBOX_STATE_COUNT=0`; no retained pending row | Not the certified boundary because there is no pending row proving a stuck outbox |
| Event never reached RabbitMQ | canonical exchange `PointOfSale.Core.Events:ProductUpdatedEvent` stayed `publish_in 0 -> 0` during a successful save | Certified |
| Consumer never received event | no canonical consumer artifacts, but this is downstream of missing RabbitMQ publish | Downstream consequence |
| Consumer threw exception | no canonical error queue and no canonical consumer log/evidence | Not supported |
| SQL Overlay rejected product | SQL product is visible/searchable/published; overlay stage not reached | Not supported |
| Projection Writer rejected product | projection writer stage not reached | Not supported |
| Mongo write failed | Mongo write stage not reached | Not supported |
| Mutation fence ignored event | mutation fence stage not reached | Not supported |
| Search consumer never received projection | search consumer stage not reached | Downstream consequence |
| Elasticsearch indexing failed | Elasticsearch stage not reached | Not supported |

## Required Runtime Probes

The minimum probes used were:

- Product save with correlation id `projection-trace-96d1f1d08eb142af8ac123763445c1b2`.
- RabbitMQ exchange counters before and after save for:
  - `PointOfSale.Core.Events:ProductUpdatedEvent`
  - `PointOfSale.Core.Events.Marketplace:ProductUpdatedEvent`
- SQL read-only probe for:
  - `Products`
  - `OutboxMessage`
  - `OutboxState`
  - `InboxState`
  - `ProcessedEvents`
- Mongo read-only probe for `ecommerce_read_db.products`.
- Elasticsearch read-only document probe for `products/_doc/{tenantId}_{productId}`.

No broad instrumentation, queue redesign, DTO change, projection behavior change, Mongo change, Elasticsearch change, or Product Authoring change was made beyond temporary trace logging and read-only probes.

## Most Probable Failure Stage

The failure stage is no longer a probability call. The fresh runtime save advanced SQL and marketplace publish counters, but did not advance the canonical RabbitMQ exchange counter. Therefore the earliest proven missing runtime artifact is the canonical RabbitMQ publish for `PointOfSale.Core.Events.ProductUpdatedEvent`.

## Certification

Event never reached RabbitMQ.
