# M3-A Projection Readiness Audit — Shopixy Load Harness (AUDIT ONLY)

> No load testing started. No k6 scenarios added. No production/business code
> changed. No Mongo/ES writes. No RabbitMQ publishes. No provider calls.
> No sync/reindex route was invoked. One read-only storefront GET was issued
> as current-state evidence (§2). Everything else is static code inspection
> with file:line references.

## 1. Executive summary

- The M2 SQL universe (`loadtest-tenant-a/b/c`, 60 products, 66 variants,
  69 stock rows) is **invisible to every Mongo/ES-backed read path today**:
  direct SQL inserts fire no domain events (confirmed: no triggers on
  `Products/ProductVariants/ProductVariantStocks`), there is no startup
  sync, and no generic event replay from SQL state exists.
- A safe, tenant-scoped, non-destructive population path **exists** using
  first-party admin sync endpoints (Mongo) + tenant-scoped search sync
  endpoints (ES) + the production publish workflow (storefront gate). All
  recommended calls are per-tenant, authenticated as that tenant's
  Owner/Admin, create-or-upsert only, and never delete an index.
- Additional M3-B prerequisite discovered: anonymous storefront reads are
  blocked by `STOREFRONT_NOT_PUBLISHED` (404) because M2 tenants have no
  `StoreConfigs` row with `StorefrontStatus='live'`. Publishing must go
  through the production-equivalent admin status workflow, not SQL edits.
- Verdict: **PASS WITH LIMITATIONS** — mechanism identified and proven safe
  on paper with exact routes/auth/scope; execution evidence intentionally
  deferred to M3-B per the audit-only default.

## 2. Current SQL-only state

- Seeded (M2 report): 3 tenants × (3 staff, 6 customers, 4 categories,
  20 products, 22 variants, 2 warehouses, 23 stock rows); all ownership
  checks green; owner login proven.
- Live evidence of invisibility: `GET /api/storefront/products` with
  `Host: loadtest-tenant-a.localhost:5174` returns **404
  `STOREFRONT_NOT_PUBLISHED`**, not an empty list — the publication guard
  fires before any projection is even consulted (§11 details). Product-list,
  detail, search, and discover paths all read Mongo/ES first
  (`ProductReadRepository` over `products`; ES candidates → Mongo hydration
  → SQL overlay), so no M2 row is reachable through them yet.

## 3. Mongo projection architecture

Collections (all in `ecommerce_read_db`): `products`, `categories`,
`shipping_methods`, `storefront_configs`, `storefront_navigation`,
`storefront_footer`, `storefront_pages`, `orders`,
`marketplace_homepage_read_models`, plus coupons/promotions/affiliate sets.

| Document | TenantId? | Writer (consumer) | Trigger events |
|---|---|---|---|
| `products` (`ProductDocument`) | Yes (`TenantId`, `ProductId`) + visibility (`IsActive/IsOnlineVisible/IsSearchable/IsFeatured`), stock (`TrackStock/AllowBackorder/StockQuantity`), `Variants[]`, freshness (`LastEventId/Version/Generation`) | `ProductCreatedEventConsumer`, `ProductUpdatedEventConsumer`, `ProductVariantProjectionConsumer`, `ProductStockChangedConsumer`, `ProductMediaChangedEventConsumer`, `ProductVisibilityLifecycleProjectionConsumer` — all via `IProductSqlAuthorityProjectionWriter` (SQL recompose → upsert **or tombstone**) | `ProductCreated/Updated/Deleted/Archived/PublishStatusChanged/PriceChanged/StockChanged/VariantCreated/Updated/Deleted/MediaChanged` (`Core/Events/ProductEvents.cs`, `ProductVariantEvents.cs`) |
| `categories` (`CategoryDocument`: `TenantId/CategoryId/ParentId/IsActive/IsOnlineVisible` + promotion banners) | Yes | **No `CategoryCreated/Updated` consumer exists**; promotion fields via `PromotionProjectionConsumer`; bulk via `AdminDataSyncController` (§6) | `Promotion*` only |
| `storefront_configs` | Yes | `StorefrontConfigProjectionConsumer` (`StoreConfigUpdated/Created/Published/RolledBack`), `SmartOfferProjectionConsumer`, `TenantCurrencyProjectionConsumer` | `Core/Events/StoreConfigEvents.cs` |
| `storefront_navigation` / `storefront_footer` / `storefront_pages` | Yes | `NavigationProjectionConsumer` (`NavigationPublishedEvent`), `StorefrontFooterProjectionConsumer`, `CmsPageProjectionConsumer` | `CmsPageEvents.cs`, `CacheInvalidationEvents.cs` |
| `orders` (unique `(TenantId,OrderId)`) | Yes | `OrderProjectionConsumer` (19 order/fulfillment/affiliate events) + 30 s `StorefrontOrderProjectionReconciliationService` sweep (SQL `Orders` → upsert, the **only** continuous SQL→Mongo repair loop, orders only) | `OrderCreated/StatusChanged/…` |
| `marketplace_homepage_read_models` | n/a (marketplace) | `HomepageProjectionConsumer` (`HomepageSectionsPublishedEvent`) | homepage publish |

Guards: every consumer acknowledges-and-returns (no retry storm) on missing
or empty `TenantId`/`ProductId`. Storefront reads always re-apply a SQL
authority overlay and suppress non-visible rows even on cache/projection hits.

## 4. Elasticsearch projection architecture

Indexes: `products`, `categories`, `cmspages` (concrete, tenant-filtered),
`public_affiliate_products`, `commercehub_products` (versioned alias
Active/Desired/WriteIndex + promotion), `marketplace_services` (global,
`TenantId`-filtered). Every product/category/page doc carries `TenantId`
`[Keyword]`; product DocId = `{TenantId}_{ProductId}`; **all** search/suggest/
discovery queries add `Term(TenantId) + Term(IsActive) +
Term(IsOnlineVisible)`.

Event path (never partial updates — full-doc reindex or versioned
tombstone/delete after SQL-overlay recompose; missing `TenantId` → ack):
`ProductSearchIndexConsumer` (`ProductCreatedEvent`),
`ProductUpdatedSearchIndexConsumer`, `ProductDeletedSearchIndexConsumer`
(deleted/archived/publish-status), `ProductStockPriceSearchIndexConsumer`,
`ProductPromotionSearchIndexConsumer`. Service façade:
`ProductSearchIndexService` (`Index/Delete/Tombstone/Search`, tenant-scoped
except global create-if-missing/ensure), `UnifiedSearchIndexService`
(+ categories/pages, tenant-scoped stale `DeleteByQuery`),
`CommerceHubSearchIndexService` (alias lifecycle; `PromoteIndexAsync` is a
platform-wide alias flip).

## 5. RabbitMQ/MassTransit projection path

Organic creation (`POST /api/products`, Owner/Admin/Manager +
`Idempotency-Key`) → `ProductAuthoringOrchestrator` →
**same-transaction outbox** (`MarketplaceEventOutboxMessages` +
MassTransit EF bus outbox, `UseBusOutbox`, 1 s delay, 5 min dedup window) →
`ProductCreatedEvent` (+ per-variant `ProductVariantCreatedEvent`) →
`ProductCreatedEventConsumer` (Mongo) + `ProductSearchIndexConsumer` (ES) +
affiliate/marketplace consumers; projection writer invalidates Redis after
every mutation. Stock-only replay exists (`sync-stock-to-mongo` publishes
`ProductStockChangedEvent{Reason=admin_sync_stock_to_mongo}`); payment-method
and shipping-method syncs republish their update events the same way.
Outbox drainers (`OutboxPublisherJob` 5 s/100-batch, `StockSyncOutboxWorker`,
`MarketplaceEventOutboxWorker`, …) are automatic-only, no HTTP. `DebugController`
outbox routes are accounting-only, `SuperAdmin` + Development-only (404
otherwise). **Direct SQL inserts touch none of this** (no triggers on any
product/stock table — verified in `DB/` snapshots and migrations).

## 6. Candidate population mechanisms

| # | Mechanism (exact route) | Auth | Scope | Effect |
|---|---|---|---|---|
| 1 | `POST api/admin/sync/product-variants` | Owner,Admin (tenant JWT) | Tenant (`RequiredTenantId`, `Where TenantId`) | Mongo `products` full-tenant rebuild via SQL-authority writer (upsert/tombstone) + Redis product cache clear. Idempotent. Docstring: “SAFE TO RUN”. |
| 2 | `POST api/admin/sync/product-variants/{productId}` | Owner,Admin | Tenant + single product | Same, one product + single-product cache clear. Ideal granularity. |
| 3 | `POST api/admin/sync/categories` | Owner,Admin | Tenant | Mongo `categories` bulk upsert from `CategoryLineageAuthority` + stale delete. The **only** category path (no category events exist). |
| 4 | `POST api/admin/sync/search/full` (or `/products`, `/categories`, `/pages`) | Owner,Admin | Tenant | ES reindex from SQL authority + tenant-scoped stale deletes. Non-destructive (no index `DELETE`). |
| 5 | `POST api/admin/search/reindex` | Owner,Admin | Tenant | ES product reindex from SQL overlay (+ tombstones). |
| 6 | `POST api/admin/commercehub/search/reindex` | Owner,Admin | Tenant | CommerceHub ES tenant reindex. Only if M3 covers CommerceHub reads. |
| 7 | `PUT {admin-store-config}/status` (`UpdateStorefrontStatus`, `AdminStoreConfigController:206`) | Owner/Admin-class (tenant JWT) + `RowVersion` concurrency + verified-email precondition | Tenant | Production publish workflow → `StorefrontStatus='live'` → publication guard passes + `StoreConfigPublishedEvent` → `storefront_configs` projection. Requires a `StoreConfigs` row to exist. |
| 8 | `POST api/products/bulk/{publish,unpublish}` | Owner,Admin,Manager | Tenant (per-item orchestrator) | Organic event path (`ProductUpdated` + `PublishStatusChanged` → Mongo + ES + invalidation). Guard skips already-published rows, so a publish-only pass over M2 rows is a **no-op**; useful only as unpublish→publish cycle or for future rows. |
| 9 | `POST api/products/sync-stock-to-mongo` | Owner,Admin + idempotency key | Tenant | Stock-only event replay. Partial; not a substitute for 1–4. |
| 10 | `POST api/admin/sync/shipping-methods`, `api/coupons|promotions/sync-to-mongo`, `api/admin/payment-methods/sync`, `api/ShippingMethods/sync`, `api/admin/af16e-reindex/trigger` | Owner,Admin(/Manager) | Tenant | Adjacent domains; invoke only if M3 scenarios need those read models. |

## 7. Production-equivalence classification

- **A — Production-equivalent**: #8 (bulk publish/unpublish runs the real
  authoring orchestrator and emits genuine domain events), #7 (the actual
  publish workflow operators use, with verification + concurrency guards).
  Prefer where the workflow fits the need.
- **B — Production-like, test-specific (accepted for M3-B)**: #1–#6, #9–#10.
  First-party admin convergence tools that recompose from the same SQL
  authority and flow through the same projection writers/consumers; they
  skip only the organic bus hop. This is the pragmatic backbone: #8 is a
  no-op over already-published M2 rows, and re-creating 60 products through
  `POST /api/products` would duplicate the universe.
- **C — Direct read-model manipulation (rejected this phase)**: raw Mongo
  inserts, raw ES `_doc` writes, hand-crafted RabbitMQ publishes. They would
  make reads pass while exercising none of the production architecture —
  precisely what M3 must not do.
- **D — Dangerous/destructive (rejected)**: `POST
  api/marketplace/admin/reindex/global` (deletes + recreates the index),
  global alias promotion/finalize, maintenance reset, debug outbox
  process/reset/requeue, dev seed/simulate/repair, bulk delete. (`DELETE
  api/admin/search/index` and `recreate-index` are additionally code-blocked
  with `403 GLOBAL_SEARCH_CONTROL_FORBIDDEN` — still never call them.)
- **E — Unknown**: none; every candidate above was traced to its handler.

Why A > B but B carries M3-B: the organic event path is the gold standard
for representativeness, but the seeded rows already carry their published
state, so the event path has nothing to say about them. B replays the same
SQL authority through the same writers/indexers minus the bus hop; combined
with #7/#8 on a subset (organic-path spot check), M3 reads exercise the
production serving architecture (Redis → Mongo/ES → SQL overlay).

## 8. Safety analysis

- All recommended calls (#1–#7) resolve scope from the caller’s
  tenant JWT (`RequiredTenantId` + `Where TenantId`), authenticated
  Owner/Admin; cross-tenant writes are structurally impossible through
  these handlers (no tenant parameter is accepted from the client).
- All are create-or-upsert (+ versioned tombstones for rows that are
  invisible in SQL); none deletes an index, drops a collection, or touches
  another tenant’s documents (stale-deletes are `TenantId`-filtered
  `DeleteByQuery`, affecting only the caller’s tenant).
- `sync-stock-to-mongo` and payment/shipping syncs publish real bus events —
  confined to the caller’s tenant and covered by the existing 1 s/5 s/15 s/
  30 s retry policy; still, M3-B needs them only if stock/price freshness is
  asserted beyond the full rebuilds.
- Prohibited in M3-B all the same: global reindexes, alias promotion,
  maintenance/debug/dev endpoints, webhooks, any provider invocation, any
  order/payment creation. Pre-flight (environment + `TEST_DB_PROOF` +
  tenant-JWT-from-M2-passwords, never TestAuth) before every call.

## 9. Tenant-isolation analysis

- Mongo: `TenantId` stored on every projection doc; `orders` enforces unique
  `(TenantId,OrderId)`; consumers reject tenantless events. Later asserts:
  tenant-A `products`/`categories` reads contain zero tenant-B IDs and zero
  foreign `TEST-PROBE-*` strings.
- ES: `TenantId` keyword + `{TenantId}_`-prefixed DocIds + mandatory term
  filters on every query path; tombstones are versioned per doc. Same
  assert shape as Mongo.
- `marketplace_services` is intentionally global (cross-tenant discovery)
  but carries `TenantId` and tenant reindex filters by it — M3 marketplace
  reads (if any) must assert vendor scoping, not absence.
- No projection derives ownership without storing it (all checked writers
  set `TenantId` from the event/SQL authority and reject when absent),
  except affiliate/marketing derivatives which are out of M3 read scope.
- Residual risk: the tenant-scoped stale `DeleteByQuery` paths delete only
  within the caller’s tenant — safe, but M3-B must run them per-tenant with
  per-tenant JWTs (never a super-admin token) so a future regression in
  scoping cannot become a cross-tenant delete.

## 10. Cache implications

- Product detail (10 m), product list (5 m), homepage rails (30/15/15 m),
  slug resolution (60 m) are Redis-first with a shared
  `cache-generation:storefront-products` and ≤45 s `MaxProjectionLagSeconds`;
  **store-config, search, and discover have no Redis layer** (SQL / ES
  direct). Reads re-apply the SQL authority overlay, so stale cache cannot
  resurrect invisible rows.
- Population side needs **no manual Redis action**: the SQL-authority
  projection writer invalidates (`product:id/slug`, generation advance,
  `product*` + `home*` pattern clear) after every upsert/tombstone, and
  `POST product-variants` additionally clears the tenant’s product cache.
  ES sync endpoints emit no invalidation and need none. No admin route
  clears product Redis (`clear-cache` is a no-op stub) — by design, events
  own invalidation.
- Consequence for M3-B: run Mongo syncs before first reads (cold slug cache
  recomputes on miss); allow ~60 s after ES sync before asserting search
  visibility (projection lag + consumer retries).

## 11. Recommended mechanism

Per tenant, with that tenant’s M2 Owner JWT, in order:
1. Ensure a `StoreConfigs` row exists (registration-equivalent; confirm the
   admin create/upsert path in M3-B — do **not** hand-edit status in SQL),
   then `PUT {admin-store-config}/status {StorefrontStatus:'live'}` → guard
   opens + `storefront_configs` projection via the real publish event.
2. `POST api/admin/sync/categories` → Mongo `categories` (12 docs).
3. `POST api/admin/sync/product-variants` → Mongo `products` (60 docs +
   variants/stock) + Redis clear.
4. `POST api/admin/sync/search/full` → ES `products`/`categories`/`pages`.
5. Organic-path spot check on 2–3 products: `bulk/unpublish` →
   `bulk/publish` (emits genuine `ProductUpdated`/`PublishStatusChanged`
   through the orchestrator; verifies the A-path end to end).
6. Verify (§13), then freeze the universe for k6.

## 12. Exact execution plan (M3-B, NOT executed in M3-A)

Prerequisites: API on `localhost:5010`, infra green, `TEST_ENVIRONMENT=local`,
`TEST_DB_PROOF` matching host/db, M2 `latest.json`, seed password in env.
Per tenant `k ∈ {a,b,c}`: login `loadtest-owner-{k}` → `access_token` cookie;
set `Host: loadtest-tenant-{k}.localhost:5174`, `X-Tenant-Id`,
`X-Request-Id`/`X-Correlation-Id` per call; call §11 steps 1–5 sequentially
per tenant (tenants may run in sequence, never parallelize the first pass);
record status/body/latency per call. Expected records: Mongo `products` 20,
`categories` 4 per tenant `TenantId`; ES `products` 20 (+tombstones: 0),
`categories` 4; Redis tenant product keys cleared then lazily rebuilt;
publication guard open (anonymous `GET products` 200, not 404).
Rollback/cleanup: none required — all calls are idempotent upserts; on
failure, fix cause and re-run the same call; never fall back to global
reindex. Safety gates: pre-flight identical to M2 (env + proof + prod-marker
refusal); per-tenant JWT only; denylist (global reindex, alias promote,
maintenance/debug/dev, webhooks, providers, orders/payments) enforced by the
runner before every call.

## 13. Verification plan (read-only)

- Mongo (per tenant): `products` count 20 with `TenantId` match; sentinel
  doc present with SKU/slug/price; `categories` count 4; zero docs with
  foreign `TenantId`; read-only `GET api/admin/sync/categories/debug` and
  `.../products/debug` as API-side cross-checks.
- ES (per tenant): `products/_count` with `Term(TenantId)` = 20;
  `categories/_count` = 4; sentinel searchable by SKU/slug; tenant-B probe
  strings return zero hits under tenant-A.
- Serving: anonymous `GET products`, `products/by-id/{probe}`,
  `search?q=`, `search/suggest`, `categories`, `resolve-slug/{probe-slug}`
  → 200 with own-tenant data; cross-tenant probe assertions (I-1/I-2) green.
- Budgets unchanged: variant-stock quantities equal manifest (syncs don’t
  mutate stock). Any mismatch → BLOCKED, re-run the tenant’s sync, never
  escalate scope.

## 14. Risks/blockers

1. `StoreConfigs` row creation path unconfirmed (service expects an existing
   row; registration is the organic creator) — M3-B must use the admin
   create/upsert or a registration-equivalent call, then publish via #7.
   Until the guard opens, **all** anonymous storefront reads stay 404.
2. No per-ID ES reindex — full-tenant sync is the smallest ES unit (fine at
   20 products; noted for future scale).
3. `MaxProjectionLagSeconds` (45 s) + consumer retries mean immediate
   post-sync reads can look stale — bake the wait into M3-B, don’t
   misread lag as leakage.
4. `marketplace_services`/CommerceHub projections untouched by this plan —
   CommerceHub reads need their own tenant reindex if M3 covers them.
5. OTel/exporters still absent locally — M3-B verification is API/Mongo/ES
   assertions, not capacity numbers.

## 15. Recommendation for M3-B

Execute §12 verbatim (per-tenant JWTs, denylist enforced, evidence per call),
resolve §14.1’s StoreConfig creation path first, then run §13 and freeze the
manifest. After green verification, M3 read scenarios (still separate work)
can assert isolation against live projections. Do not proceed to any VU load
until §13 is fully green for all three tenants.

**Verdict: PASS WITH LIMITATIONS** — a safe, tenant-scoped,
non-destructive, production-representative mechanism is identified (§11) and
executable in the approved local environment with per-call verification
(§12–§13); execution evidence and the StoreConfig creation-path confirmation
(§14.1) are the explicit missing pieces reserved for M3-B.
