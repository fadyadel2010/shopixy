# ARCH-4 CommerceHub Event & Read Model Architecture Review

Date: 2026-06-25
Status: PASS - Production-certifiable

## Runtime Pipeline Reconstructed

The inspected CommerceHub runtime pipeline is SQL business commit -> SQL transactional outbox / MassTransit bus outbox -> RabbitMQ transport -> durable projection outbox / fenced consumers -> SQL-rebuilt Mongo read models -> Redis cache admission -> Elasticsearch search projection -> frontend presentation.

The graph is not authority-moving. SQL and owning backend services remain the only business authority. Every later stage either sequences, transports, projects current SQL truth, caches current projection evidence, indexes SQL-overlaid search documents, or presents backend responses.

## Outbox Findings

- `MarketplaceEventOutboxService` creates outbox rows inside the caller's SQL unit of work and does not save independently.
- `MassTransitEventPublisher` blocks authoritative event publish outside a transactional/pending SQL mutation boundary.
- `MarketplaceEventOutboxProcessor` owns per-aggregate sequencing, claims, retries, and publish marking only; it does not mutate business SQL.
- Failed earlier same-aggregate events block later same-aggregate events, so outbox retry cannot reorder authority.
- Outbox cannot create, recreate, mutate, lose, or own business authority.

## RabbitMQ Findings

- RabbitMQ is configured through MassTransit endpoint conventions and retry policy only.
- Exchanges, queues, redelivery, delayed retry, and fault messages are transport/delivery mechanics.
- RabbitMQ owns no SQL mutation boundary and cannot become queue authority or recreate authority through redelivery.

## Consumer Findings

- Marketplace backbone consumers stage `MarketplaceProjectionOutboxMessage` rows behind SQL duplicate checks.
- Projection consumers rebuild product, order, and homepage read models from current SQL through projection services.
- Fault monitor consumers alert only.
- Consumers do not mutate foreign domains and do not bypass owning domain services for business truth.

## Mongo Findings

- Mongo product/order/homepage documents are projections.
- Projection services read current SQL product, listing, variant, stock, price, category, tenant, order, and homepage authority before upsert/delete.
- Mongo repositories reject stale upsert/delete attempts through entity mutation version and event freshness filters.
- Mongo cannot repair SQL, restore checkout/payment/shipment/settlement truth, or become business truth.

## Redis Findings

- Redis stores cache envelopes with projection/source timestamps and schema metadata.
- Cache reads fail open to backend population, delete corrupt entries, and bypass stale projection-lagged entries.
- Marketplace read keys include tenant/customer/search scope, and homepage cache output is re-filtered against current SQL visibility.
- Redis cannot restore authority, recreate authority, or widen tenant/customer scope.

## Elasticsearch Findings

- `CommerceHubSearchSqlAuthorityOverlay` recomposes search documents from current SQL listing, product, variant, category, stock, pricing, tenant, vendor, and tombstone authority.
- Reindex/catch-up paths apply SQL overlay before indexing and delete documents missing current SQL-visible authority.
- Alias promotion is gated by SQL-visible document count verification.
- Elasticsearch owns search only; it cannot own catalog, restore catalog, restore checkout, or restore authority.

## Frontend Findings

- React Query clients are runtime caches only.
- POS auth bootstrap clears query/runtime caches on actor-scope change and revalidates on focus, online, popstate, visibility, and BFCache pageshow.
- Storefront checkout order/payment identity is deliberately not restorable from browser storage or route state.
- Local/session storage stores presentation preferences, checkout draft/candidate data, session ids, affiliate refs, or UX state only; backend APIs revalidate authority.
- Frontend cannot recreate truth, events, projections, or deleted authority.

## Projection Findings

- Projection ownership is derivative: consumers request rebuild, projection services read SQL, repositories apply freshness filters.
- `ProjectionEventFence` and `MarketplaceProjectionOutboxProcessor` collapse duplicate/replayed events with SQL processed-event records.
- Projection writes can stop presentation freshness, but cannot stop or originate business authority.

## Cache Findings

- Cache ownership is admission and invalidation only.
- Caches may serve bounded stale presentation when accepted by lag/freshness policy; they cannot mutate SQL or recover authority.

## Replay Findings

- Replay re-enters event fences, projection outbox idempotency, SQL rebuild guards, and SQL overlays.
- Replay may repair projections/search from current SQL; it cannot recreate business truth.

## Recovery Findings

- Order projection repair builds Mongo documents from current SQL marketplace-order/sub-order authority and fails closed unless the existing projection proves SQL authority.
- Search recovery/reindex applies SQL authority overlays and deletes stale documents absent from SQL-visible authority.
- Recovery returns to SQL or fails closed; it cannot recreate authority.

## Rebuild Findings

- `SqlTruthRebuildGuard` makes SQL authority explicit for CommerceHub projection/search/cache/rebuild callers.
- Projection rebuilds and search rebuilds can refresh or delete derivative records only.
- Rebuild cannot skip SQL, merge stale projections into truth, or restore deleted authority.

## Event Ordering Findings

- Outbox ordering is per aggregate stream using version, occurred time, created time, and id; earlier unpublished same-aggregate events block later events.
- Mongo freshness filters reject late/stale writes.
- Event ordering can affect projection lag, but not business authority.

## Eventual Consistency Findings

- Checkout, payment, shipment, refund, settlement, and payout business correctness is owned by backend SQL/service boundaries.
- Eventual consistency can delay read/search/cache/frontend presentation only.
- Stale projections cannot become truth and lag cannot change authority.

## Failure Findings

- Broker, consumer, Mongo, Redis, and Elasticsearch failures degrade delivery or presentation.
- Failures do not promote projections, caches, search, queues, or frontend state into authority.
- Recovery paths re-enter SQL or fail closed.

## Tenant Findings

- Outbox events require tenant identity and currency/tenant guards.
- Projection and cache reads are tenant/customer scoped.
- Search document identity includes tenant/listing identity and stale delete supports tenant scope.
- Tenant boundaries were not found to leak through projections, cache, search, or frontend presentation.

## Cross-Domain Findings

- Consumers project their own read models only.
- Downstream read/search/cache/frontend surfaces cannot mutate upstream product, checkout, payment, shipment, refund, settlement, payout, or catalog authority.
- Cross-domain recovery uses owning SQL/service authority as entrypoint.

## Architectural Risks

- Residual risk is operational proof, not inspected runtime design: deployed chaos should continue to exercise broker outages, retry storms, Redis/Mongo/Elasticsearch tampering, delayed/duplicate events, projection rebuild races, BFCache restoration, and multi-tab tenant/actor changes.
- Future event or read-model additions must preserve SQL rebuild/overlay/fence/cache-admission rules and update this freeze.

## Runtime Hardening Performed

- None. Runtime changes were not required.

## Exact Runtime Files Modified

- None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubEventReadModelArchitectureCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-event-read-model-architecture-review-findings.md`

## Final Questions

- Who owns Event Authority? SQL business commits and owning backend services own event authority; outbox owns sequencing/delivery evidence only.
- Who owns Projection Authority? SQL owns projection source authority; projection services own derivative write mechanics only.
- Can Events become authority? No.
- Can Outbox become authority? No.
- Can RabbitMQ become authority? No.
- Can Consumers become authority? No.
- Can Mongo become authority? No.
- Can Redis become authority? No.
- Can Elasticsearch become authority? No.
- Can Frontend become authority? No.
- Can Replay recreate authority? No.
- Can Rebuild recreate authority? No.
- Can Recovery recreate authority? No.
- Can Projection Repair recreate authority? No.
- Can Event Ordering affect authority? No.
- Can Eventual Consistency affect authority? No.
- Can Cache become authority? No.
- Can Tenant boundaries leak through projections? No inspected path.
- Were runtime changes required? No.
- Is CommerceHub Event & Read Model Architecture production-certifiable? Yes.
