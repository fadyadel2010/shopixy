# Product Projection Runtime Certification

Product traced: `Samsung Galaxy Note 10`

Runtime product identity from live PostgreSQL probe:

- ProductId: `d7160ce9-5bce-141e-61de-8e0f36db6085`
- TenantId: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- CategoryId: `4ce92bdd-c8a8-486e-9a96-08275610375b`
- Shape: variant product, `HasVariants=True`

## Executive Summary

The canonical storefront catalog architecture is intact: React `ProductListPage` owns catalog rendering, calls `/api/storefront/products/discover`, the Discover API queries Elasticsearch candidates, and SQL authority recomposes and revalidates those candidates before returning them.

The traced product is not lost in Product Authoring, SQL visibility flags, category ownership, variant pricing, Discover API filtering, or React rendering. Live runtime evidence shows the SQL source of truth contains one matching product and that it passes the public gates:

- `IsDeleted=False`
- `IsActive=True`
- `IsOnlineVisible=True`
- `PublishStatus=1`
- `IsSearchable=True`
- `HasVariants=True`
- active non-deleted category chain exists
- four active current variants exist
- all four variants have effective price `3500.00`
- stock exists for all four variants

The first missing runtime output is the Mongo storefront product projection. The configured MongoDB database `ecommerce_read_db`, collection `products`, has no document for the traced tenant/product key. Elasticsearch also has no document in index `products` for document id `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5_d7160ce9-5bce-141e-61de-8e0f36db6085`, and an Elasticsearch visible-filter search returns zero hits.

Because Elasticsearch indexing depends on Mongo projection readiness for create/update events, the Elasticsearch miss is downstream of the Mongo projection miss. The visibility failure boundary is therefore after committed SQL authority and before the Mongo projection output.

## Complete Runtime Diagram

```text
Admin ProductForm
  -> ProductsController
  -> ProductAuthoringOrchestrator
  -> PostgreSQL Products/ProductVariants/ProductVariantAttributes/ProductVariantStocks
  -> MassTransit EF Outbox tables
  -> MassTransit bus outbox dispatcher after commit
  -> RabbitMQ product event endpoints
  -> ProductCreatedEventConsumer/ProductUpdatedEventConsumer/ProductVisibilityLifecycleProjectionConsumer
  -> ProductSqlAuthorityProjectionWriter
  -> ProductSearchSqlAuthorityOverlay
  -> MongoDB ecommerce_read_db.products
  -> ProductSearchIndexConsumer/ProductUpdatedSearchIndexConsumer/ProductDeletedSearchIndexConsumer
  -> ProductSearchDocumentMapper
  -> Elasticsearch products index
  -> GET /api/storefront/products/discover
  -> ProductDiscoveryService
  -> Storefront fetchProductsDiscover
  -> useProductDiscovery/useProductListDiscovery
  -> ProductListPage
  -> ProductCard DOM
```

## Stage Ownership Table

| Stage | Owner | Source file | Input | Output | Failure conditions | Retry behavior |
| --- | --- | --- | --- | --- | --- | --- |
| Product authoring form | Admin React product form | `PointOfSale.Web/src/components/ProductForm.jsx:272`, `PointOfSale.Web/src/components/ProductForm.jsx:300`, `PointOfSale.Web/src/components/ProductForm.jsx:515` | Draft form state | Product authoring payload | Variant create mode forces `publishStatus: 0`; invalid publish readiness blocks public state | Browser/user retry only |
| Authoring API | Products API | `PointOfSale.API/Controllers/ProductsController.cs` | Product authoring request | Call to orchestrator | Validation failure, tenant mismatch, idempotency conflict | HTTP caller retry/idempotency |
| Authoring transaction | Product authoring orchestrator | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:69`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:462` | Authoring command | SQL mutations plus staged integration events | Transaction rollback, idempotency conflict, validation failure | Transaction rolls back; client may retry idempotently |
| SQL product state | EF/PostgreSQL write model | `PointOfSale.Core/Entities/Product.cs:113`, `PointOfSale.Core/Entities/Product.cs:185`, `PointOfSale.Core/Entities/Product.cs:190`, `PointOfSale.Core/Entities/ProductVariant.cs` | Product aggregate | Committed relational source of truth | `Deleted`, inactive, draft/archived, offline, unsearchable, no tenant match, invalid category, variant product with no active priced variant | None; source of truth must be corrected by authoring |
| EF outbox write | MassTransit EF outbox | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:593`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:615`, `PointOfSale.API/Program.cs:1957`, `PointOfSale.API/Program.cs:1958`, `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1266` | Integration event publish call inside DbContext transaction | `OutboxMessage` row committed with product transaction | No current transaction, serialization failure, transaction rollback | Commit atomicity prevents event without product commit; dispatcher retries unsent outbox rows |
| RabbitMQ dispatch | MassTransit bus outbox/RabbitMQ | `PointOfSale.API/Program.cs:2008`, `PointOfSale.API/Program.cs:2030`, `PointOfSale.API/Program.cs:2055` | Committed outbox row | RabbitMQ delivered message | RabbitMQ unavailable, endpoint binding issue, consumer endpoint unavailable | MassTransit retry intervals: `1s`, `5s`, `15s`, `30s`; default RabbitMQ error queues after retry exhaustion |
| Mongo projection consumer | Product projection consumers | `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs:30`, `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs:88`, `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs:14`, `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs:75`, `PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs:18`, `PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs:138` | `ProductCreatedEvent`, `ProductUpdatedEvent`, `ProductPublishStatusChangedEvent`, delete/archive events | Projection writer call | Missing tenant/product is acknowledged without retry; Mongo/transient/unexpected exceptions are thrown | Consumer exceptions use MassTransit retry; permanent missing entity paths are ack/no retry |
| SQL authority overlay | Projection SQL recomposer | `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:74`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:176`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:292` | Event seed plus current SQL | Public `ProductDocument` or null/tombstone decision | Missing SQL product/category, inactive tenant, unpublished/offline product, variant product without active positive-priced variant | Called by consumer retry path when exceptions occur; null output is handled as tombstone |
| Mongo product projection | Mongo read model | `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:56`, `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:95`, `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:122`, `PointOfSale.Infrastructure/MongoDB/Services/MongoDbContext.cs:58`, `PointOfSale.Infrastructure/MongoDB/Services/MongoDbContext.cs:251` | SQL-authoritative product document | `ecommerce_read_db.products` document keyed by tenant/product | Admission rejects hidden product; mutation boundary rejects stale/out-of-order event; Mongo write failure | Exceptions retry by MassTransit; stale events are ignored by freshness fence |
| Elasticsearch consumer | Search projection consumers | `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs:135`, `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs:139`, `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs:115`, `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs:121`, `PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs:124` | Product event plus Mongo/SQL projection | Search index write/delete | Mongo document missing for create/update throws `ProjectionNotReadyException`; SQL overlay null deletes; hidden/unsearchable deletes | Exceptions retry through MassTransit intervals and then error queue |
| Elasticsearch index | Product search index service | `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:338`, `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs:46`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs:45`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs:79` | `ProductSearchDocument` | `products` index document `{TenantId}_{ProductId}` | Elasticsearch disabled/unavailable, index failure, document deleted because hidden | Index service returns false or throws; consumer retries failures |
| Discover API | Storefront products API | `PointOfSale.API/Controllers/StorefrontProductsController.cs:766`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs:93`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs:283`, `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs:554` | Tenant header plus discovery query | Public product list DTO | ES disabled/unavailable, no ES candidate, tenant/category/filter mismatch, SQL recomposition rejects candidate | API returns failure/503 on discovery exception; caller may retry |
| Storefront fetch/state | Storefront React hooks | `PointOfSale.Storefront/src/services/api/endpoints.ts:20`, `PointOfSale.Storefront/src/services/api/storefrontApi.ts`, `PointOfSale.Storefront/src/hooks/useProductDiscovery.ts:141`, `PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts:187` | Discover API response | Hook product state | Network failure, stale request discarded, filter dependency mismatch | Manual dependency-triggered refetch; no polling or focus-refresh path found |
| Catalog DOM | ProductListPage | `PointOfSale.Storefront/src/pages/ProductListPage.tsx:88`, `PointOfSale.Storefront/src/pages/ProductListPage.tsx:309` | Hook products array | `ProductCard` rendered in canonical catalog | Empty products array, loading/error path, user filters excluding product | Browser rerender on state update |

## Product Lifecycle

### Product Save

Source-backed behavior:

- The admin form builds an authoring payload in `buildProductAuthoringPayloadFromDraft`.
- For variant create mode, the frontend forces `publishStatus: 0` on the initial create payload.
- The orchestrator copies publication state from the command into the `Product` aggregate in `ApplyProduct`.
- Create and update paths call `StageAuthoritativeOutboxAsync` during the authoring transaction.

Expected event behavior:

- Initial create writes `ProductCreatedEvent`.
- Subsequent edits write `ProductUpdatedEvent`.
- Publish/online-visible transitions also write `ProductPublishStatusChangedEvent`.

Live certification for this product:

- Current SQL state is already published and publicly eligible.
- Therefore the original variant-create draft behavior is not the current visibility blocker.

### PostgreSQL Authority

Live probe result:

```text
SQL_PRODUCT_MATCH_COUNT=1
PRODUCT Id=d7160ce9-5bce-141e-61de-8e0f36db6085 TenantId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5 Name="Samsung Galaxy Note 10" IsDeleted=False IsActive=True IsOnlineVisible=True PublishStatus=1 IsSearchable=True HasVariants=True ProjectionMutationVersion=3 PublicGate=True
CATEGORY depth=0 Name="Samsung" IsDeleted=False
CATEGORY depth=1 Name="Smart Phones" IsDeleted=False
VARIANT_COUNT=4
ACTIVE_PRICED_CURRENT_VARIANT_COUNT=4
```

Certification:

- SQL visibility gates pass.
- Tenant isolation passes.
- Category lineage passes.
- Variant product admission passes.
- `ProductUnits` count is `0`, but this does not block a `HasVariants=True` product because variant pricing is the admission path.

### Outbox

Source-backed behavior:

- Product events are published inside the authoring transaction through `MassTransitEventPublisher`.
- `UsePostgres()` and `UseBusOutbox()` configure MassTransit EF outbox persistence.
- `OutboxMessage`, `OutboxState`, and `InboxState` entities are part of the EF model.
- Broker dispatch happens after the database commit, not before the SQL product commit.

Live probe result:

```text
OUTBOX_MATCH_COUNT=0
```

Interpretation:

- No pending matching `OutboxMessage` row remains for this product.
- This does not prove the event was never written, because MassTransit outbox rows may be dispatched and cleaned/advanced after commit.
- This also does not prove the consumer processed the event.

### MassTransit and RabbitMQ

Source-backed behavior:

- Consumers registered include `ProductCreatedEventConsumer`, `ProductUpdatedEventConsumer`, `ProductVisibilityLifecycleProjectionConsumer`, `ProductSearchIndexConsumer`, `ProductUpdatedSearchIndexConsumer`, and `ProductDeletedSearchIndexConsumer`.
- RabbitMQ endpoints are configured through `ConfigureEndpoints`.
- Retry intervals are configured as `1s`, `5s`, `15s`, and `30s`.
- No source evidence of a custom dead-letter policy for this path was found; MassTransit RabbitMQ defaults apply after retry exhaustion.

Runtime evidence:

- No RabbitMQ management/error-queue probe was performed.
- The earliest persisted projection output, Mongo, is missing.

### Mongo Projection

Expected source-backed behavior:

- `ProductCreatedEventConsumer` and `ProductUpdatedEventConsumer` pass the event to `ProductSqlAuthorityProjectionWriter`.
- The writer recomposes the product from SQL through `ProductSearchSqlAuthorityOverlay`.
- If SQL says the product is active and online visible, the writer upserts a `ProductDocument`.
- The Mongo collection is `products`, configured by `MongoDbSettings.ProductsCollectionName`.
- Unique projection identity is `(TenantId, ProductId)`.

Live probe result against runtime user-secret configuration:

```text
MONGO_FOUND=False Database=ecommerce_read_db Collection=products
```

Certification:

- The product has not reached the Mongo projection output.
- Since SQL authority passes, the product was lost between committed SQL/outbox dispatch and Mongo upsert.

### Elasticsearch

Expected source-backed behavior:

- Product create/update search consumers first require a Mongo product projection.
- If Mongo is missing, they throw `ProjectionNotReadyException` and retry.
- They then reapply SQL authority, map `ProductDocument` to `ProductSearchDocument`, and index into `products`.
- Search document id is `{TenantId}_{ProductId}`.
- Indexable documents must be active, online visible, and searchable.

Live probe result:

```text
ES_DOC_STATUS=404 Index=products DocumentId=83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5_d7160ce9-5bce-141e-61de-8e0f36db6085
ES_DOC_BODY={"_index":"products","_id":"83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5_d7160ce9-5bce-141e-61de-8e0f36db6085","found":false}
ES_SEARCH_STATUS=200
ES_VISIBLE_SEARCH_HITS=0
```

Certification:

- Elasticsearch does not contain the product.
- This is expected downstream behavior when Mongo projection is missing.

### Discover API

Source-backed behavior:

- `/api/storefront/products/discover` delegates to `ProductDiscoveryService.DiscoverAsync`.
- Discovery queries Elasticsearch, not Mongo, for initial candidates.
- It always filters `TenantId`, `IsActive=true`, `IsOnlineVisible=true`, and `IsSearchable=true`.
- It then recomposes candidates through SQL authority and drops any product that fails SQL visibility/filter checks.

Certification:

- The product cannot be returned by `/discover` because it has no Elasticsearch candidate.
- Discover API is not the earliest failing stage.

### React and DOM

Source-backed behavior:

- Storefront calls `PRODUCTS_DISCOVER`, mapped to `/api/storefront/products/discover`.
- `useProductDiscovery` fetches and stores the response in local hook state.
- It discards stale responses by request id.
- No React Query cache, module-level product cache, polling, or focus-refresh logic was found in the canonical catalog path.
- `ProductListPage` owns the canonical catalog and maps discovered products into `ProductCard`.

Certification:

- Stale frontend state could delay visibility after a backend projection appears, because no focus refresh or polling path was found.
- It cannot explain this runtime failure, because the backend candidate is absent from Elasticsearch.

## Possible Failure Matrix

| Boundary | Evidence for this product | Can explain current failure? | Source-backed reason |
| --- | --- | --- | --- |
| Authoring form created product as draft | Current SQL `PublishStatus=1` | No | The product is now published and online visible |
| SQL product visibility flags | SQL public gate is true | No | `IsActive=True`, `IsOnlineVisible=True`, `IsSearchable=True`, `PublishStatus=1`, `IsDeleted=False` |
| Tenant isolation | Same tenant across product/category/variants | No | Live probe used tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Category lineage | Category chain exists and not deleted | No | `Samsung` -> `Smart Phones` both active |
| Variant admission | Four active current priced variants | No | Overlay requires at least one active positive-priced variant for `HasVariants=True` |
| Product units | `UNIT_COUNT=0` | No for this product | Unit admission applies to simple products; this product uses variant admission |
| EF outbox pending row | No pending row found | Indeterminate | Dispatched/cleaned outbox rows are not retained as proof of delivery |
| RabbitMQ dispatch/endpoint binding | Not directly probed | Yes | Mongo output is missing and consumers depend on broker delivery |
| Projection consumer receipt | Not directly probed | Yes | No Mongo output means consumer did not successfully produce projection |
| SQL authority overlay | SQL data satisfies overlay gates | Unlikely | Product has category lineage and active priced variants |
| Mongo mutation boundary | No document found | Yes | This is the first missing persisted output |
| Elasticsearch indexing | No ES document found | Downstream only | Create/update ES consumers require Mongo readiness before indexing |
| Discover API filters | No ES candidate exists | No | Discover cannot return a missing ES document |
| React cache/state | Backend result absent | No | React cannot render a product absent from the API result source |

## Required Runtime Probes

Minimum additional instrumentation, if exact substage attribution inside the SQL-to-Mongo handoff is required:

1. Product authoring transaction/outbox probe
   - Log event type, event id, tenant id, product id, event version, and idempotency key immediately after each `PublishAsync` call in `StageAuthoritativeOutboxAsync`.
   - Purpose: confirm the product event was staged in the same SQL transaction.

2. MassTransit dispatch probe
   - Log outbox sequence/message id when the bus outbox dispatches `ProductCreatedEvent`, `ProductUpdatedEvent`, or `ProductPublishStatusChangedEvent`.
   - Purpose: confirm the event left PostgreSQL outbox and reached RabbitMQ.

3. Projection consumer receive probe
   - Log at consumer start for `ProductCreatedEventConsumer`, `ProductUpdatedEventConsumer`, and `ProductVisibilityLifecycleProjectionConsumer`: message id, event id, tenant id, product id, event version.
   - Purpose: confirm the projection service received the message.

4. Projection writer decision probe
   - Log one decision from `ProductSqlAuthorityProjectionWriter`: `Upserted`, `Tombstoned`, `Ignored`, or exception, with product id and tenant id.
   - Purpose: confirm whether SQL overlay admitted the product and whether Mongo accepted the mutation.

5. RabbitMQ error queue probe
   - Inspect the endpoint error queues for the traced product id or event id.
   - Purpose: distinguish undelivered/retried messages from successfully acknowledged but non-upserting consumers.

These probes are intentionally limited to the failing boundary. No frontend, Discover API, Elasticsearch redesign, or broad platform instrumentation is required for this certification.

## Most Probable Failure Stage

The most probable failing stage is the Product Projection Runtime before Mongo output: either the authoritative product event did not reach the projection consumer, or the projection consumer/writer did not successfully upsert `ecommerce_read_db.products` for the traced tenant/product key.

The earliest certified missing stage is Mongo projection, because SQL authority passes and Mongo has no document. Elasticsearch, Discover API, React, and DOM are downstream of that missing Mongo output.

## Certification

The product visibility failure occurs BEFORE stage Mongo projection.
