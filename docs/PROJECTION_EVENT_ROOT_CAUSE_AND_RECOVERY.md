# Projection Event Root Cause and Recovery

**Certification date:** 2026-08-01 (Africa/Cairo)  
**Repository:** `PointOfSale`  
**Traced tenant:** `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`  
**Traced product:** `d7160ce9-5bce-141e-61de-8e0f36db6085` (`Samsung Galaxy Note 10`)  
**Recovery correlation:** `projection-recovery-6ee495e53bae464f8a76691798d51c84`

## Executive Summary

The canonical `PointOfSale.Core.Events.ProductUpdatedEvent` was created and its
publisher was called. It disappeared because `MassTransitEventPublisher` passed
the event to MassTransit through a generic overload whose compile-time message
type was the abstract base `IntegrationEvent`:

```csharp
await _publishEndpoint.Publish(@event, cancellationToken);
```

The private helper parameter is declared as `IntegrationEvent`. MassTransit
therefore staged the EF bus-outbox envelope with:

```text
urn:message:PointOfSale.Core.Events:IntegrationEvent
```

It did not stage the concrete contract:

```text
urn:message:PointOfSale.Core.Events:ProductUpdatedEvent
```

The bus outbox dispatched exactly what it was given, to the base
`IntegrationEvent` exchange. RabbitMQ cannot route a publish on the base
exchange backward into a derived `ProductUpdatedEvent` exchange, so none of the
canonical product consumers received the message. This explains the certified
`ProductUpdatedEvent` exchange counter of `0`, the absent Mongo document, and
the absent Elasticsearch document.

The surgical fix passes the already-known runtime type to MassTransit:

```csharp
await _publishEndpoint.Publish(@event, eventType, cancellationToken);
```

No authoring behavior, event contract, consumer, queue, exchange name, Mongo
code, Elasticsearch code, or topology configuration changed.

## Runtime Evidence

The following canonical reports were read before source changes and were used
as established runtime evidence:

- `STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md`
- `STOREFRONT_RUNTIME_RECOVERY_MASTER_PLAN.md`
- `RECOVERY-0_CATALOG_OWNERSHIP.md`
- `PROJECTION_RUNTIME_CERTIFICATION.md`
- `PROJECTION_EVENT_RUNTIME_TRACE_REPORT.md`

Their certified boundary was preserved: product authoring and SQL commit
succeeded, the marketplace event was published and consumed, but the canonical
RabbitMQ exchange remained `publish_in 0 -> 0`.

The decisive additional evidence was captured at the MassTransit EF bus-outbox
boundary. Before the fix, the new regression observed the actual staged
`OutboxMessage.MessageType` and failed:

```text
Actual:   urn:message:PointOfSale.Core.Events:IntegrationEvent
Expected: urn:message:PointOfSale.Core.Events:ProductUpdatedEvent
```

That result rules out event construction, a skipped publisher call, an outbox
rejection, missing consumer registration, and a RabbitMQ outage. The outbox
accepted a message, but it accepted the wrong MassTransit contract identity.

After the fix, a real API update advanced:

```text
RowVersion:                 728832 -> 729791
ProjectionMutationVersion: 5 -> 6
Canonical Rabbit publish_in: 0 -> 1
```

The same correlation then appeared in canonical consumer processing evidence:

```text
ProductUpdatedSearchIndexConsumer
ProductCacheInvalidationConsumer:ProductUpdatedEvent
EventType=PointOfSale.Core.Events.ProductUpdatedEvent
EventId=93c60ab4-1b2b-4df0-bb1a-f89ef61d0311
```

Downstream state converged:

```text
Mongo: Found=True, LastEventVersion=6,
       LastEventId=93c60ab4-1b2b-4df0-bb1a-f89ef61d0311
Elasticsearch: HTTP 200 for
       83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5_d7160ce9-5bce-141e-61de-8e0f36db6085
Storefront /discover: HTTP 200, MatchCount=1
Rabbit queues: running, one consumer on each canonical update queue, no error queue
```

## Call Graph

### HTTP action to authoring transaction

The complete live update path is:

```text
ASP.NET Core endpoint dispatch
  -> ProductsController.UpdateProduct(Guid, UpdateProductRequest)
     -> RequireProductMutationIdempotencyKey()
     -> RequireExpectedVersion()
     -> ProductAuthoringCommandMapper.FromUpdateRequest(...)
        -> BuildProductAuthoringCommand(...)
           -> CurrentTraceIdentifier()
           -> ReadIdempotencyKey()
           -> ReadExpectedVersion()
     -> ProductAuthoringOrchestrator.AuthorAsync(command)
        -> RequireProductAuthoringIdempotency(command)
        -> ProductAuthoringReconstructionService.ReconstructForAdmissionAsync(command)
           -> LoadCurrentUnitsAsync(...) when units were omitted
           -> LoadCurrentAuthoringVariantsAsync(...) when variants were omitted
        -> AdmitAsync(command)
           -> LoadAdmissionFactsAsync(command)
              -> CategoryLineageAuthority.RequireLiveCategoryAsync(...)
           -> IProductAuthoringPolicy.EvaluateAdmissionAsync(...)
        -> ExecuteUpdateAsync(command)
           -> LoadTrackedProductAsync(...)
           -> IProductAuthoringPolicy.EvaluateLifecycleAsync(...)
           -> TryResolveExistingProductAuthoringIdempotencyAsync(...)
           -> IsStale(command, product)
           -> ProductVariantCloneImportAuthority
              .IsBarcodeOwnedByAnotherSemanticEntityAsync(...)
           -> IProductAuthoringPolicy.EvaluateSimpleProductIdentityAsync(...)
           -> ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(...)
           -> LoadTenantUnitLookupAsync(...) when units were submitted
           -> IProductAuthoringPolicy.EvaluateUnitReferencesAsync(...)
           -> CommitProductAuthoringTransactionAsync(...)
              -> ApplicationDbContext.Database.BeginTransactionAsync(...)
              -> TryClaimProductAuthoringIdempotencyAsync(...)
                 -> IProductMutationIdempotencyService.TryClaimAsync(...)
              -> mutateAndStageAsync callback
                 -> ApplyProduct(...)
                 -> SyncAuthoredProductUnitsAsync(...) when submitted
                 -> SyncAuthoredVariantsAsync(...) when submitted
                 -> IProductVariantService.EnsureDefaultVariantAlignedAsync(...)
                 -> ProductVariantCloneImportAuthority
                    .ApplySimpleProductIdentityFromDefaultVariant(...)
                 -> ApplyProductPricingCompatibilityMirror(...)
                 -> StageAuthoritativeOutboxAsync(...)
              -> ApplyExpectedProductRowVersion(...)
              -> CompleteProductAuthoringIdempotency(...)
              -> StageProductAuthoringAudit(...)
              -> IProductAuthoringTransactionObserver.BeforeCommitAsync(...)
              -> ApplicationDbContext.SaveChangesAsync(...)
              -> IDbContextTransaction.CommitAsync(...)
              -> IProductAuthoringTransactionObserver.AfterCommitAsync(...)
     -> LoadProductForResponseAsync(...)
     -> Product.ToResponseDto()
```

### Canonical event to MassTransit

Inside `StageAuthoritativeOutboxAsync` for an admitted non-create mutation:

```text
StageAuthoritativeOutboxAsync(...)
  -> PendingProductMutationVersion(product)
  -> ProductEventVersionHelper.ToProductEventVersion(...)
  -> BuildProductUnitEventDataAsync(...)
  -> ProductChangedFields()
  -> new PointOfSale.Core.Events.ProductUpdatedEvent
  -> IEventPublisher.PublishAsync<ProductUpdatedEvent>(event)
  -> MassTransitEventPublisher.PublishAsync<ProductUpdatedEvent>(event)
  -> PublishIntegrationEventAsync(IntegrationEvent event,
                                  typeof(ProductUpdatedEvent), ...)
     -> EnrichEventWithTenantContext(...)
     -> EnrichAuthoritativeOrderingMetadataAsync(...)
     -> EnsureAuthoritativeTenantScope(...)
     -> EnsureTransactionalPublishBoundary(...)
     -> IPublishEndpoint.Publish(event, eventType, cancellationToken)
  -> MassTransit EF bus outbox intercepts IPublishEndpoint
  -> OutboxMessage is persisted by the same ApplicationDbContext.SaveChangesAsync
  -> source transaction commits
  -> MassTransit bus-outbox delivery service dispatches the envelope
  -> RabbitMQ PointOfSale.Core.Events:ProductUpdatedEvent exchange
  -> product-updated-event / product-updated-search-index / other bound queues
  -> ProductUpdatedEventConsumer.Consume(...)
     -> IProductSqlAuthorityProjectionWriter.ApplyAsync(...)
     -> Mongo products upsert
  -> ProductUpdatedSearchIndexConsumer.Consume(...)
     -> ProductSearchSqlAuthorityOverlay.ApplyAsync(...)
     -> IProductSearchIndexService.IndexAsync(...)
     -> Elasticsearch products document
  -> StorefrontProductsController.DiscoverProducts(...)
     -> IProductDiscoveryService.DiscoverAsync(...)
     -> Elasticsearch candidate plus SQL authority overlay
     -> Storefront ProductListPage
```

### Injected dependencies

`ProductsController` receives `ApplicationDbContext`, `ITenantContext`,
`ILogger<ProductsController>`, `IBarcodeService`, `IValidationService`,
`IAuditService`, `IProductVariantService`, `IEventPublisher`,
`IMarketplaceEventOutboxService`, `IProductAuthoringOrchestrator`,
`IProductAuthoringCommandMapper`, `IProductAuthoringReconstructionService`, and
`IProductMutationIdempotencyService`. Only the mapper and orchestrator are on
the live `UpdateProduct` path after the controller preconditions; the other
dependencies serve other controller actions.

`ProductAuthoringOrchestrator` receives `IProductAuthoringPolicy`,
`ApplicationDbContext`, `ITenantContext`, `IProductVariantService`,
`IEventPublisher`, `IMarketplaceEventOutboxService`,
`ILogger<ProductAuthoringOrchestrator>`, `IHttpContextAccessor`,
`IProductMutationIdempotencyService`, `IProductAuthoringTransactionObserver`,
and `IProductAuthoringReconstructionService`.

`MassTransitEventPublisher` receives `ApplicationDbContext`, `IBus`,
`IPublishEndpoint`, `ILogger<MassTransitEventPublisher>`, and `ITenantContext`.
The canonical path uses the scoped `IPublishEndpoint`, which is intercepted by
`UseBusOutbox()`; it does not bypass MassTransit.

## Marketplace Event Flow

### Live producer

`ProductAuthoringOrchestrator.StageAuthoritativeOutboxAsync` enqueues
`PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent` for every admitted
update, archive, or remove operation after the create-only branch returns.

Call stack:

```text
ProductsController.UpdateProduct
  -> ProductAuthoringOrchestrator.AuthorAsync
  -> ExecuteUpdateAsync
  -> CommitProductAuthoringTransactionAsync
  -> StageAuthoritativeOutboxAsync
  -> IMarketplaceEventOutboxService.EnqueueAsync<Marketplace.ProductUpdatedEvent>
  -> MarketplaceEventOutboxService.EnqueueAsync
  -> ApplicationDbContext tracks MarketplaceEventOutboxMessage
  -> same SaveChanges/transaction commit as Product
  -> MarketplaceEventOutboxWorker
  -> MarketplaceEventOutboxProcessor
  -> PublishAsync(IPublishEndpoint, message)
  -> ResolveType(message.ClrType)
  -> JsonSerializer.Deserialize(payload, eventType)
  -> IPublishEndpoint.Publish(payload, eventType, ...)
  -> RabbitMQ marketplace ProductUpdatedEvent exchange
```

Conditions and behavior:

- It is not controlled by a feature flag.
- It is not conditional on product visibility, publish status, product type,
  variants, inventory, or `HasVariants`.
- It uses the same product `ProjectionMutationVersion` converted to the same
  event version as the canonical event.
- It requires an admitted non-create authoring operation and a non-empty
  correlation ID.
- Admission, lifecycle, identity, unit validation, stale version, idempotency
  replay, or transaction-claim early returns prevent both product-updated
  events.
- Enqueue and product mutation are atomic in the authoring SQL transaction.
- Dispatch uses a separate custom marketplace outbox worker after commit.
- Ordering/idempotency use event ID, aggregate/entity ID, tenant ID, version,
  correlation ID, claim leases, retry state, and processed-event records.
- Crucially, dispatch supplies the stored concrete CLR `Type` explicitly.

### Other marketplace construction sites

`ProductVariantsController.UpdateVariant` contains a marketplace product
update producer, conditional on a simple-product compatibility mirror change.
The method returns `LegacyVariantMutationDeprecatedResult()` before that block,
so it is unreachable runtime code.

`ProductsController.MigrateToMultiUnit` contains a per-product marketplace
producer, conditional on a migrated product having its new unit. That action
also returns `LegacyProductMigrationDeprecatedResult()` before the block, so it
is unreachable runtime code.

`ProjectionFailureCompensationService.BuildProductUpdatedReplayAsync` can
reconstruct a marketplace product-update replay envelope for an already failed
marketplace outbox message. It is a compensation/replay path, not a normal
product-update publication site and it does not publish the canonical event.

## Canonical Event Flow

### Live producer

`ProductAuthoringOrchestrator.StageAuthoritativeOutboxAsync` constructs and
publishes `PointOfSale.Core.Events.ProductUpdatedEvent` immediately after the
marketplace enqueue for every admitted non-create operation.

Conditions and behavior:

- It is not controlled by a feature flag.
- It has no separate visibility, publish-status, variant, product-type, price,
  inventory, or searchability gate.
- It uses the pending product projection mutation version.
- It includes product visibility/publication data in the payload, but those
  fields do not decide whether it is published.
- It uses `IEventPublisher`, registered as `MassTransitEventPublisher`.
- `EnsureAuthoritativeTenantScope` requires valid tenant authority.
- `EnsureTransactionalPublishBoundary` requires an active transaction or
  pending business mutation for authoritative events.
- The scoped MassTransit `IPublishEndpoint` uses the EF bus outbox; product,
  idempotency, audit, marketplace outbox, and canonical outbox rows are saved in
  the same PostgreSQL transaction.
- Its deterministic authoring idempotency key includes the product, operation,
  request key, event kind, and event version. Authoring replay returns before a
  second event is staged.
- Before recovery, the event object was created and `PublishAsync` was called,
  but the staged MassTransit contract was the abstract base type.

### Other canonical construction sites

`ProductVariantsController.UpdateVariant` contains a canonical
`ProductUpdatedEvent` producer when a simple-product mirror changed. It is in
the same unreachable legacy block described above.

`ProductsController.MigrateToMultiUnit` contains a canonical producer for unit
migration. It is also in an unreachable legacy block.

No other production source constructs and publishes the canonical product
update contract.

## Side-by-Side Comparison

| Question | Marketplace event | Canonical event |
|---|---|---|
| Always produced for an admitted non-create authoring mutation? | Yes | Yes |
| Visibility conditional? | No | No |
| Publish-status conditional? | No | No |
| Product type conditional? | No | No |
| Variant existence conditional? | No | No |
| Inventory conditional? | No | No |
| Uses product projection mutation version? | Yes | Yes |
| Tenant scoped? | Yes | Yes |
| Same source SQL transaction? | Yes | Yes |
| Source persistence | Custom `MarketplaceEventOutboxMessage` | MassTransit EF `OutboxMessage` |
| Dispatcher | `MarketplaceEventOutboxWorker` | MassTransit bus-outbox delivery service |
| Publisher | `IPublishEndpoint` in custom processor | `IEventPublisher` -> `MassTransitEventPublisher` |
| Concrete runtime type supplied? | Yes: `Publish(payload, eventType, ...)` | No before fix; yes after fix |
| Broker exchange before fix | Marketplace concrete exchange | Abstract `IntegrationEvent` exchange |
| Idempotency | Custom outbox event/aggregate/version plus `ProcessedEvents` | Authoring request record, event id/key, MT outbox/inbox |
| Consumer result in certified failing run | Published and consumed | No concrete consumer delivery |

Both events were supposed to be unconditional peers after authoring admission.
The only causal differential was their dispatch overload: the marketplace
processor preserved its concrete stored CLR type, while the canonical publisher
erased it to `IntegrationEvent`.

## Root Cause

There is exactly one root cause:

> `MassTransitEventPublisher.PublishIntegrationEventAsync` called the generic
> MassTransit publish overload with an expression statically typed as
> `IntegrationEvent`, causing the EF bus outbox to persist and dispatch the
> abstract base contract instead of the concrete `ProductUpdatedEvent`
> contract.

The canonical event disappeared at outbox entry creation. It was not filtered
or rejected. The wrong `MessageType` was written successfully. The dispatcher
then published that wrong contract successfully to the wrong exchange.

This also explains the Rabbit topology evidence. The concrete
`ProductUpdatedEvent` exchange has the registered consumer bindings and an
inheritance binding toward the base `IntegrationEvent` exchange. Routing flows
from a derived publish toward base consumers; it does not flow backward from a
base publish to derived consumers.

## Dependency Registration

Dependency and topology registration are correct:

- `AddProductAuthoringInfrastructure()` registers the mapper, reconstruction
  service, idempotency service, policy, and `IProductAuthoringOrchestrator`.
- `AddMarketplaceEventBackbone()` registers the custom marketplace outbox
  service, processor, workers, and replay support.
- `AddMassTransit()` registers `ProductUpdatedEventConsumer`,
  `ProductVisibilityLifecycleProjectionConsumer`,
  `ProductUpdatedSearchIndexConsumer`, `ProductCacheInvalidationConsumer`,
  affiliate consumers, and CommerceHub projection consumers.
- `AddEntityFrameworkOutbox<ApplicationDbContext>()` uses PostgreSQL,
  `UseBusOutbox()`, a one-second query delay, and a five-minute duplicate
  detection window.
- `ApplicationDbContext.OnModelCreating` registers MassTransit inbox,
  outbox-message, and outbox-state entities.
- RabbitMQ host, credentials, retry intervals, publish filters, kebab-case
  endpoint names, and `ConfigureEndpoints(context)` are registered.
- `IEventPublisher` is scoped to `MassTransitEventPublisher`.
- Live RabbitMQ showed the concrete exchange, all expected bindings, running
  queues, and active consumers.

`ProductUpdatedEvent` did not need an additional producer registration. Its
concrete consumers and topology were already present, and the post-fix concrete
publish reached them without any DI or topology change.

## Files Changed

### Production

- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
  - One line: use `Publish(object, Type, CancellationToken)` with the existing
    `eventType` instead of the base-typed generic overload.

### Regression

- `PointOfSale.Tests/Infrastructure/MassTransitEventPublisherRuntimeTypeTests.cs`
  - Uses the real `MassTransitEventPublisher`, MassTransit test harness, SQLite
    EF bus outbox, real `OutboxMessage`, an explicit transaction, and a concrete
    `ProductUpdatedEvent` consumer probe.
  - Asserts the staged outbox contract before commit and concrete consumer
    receipt after commit.

### Report

- `PROJECTION_EVENT_ROOT_CAUSE_AND_RECOVERY.md`

Other dirty-worktree files predated this recovery and were not reverted or
claimed as part of the surgical fix.

## Why This Is the Smallest Possible Fix

The publisher already receives the correct concrete `Type` as `eventType`.
Using the MassTransit overload that accepts that value repairs only contract
identity at the exact loss point. The change does not add a branch, new service,
new event, new queue, new exchange, new consumer, new transaction, new outbox,
or new projection behavior.

Changing the producer, consumer, topology, RabbitMQ, MongoDB, Elasticsearch, or
event contracts would treat downstream symptoms and widen the blast radius.
None is necessary; the live post-fix dispatch proved the existing architecture
works once the concrete type is preserved.

## Regression Tests

`MassTransitEventPublisherRuntimeTypeTests.ProductUpdatedEvent_PreservesConcreteContract_ThroughEfBusOutboxDispatch`
proves:

```text
canonical ProductUpdatedEvent object
  -> real IEventPublisher implementation
  -> real EF bus-outbox interception
  -> concrete ProductUpdatedEvent MessageType in OutboxMessage
  -> transaction commit
  -> MassTransit dispatcher
  -> concrete ProductUpdatedEvent consumer receipt
```

Pre-fix result: failed because `OutboxMessage.MessageType` contained only the
abstract `IntegrationEvent` URN.  
Post-fix result: passed, 1/1.

The existing authoring transaction tests prove the accepted update constructs
the canonical event within the source transaction. Existing Mongo and
Elasticsearch convergence suites protect the registered consumer/writer paths.
The live correlated validation completes the production chain by proving the
actual Mongo document, Elasticsearch document, and Storefront result.

## Validation Results

### Automated validation

- `dotnet build PointOfSale.sln --no-restore`
  - Passed, 0 warnings, 0 errors.
- Focused transport/outbox/authoring-transaction/Mongo/Elasticsearch suites
  - Passed 52/52.
- New MassTransit runtime-type regression by itself
  - Passed 1/1 after failing 1/1 before the fix.
- Storefront catalog ownership and catalog-section tests
  - Passed 6/6.
- `npm run build` in `PointOfSale.Storefront`
  - Passed; existing Vite chunk/import warnings remain.
- `npm run build` in `PointOfSale.Web`
  - Passed.

Two unrelated existing assertions remain red in the dirty workspace and were
not broadened into this transport recovery:

- `ProductAuthoringProducerCompletenessIntegrationTests`: 7 passed, 1 failed.
  The emitted event exists, but the test expects default-unit payload details
  that are currently absent.
- `ProductSqlAuthorityProjectionWriterTests`: 9 passed, 1 failed. The test
  expects recomposed sale price `42`; current result is `0`.

Neither failure is changed by the MassTransit publish overload, and live Mongo,
Elasticsearch, and Storefront convergence succeeded for the traced product.

### Live production-path validation

With the API and registered MassTransit consumers running against the configured
PostgreSQL, RabbitMQ, MongoDB, Elasticsearch, and Redis services:

1. `PUT /api/products/d7160ce9-5bce-141e-61de-8e0f36db6085` returned success.
2. SQL row version advanced `728832 -> 729791`.
3. Projection mutation version advanced `5 -> 6`.
4. The certified canonical exchange baseline advanced `publish_in 0 -> 1` and
   reported `publish_out=6`.
5. Canonical `ProductUpdatedEvent` consumer records carried correlation
   `projection-recovery-6ee495e53bae464f8a76691798d51c84`.
6. Mongo `ecommerce_read_db.products` contains the product with
   `LastEventVersion=6`.
7. Elasticsearch `products` returns HTTP 200 for the tenant/product document.
8. `GET /api/storefront/products/discover?query=Samsung%20Galaxy%20Note%2010`
   returned HTTP 200 and one exact product match.
9. Canonical queues were running with active consumers, zero pending messages,
   and no canonical error queues.

The delivered EF outbox rows were cleaned after successful dispatch, as
expected. The regression test captures and asserts the concrete row before
commit; the live Rabbit counter and correlated consumer records prove its
successful dispatch.

## Certification

The canonical event loss is recovered and certified.

The event was never missing from product authoring. It was never rejected by
the outbox. It was not filtered by product visibility, publication state,
variants, inventory, tenant feature flags, consumer registration, or exchange
topology. Its MassTransit contract identity was erased at the publisher-to-
outbox handoff.

The one-line runtime-type publish fix preserves
`PointOfSale.Core.Events.ProductUpdatedEvent` through the EF bus outbox and
RabbitMQ. The correlated live update now reaches canonical consumers, creates
the Mongo document, creates the Elasticsearch document, and appears through
the canonical Storefront discovery path.
