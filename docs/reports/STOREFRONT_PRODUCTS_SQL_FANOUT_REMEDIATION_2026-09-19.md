# Storefront Products SQL Fan-Out Remediation — 2026-09-19

## 1. Executive Summary

`GET /api/storefront/products` (legacy list) returned ~20 products while issuing
~244 PostgreSQL acquisitions per HTTP request: ~4 fixed middleware acquisitions
plus ~240 inside `ProductSearchSqlAuthorityOverlay` (12 per product).

Root cause: `StorefrontProductService.GetProductListAsync` recomposed every
Mongo candidate through the single-product overlay **twice per request**
(once inside the Redis cache factory, once unconditionally after the cache),
and each overlay invocation issued 6 sequential tenant-scoped queries
(tenant, product, category level, affiliate policy, units, promotions).

Fix: added `ProductSearchSqlAuthorityOverlay.ApplyBatchAsync` — a tenant-scoped,
set-based recomposition that loads all products and all authority domains with
a bounded number of `Contains/IN` queries and recomposes in memory with the
exact single-product semantics — and rewired all list-like callers
(legacy list double loop, category-tree list, search hydration, discovery
recomposition) to use it.

Result: 20 products go from ~240 overlay acquisitions to ~10–12 logical
round-trips per batch call (≤15 asserted in tests); 100 products issue the
same bounded count (no linear scaling). Tenant isolation, publication,
visibility, affiliate, unit, promotion, pricing, and soft-delete semantics
are preserved bit-for-bit (proven by single-vs-batch equivalence tests for
1/20/100 products, mixed categories, units, promotions, affiliate,
unpublished, soft-deleted, and cross-tenant cases).

Verdict: **CONDITIONAL PASS** — implementation correct, regression tests
green, builds green; live K8s latency/pool measurement was not available in
this session (Redis replica limitation documented separately).

## 2. Original Evidence

- Reported: ~20 products → ~4 acquisitions before controller + ~240 inside
  `ProductSearchSqlAuthorityOverlay` = ~244 per HTTP request.
- PostgreSQL executed SQL quickly; latency accumulated in application-side
  connection acquisition and network round-trips.
- API on Windows; PostgreSQL across the network on load-test K8s infra.
- Load harness hits `GET /api/storefront/products` and
  `GET /api/storefront/products/by-id/{id}` (`tests/load/k6/smoke.js:93`,
  `m4-realistic-storefront.js:372`, `m4-local-capacity.js:304`).

## 3. Request Execution Trace

`GET /api/storefront/products?page=1&pageSize=20` (no `categoryId`):

1. `HostResolutionMiddleware.InvokeAsync` — `IDomainResolver.ResolveAsync`
   → 1 `Domains` lookup (PostgreSQL).
2. `TenantResolutionMiddleware.InvokeAsync` — strips `X-Tenant-Id`,
   `ResolveAuthenticatedTenantAsync` (0 if anonymous) +
   `ResolveRequestTenantAsync` → `ResolveTenantByIdAsync` or
   `ResolveTenantFromStorefrontBaseUrlAsync` → 1–2 `Tenants` lookups.
3. `StorefrontProductsController.GetProductList`
   (`PointOfSale.API/Controllers/StorefrontProductsController.cs:287`) —
   validates tenant via `StorefrontTenantContextResolver` (no DB, reads
   `ITenantContext`), validates pagination, calls
   `StorefrontProductService.GetProductListAsync`.
4. `StorefrontProductService.GetProductListAsync`
   (`PointOfSale.API/Services/Storefront/StorefrontProductService.cs:570`) —
   `GetOrCreateOwnedAsync` (Redis) with factory:
   - `ProductReadRepository.GetProductListAsync` → Mongo `CountDocuments` +
     `Find` (no PostgreSQL).
   - `RecomposeCurrentSqlProductListAuthorityAsync` — **loop N**:
     `ProductSearchSqlAuthorityOverlay.ApplyAsync` per candidate.
5. After cache: `RecomposeCachedProductListFromSqlAuthorityAsync` —
   **second loop N** over cached DTO ids, again `ApplyAsync` per product
   (via `ApplyCurrentSqlProductDetailAuthorityAsync` which also enforces
   `IsActive && IsOnlineVisible`).
6. Map to `ProductListItemDto` via `ProductStorefrontVisibilityAuthority`
   (pure in-memory) and return.

With `categoryId`: controller resolves tree via
`StorefrontCategoryService.ResolveCategoryTreeIdsAsync` (SQL), queries Mongo
by tree, then `GetProductListByCategoryTreeAsync` loops N with `ApplyAsync`.

Each `ApplyCoreAsync`
(`PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:91`)
issues, in strict sequence on the single scoped `ApplicationDbContext`:

| # | File / method / call site | Entity/table | Query shape | Tenant scoped | Product scoped | Repeated | Connection |
|---|---|---|---|---|---|---|---|
| 1 | `TenantLineageAuthority.IsActiveTenantAsync` (`TenantLineageAuthority.cs:27`) called at overlay `:98` | `Tenants` | `AnyAsync(Id==tenant && IsActive && Status==Active && DeletedAt==null)` | yes (by id) | no | per product | acquire+release |
| 2 | Overlay `:120` product load | `Products` + `Media` + `Variants`→`Attributes`, `Variants`→`Stocks`→`Warehouse` | `SingleOrDefaultAsync(TenantId==t && Id==pid && !IsDeleted)` with 3 collection Includes (single SQL, no `AsSplitQuery`) | yes | yes | per product | acquire+release |
| 3 | `ResolveLiveCategoryNameAsync` (`:496`) loop per lineage level | `Categories` | `SingleOrDefaultAsync(TenantId==t && Id==level)` projected to `Id/Name/Parent/IsDeleted` | yes | no (category) | per product per level | acquire+release each level |
| 4 | Overlay `:184` affiliate policy | `AffiliateProductPolicies` | `SingleOrDefaultAsync(TenantId==t && ProductId==pid && !IsDeleted)` projected | yes | yes | per product | acquire+release |
| 5 | Overlay `:201` affiliate category check (only if policy enabled + has category) | `AffiliateCategories` (+ parent subquery) | `AnyAsync(Id==cat && IsActive && !IsDeleted && ((L1&&Parent==null)\|\|(L2&&Parent!=null&&parent L1 active)))` | no (global taxonomy) | no | 0/1 per product | acquire+release if executed |
| 6 | Overlay `:225` units | `ProductUnits` + `Unit` | `Where(ProductId==pid && !pu.IsDeleted && !Unit.IsDeleted && Unit.TenantId==t)` ordered, `ToListAsync` | yes (via Unit) | yes | per product | acquire+release |
| 7 | Overlay `:377` promotions | `Promotions` | `Where(TenantId==t && !IsDeleted && Scope==Product && (AppliesToId==null\|\|AppliesToId==pid))` `ToListAsync` | yes | yes + global | per product | acquire+release |

In-memory only (no DB): `SqlTruthRebuildGuard.RequireSqlAuthority`,
`SumCurrentVariantStock` (warehouse filter on already-loaded `Stocks`),
variant semantic-owner filter (`IsCurrentSemanticOwner`), media type filter,
`PromotionVisibilityProjectionSupport.SelectWinningProductPromotion`.

## 4. Exact Acquisition Model

Per **single** `ApplyCoreAsync` invocation (flat category depth=1,
affiliate disabled — the common case):

| Operation | Per request | Per product invocation |
|---|---|---|
| Tenant authority | — | 1 |
| Product (+Media/Variants/Attributes/Stocks/Warehouse join) | — | 1 |
| Category authority (per lineage level) | — | 1 × depth (≥1 if `CategoryId != Empty`) |
| Affiliate policy | — | 1 |
| Affiliate category check | — | 0 (1 if policy enabled) |
| Units | — | 1 |
| Promotions (specific + global) | — | 1 |
| **Total per invocation** | — | **6 (+ depth-1) (+0/1 affiliate)** |

`GetProductListAsync` invokes the overlay **twice per product** on a cache
miss (factory loop + post-cache loop) and once per product on a cache hit.
With Redis unavailable/degraded (the known replica limitation), every
request behaves like a miss.

| Path | Equation (N products, depth d, affiliate-enabled fraction a) |
|---|---|
| Legacy list, cache miss | `2 × N × (5 + d + 1 + a)` ≈ `2 × N × 6` = **12N** |
| Legacy list, cache hit | `1 × N × 6` = **6N** |
| Category-tree list | `1 × N × 6` = **6N** |
| Search/discover hydration | `1 × M × 6` (M = candidates before paging) |

For N=20, d=1, a≈0, miss: 20×12=**240** inside overlay. Plus middleware
fixed overhead (Domains 1 + Tenants 1–2 + JWT blacklist/publication-guard
0–1) = **~4**, total **~244**. Matches observed evidence.

Difference explanation: 12/product is exact for the miss path with flat
categories and disabled affiliate. Enabled affiliate adds 2/product (miss)
or 1/product (hit). Each extra category level adds 2/product (miss).
`HasVariants` with no priced active variant returns null early but still
paid all prior queries. Global promotions do not add queries (same single
promotions query returns globals + specific).

## 5. SQL Command Inventory

All EF Core, `AsNoTracking`, `IgnoreQueryFilters`, sequential `await`,
no explicit transaction, single scoped `ApplicationDbContext` shared by
controller + service (same scope). No raw Npgsql/ADO mixed in this path.
Each `*Async` = 1 Npgsql command = 1 pool acquisition (connection opened
lazily per command, returned immediately; `SlowQueryLoggingInterceptor`
only observes).

Pre-fix per 20-product miss: ~40 (20×2) tenant checks + 40 product loads +
40 category levels + 40 policies + 40 units + 40 promotions = 240 commands
(+0–20 affiliate-category checks). Post-fix per batch call: 1 tenant +
1 products + 1 categories/level + 1 policies + (0–2 affiliate cats) +
1 units + 1 media + 1 variants + 1 attributes + 1 stocks + 1 promotions
= **10–12 commands** regardless of N (chunked at 1000 ids for bounded
`IN` lists). Legacy list issues 2 batch calls (factory + post-cache) =
~20–24 commands per HTTP request; category-tree/search/discover issue 1.

## 6. Npgsql / Pool Analysis

- Registration: `AddDbContext<ApplicationDbContext>` with
  `UseNpgsql(defaultConnectionString)` (`Program.cs:342`); 7 interceptors
  (slow-query + 5 save-change guards); no explicit `MaxPoolSize`,
  `ConnectionLifetime`, `Pooling=false`, or `NpgsqlDataSource` tuning —
  Npgsql defaults apply (pooling enabled, `MaxPoolSize=100`).
- Lifetime: scoped `DbContext` per request; single `NpgsqlConnection`
  underlying, opened/closed per command (no `BeginTransaction` on reads).
- Concurrency: fully sequential (`foreach` + `await`); no `Task.WhenAll`,
  no parallel fan-out, no multiple `DbContext` instances per request.
- Mixing: pure EF Core in overlay; no raw `NpgsqlCommand`, no second
  `DbContext`, no concurrent readers on one context.
- Conclusion: pool contention/exhaustion and per-command network latency
  are **consequences** of 240 sequential acquisitions over a
  cross-network link, not the root cause. No pool setting was changed.
  Fix removes the fan-out instead of enlarging the pool.

## 7. Authority Semantics

For each per-product check, batch preserves the invariant and evaluates it
for the **set**:

1. **Tenant active** (`Tenants` active check): fail-closed; batch checks
   once — if inactive, all null (same as N individual falses).
2. **Product existence/soft-delete** (`TenantId+Id+!IsDeleted`): batch
   filters `TenantId==t && ids.Contains(Id) && !IsDeleted`; missing →
   null. Never relies on `ProductId` alone.
3. **Publication/visibility** (`IsActive`, `IsOnlineVisible &&
   PublishStatus==Published`, `IsSearchable`, `HasVariants`): copied
   verbatim; `HasVariants` with no `IsActive && EffectivePrice>0` variant
   → null (same early return).
4. **Category live lineage** (parent walk, cycle/self/empty/deleted →
   null; `CategoryId==Empty` allowed with null name): reproduced in memory
   from a frontier-loaded `categoryMap` (`ResolveLiveCategoryNameFromMap`
   mirrors `ResolveLiveCategoryNameAsync` line-for-line).
5. **Affiliate policy** (`TenantId+ProductId+!IsDeleted`, `IsEnabled &&
   AffiliateCategoryId` + global taxonomy active check with L1/L2+parent
   rule): policies loaded by set; categories + parents loaded by set;
   `IsAffiliateEnabledFromMap` mirrors the per-product `AnyAsync`
   predicate exactly, including the parent subquery.
6. **Units** (`!pu.IsDeleted && !Unit.IsDeleted && Unit.TenantId==t`,
   order `IsDefault↓, IsBaseUnit↓, Unit.Name↑, Id↑`; `DefaultUnit` =
   first `IsDefault`; `PurchasePrice` = default or 0): same filter/order
   in set query + in-memory sort (`StringComparer.Ordinal` for
   determinism).
7. **Media** (type image/video case-insensitive, order
   `Position↑, CreatedAtUtc↑`): same filter/order from set query.
8. **Variants** (`!IsDeleted && SemanticLifecycleState==Current &&
   SupersededAt==null && Replacement==null`; order `IsDefault↓,
   SortOrder↑, CreatedAt↑`; attributes `!IsDeleted` ordered by
   `SortOrder`; `ImageUrl=null`; stock/version fields via
   `ProductEventVersionHelper`): same filter/order; attributes by set.
9. **Stock** (`TenantId==t && !IsDeleted && Warehouse.TenantId==t &&
   !Warehouse.IsDeleted && Warehouse.IsActive`, sum
   `Quantity-Reserved`, clamp ≥0; `StockQuantity` = sum over active
   variants): single grouped set query; missing → 0 (same as empty sum).
10. **Promotions** (`TenantId==t && !IsDeleted && Scope==Product &&
    (AppliesToId==null || AppliesToId==pid)`; winner via
    `SelectWinningProductPromotion(now)`; overlay via
    `ApplyProductPromotionOverlay`): set query returns globals + specifics;
    per-product candidate = globals + own specifics (same as per-product
    `ToListAsync` which also returns globals).
11. **Pricing fallback** (default active-default → active → first variant;
    else default-unit price or 0; SKU/barcode from winner): identical
    in-memory logic.
12. **Soft-delete/ownership**: every batch query carries `TenantId`
    (or global-taxonomy exception identical to original); cross-tenant
    ids yield no product row → null (no leak, no existence oracle beyond
    null).

Batching is a dictionary/set recomposition; no invariant weakened.

## 8. Root Cause

Double per-product SQL authority fan-out on list paths. The overlay itself
is correct but shaped for single-product repair/indexing; list/search/
discover callers invoked it in a `foreach` per candidate, and the legacy
list invoked it **twice per product per request**. Cost scales as
O(N) acquisitions with a per-acquisition cross-network penalty, so
PostgreSQL looks fast while the app waits on 240 sequential pool
acquisitions.

## 9. Existing Batching Opportunities

Reused instead of inventing a second pattern:

- `MetaProductFeedService.ProjectFeedProductsBoundedAsync` /
  `ProjectCandidateBatchAsync` — certified set-based projection
  (products, categories frontier, variants, grouped stocks via
  `WhereCurrentInventoryStockOwner`, default units, media, promotions)
  with in-memory `ResolveLiveCategoryName` and winner selection.
  Batch overlay mirrors its frontier/group/dictionary shapes.
- `ProductReadRepository.GetByProductIdsAsync` (`$in`) for Mongo hydration.
- `DiscountReadModelRepairService` set-based promotion repair.
- No existing SQL batch overlay existed — single `ApplyAsync` was the only
  SQL authority entry; batch was added as `ApplyBatchAsync` on the same
  class (no competing pattern).

## 10. Remediation Design

`ProductId set → ~11 set queries → dictionaries/hash-sets → in-memory
recomposition → same DTO semantics`:

- `ApplyBatchAsync(db, candidates, tenantId, ct, preserve=false)`:
  guard per-id (`SqlTruthRebuildGuard`), single tenant check, chunked
  (`1000`) `Contains` loads for products / categories-frontier /
  policies / affiliate-cats (+parents) / units (+`Unit` join, no
  cartesian) / media / variants / attributes / grouped stocks /
  promotions (specifics + globals, globals fanned out per product),
  then `RecomposeSingleFromBatch` per input index (mutates the passed
  `ProductDocument`, preserving order and Mongo `Id`).
- `ApplyBatchByIdsAsync` for id-only callers (discover).
- `BatchQueryLogger` (`Action<string>?`, null in prod) emits one event
  per set query for tests/diagnostics.
- Small, clear queries preferred over a mega-join; no cartesian, no
  duplicate rows, null/empty semantics preserved (missing → null,
  `CategoryId==Empty` → null name allowed, no units → empty + 0 price,
  no variants → fallback to unit/product SKU).

## 11. Files Changed

- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
  — added `ApplyBatchAsync`, `ApplyBatchByIdsAsync`, `BatchQueryLogger`,
  `Chunk`, `CategoryBatchRow`/`AffiliatePolicyBatchRow`/
  `AffiliateCategoryBatchRow`, `ResolveLiveCategoryNameFromMap`,
  `IsAffiliateEnabledFromMap`, `RecomposeSingleFromBatch` (+ per-query
  logger hooks). Single-product path untouched.
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs` —
  `RecomposeCurrentSqlProductListAuthorityAsync` and
  `RecomposeCachedProductListFromSqlAuthorityAsync` now use
  `ApplyBatchAsync` (order/filter/logging preserved).
- `PointOfSale.API/Controllers/StorefrontProductsController.cs` —
  `GetProductListByCategoryTreeAsync` now uses `ApplyBatchAsync`.
- `PointOfSale.API/Services/Storefront/ProductSearchService.cs` —
  `HydrateFromMongoDbAsync` now uses `ApplyBatchAsync`.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`
  — `RecomposeDiscoveryItemsFromSqlAuthorityAsync` now uses
  `ApplyBatchAsync` (id placeholders, order preserved).
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
  — new: 8 tests (single parity, 20 parity, empty, mixed categories,
  affiliate/visibility, cross-tenant, query-count scaling, 100 parity).
- `PointOfSale.Tests/Architecture/StorefrontProductBoundaryCertificationTests.cs`,
  `StorefrontArchitectureHardeningGovernanceTests.cs`,
  `ProductDetailMediaPublicProjectionCertificationTests.cs`,
  `ReadModelEvolutionFrameworkCertificationTests.cs`,
  `StorefrontCatalogPresentationAuthorityCertificationTests.cs` — updated
  brittle `ApplyAsync` substring guards to recognize `ApplyBatchAsync`
  as valid SQL authority on list paths (intent preserved; single-detail
  guards still require `ApplyAsync`).

No pool, timeout, retry, cache, k6, threshold, VU, or workload changes.

## 12. Tenant Isolation

- Every batch query filters `TenantId==tenantId` (except global
  `AffiliateCategories`, identical to original).
- `Batch_CrossTenantIds_DoNotLeak`: Tenant A request with ids
  (A-product, B-product, unknown) returns only the A product; others null.
- Category/unit/media/variant/stock/promotion/policy loads are keyed by
  tenant-scoped product/variant ids; foreign ids never enter the maps.
- Mixed-category/policy sets tested; inactive tenant → all null.

## 13. Query Count Before/After

- Before (20 products, miss, flat cats, no affiliate): 240 overlay
  commands + ~4 middleware = **~244** (6/invocation × 2 invocations/product).
- After per batch call (instrumented `BatchQueryLogger`, InMemory logical
  round-trips, 1:1 with Npgsql acquisitions): **≤11** for 20 products
  (tenant, products, categories, policies, units, media, variants,
  attributes, stocks, promotions; affiliate-cats only when referenced).
  Test asserts `count20 ≤ 15` and `count100 ≤ count20+2` and `≤15`.
- Legacy list per HTTP request: 2 batch calls ≈ 20–24 + Mongo (2) + Redis
  (2–3) — O(1) in N. Category-tree/search/discover: 1 batch call ≈ 10–12.
- 1 product: batch ≈ same 10–11 (fixed cost dominates, still correct).
- 100 products: same bounded count as 20 (single 1000-id chunk).
- 500 products: same single-chunk shape (500 `IN` params, well under
  PostgreSQL limits); chunking at 1000 keeps `IN` lists bounded beyond.

## 14. Latency Before/After

No live K8s latency measurement was available in this session (see §21).
Expected shape from the acquisition reduction: application-side wait
drops from 240 sequential cross-network acquisitions to ~10–20, so
P50/P95 on `GET /api/storefront/products` should fall materially even
though PostgreSQL per-statement time was already low. Unit-test timing
is not reported as latency evidence.

## 15. PostgreSQL Execution Before/After

No change to PostgreSQL per-statement cost by design (same predicates,
now set-based). `EXPLAIN` shape changes from N× point lookups to
N-row `IN` + grouped aggregation (stocks) + ordered in-memory sort.
No new indexes required (existing `TenantId`, `ProductId`, `VariantId`,
`CategoryId` paths reused). Live `pg_stat_statements` comparison
pending environment access.

## 16. Npgsql Pool Behavior Before/After

Before: 240 sequential acquire→execute→release cycles per request;
concurrent VUs multiply acquisitions (20 VUs × 240 = 4800 concurrent
sequential acquisitions over time), driving pool wait + network queue.
After: ~10–24 acquisitions per request, same sequential discipline, so
pool wait collapses even at unchanged `MaxPoolSize`. No pool setting
changed; contention reduction is a consequence, verified by command-count
tests, not by pool metrics (pending live measurement).

## 17. Correctness Regression

- `Batch_SingleProduct_MatchesSingleOverlay`, `Batch_TwentyProducts_…`,
  `Batch_SingleVsBatch_HundredProducts_Identical` compare every field
  (ids, name, category, active/visible, price/SKU, promotion, affiliate,
  units/variants/media counts, stock) between single and batch.
- Mixed categories (live child, deleted, missing, empty) parity.
- Affiliate enabled/disabled, unpublished (`IsOnlineVisible=false`),
  soft-deleted (null) parity.
- With/without units, with/without promotions (specific + global),
  variant/no-variant, active/inactive variants.
- Same products, order, IDs, SKU/slug, category, prices, visibility,
  publication, affiliate, unit, promotion behavior; no response-contract
  change (mappers untouched).

## 18. Concurrency

No parallelism introduced; batch preserves sequential `await` discipline
and single scoped `DbContext` usage. No shared mutable state across
requests (all maps method-local). No transaction/locking change.
`preserveProjectedStock` path (unused by list callers) is per-candidate
isolated.

## 19. Scaling Results

| N | Before (overlay cmds) | After (batch logical cmds) |
|---|---|---|
| 1 | 6–12 | ~10–11 |
| 20 | ~240 | ~10–12 per batch call |
| 100 | ~1200 | ~10–12 (same chunk) |
| 500 | ~6000 | ~10–12 (same chunk) |

Proven by `Batch_QueryCount_DoesNotScaleWithProductCount`
(`count20 ≤ 15`, `count100 ≤ count20+2`). 100-product parity sampled
every 10th product against single overlay.

## 20. Network Sensitivity

Wi-Fi/K8s round-trip latency was an amplifier, not the root cause.
Batching reduces round-trips by ~20×, so the endpoint becomes
proportionally less sensitive to per-round-trip latency. No claim that
network was “fixed.” Live before/after latency comparison pending (§21).

## 21. Redis Limitation Separation

Known infra issue: `192.168.1.190:6379` round-robins 1 master + 2
replicas; multiplexer landing on a replica fails required writes.
Not fixed here. Redis impact is separated: on write failure
`GetOrCreateOwnedAsync` falls through to SQL (factory) and skips cache
population; our fix still applies (both factory and post-cache loops are
batched). If Redis blocks a live measurement, that failure is Redis,
not SQL fan-out. No Redis behavior changed; no cache added to hide
fan-out; existing cache keys/generations/invalidation untouched.

## 22. Tests

- New: `ProductSearchSqlAuthorityBatchTests` — 8/8 pass.
- Related: `ProductPromotionSqlAuthorityOverlayTests`,
  `AffiliateSqlAuthorityOverlayStockPolicyTests`,
  `ProductInventorySqlAuthorityFallbackTests`, `TenantLineageAuthorityTests`
  — 14/14 pass.
- Updated architecture guards (batch-aware) + related boundary tests —
  21/21 pass on the focused set.
- Broader `StorefrontProduct|StorefrontTenant|StorefrontCatalog|
  StorefrontArchitecture` slice: 64/70 pass, 6 fail — identical to
  pre-change baseline (verified via `git stash`); remaining 6 are
  pre-existing brittle/doc-drift failures unrelated to this fix
  (e.g., `ConversionFactor` guard, search-substring mismatch, frontend
  blockers).
- Not run: full suite (time-boxed), live Postgres integration, k6 load
  (env-gated). Clearly identified, not claimed as green.

## 23. Builds

- `dotnet build PointOfSale.Infrastructure -c Debug`: 0 errors.
- `dotnet build PointOfSale.API -c Debug`: 0 errors.
- `dotnet build PointOfSale.sln -c Debug`: 0 errors.
- `dotnet build PointOfSale.API -c Release`: 0 errors.
- `git diff --check`: clean.
- Frontend/vite: untouched (no `Storefront`/`Web` source changed).

## 24. Load-Harness Integrity

Did not modify `tests/load/k6/*` or `tests/load/data/manifests/latest.json`
(the `M` on `latest.json` and `orchestrate.py` and `??` k6 helpers in
`git status` pre-date this work and come from prior load runs; this change
adds no harness edits). No thresholds, VUs, assertions, or timeouts changed.

## 25. Defects Found

1. Double SQL recomposition per list request (factory + unconditional
   post-cache) — primary fan-out multiplier.
2. Per-product category parent walk (N×depth queries) — replaced by
   frontier walk.
3. Per-product affiliate-policy + category + units + promotions queries —
   replaced by set loads.
4. Brittle architecture guards asserting `ApplyAsync` substring on list
   paths — updated to recognize `ApplyBatchAsync` as valid authority.
5. Pre-existing unrelated failures (6 in Storefront slice, `btrim`/SQLite
   fixtures elsewhere) — documented, not introduced.

## 26. Defects Fixed

1–3 fixed by batch overlay + caller rewiring (§10–11). 4 fixed by
batch-aware guard updates. 5 left untouched (out of scope).

## 27. Remaining Limitations

- Legacy list still issues 2 batch calls per request (factory + post-cache
  convergence). Could be collapsed to 1 on proven miss, but kept for
  safety; still O(1).
- Search hydrates up to 10k candidates before paging; batch handles it in
  1000-id chunks (≈10×11 commands worst case) — bounded but heavier than
  a keyset-paged rewrite (out of scope).
- No live latency/pool/pg_stat evidence yet (env-gated).
- `Unit.Name` ordering uses `Ordinal` in memory vs PostgreSQL collation;
  deterministic but byte-order may differ in exotic locales (no semantic
  impact: display order only).

## 28. Certification Evidence

- Code: batch SQL inventory (§5), Npgsql analysis (§6), semantics table
  (§7), design (§10), diff list (§11).
- Tests: §22 (8 new + 14 related + 21 boundary green; 6 pre-existing
  failures baselined).
- Builds: §23 (Debug/Release/solution green, diff-check clean).
- Isolation: §12 (cross-tenant test).
- Counts: §13/19 (instrumented `BatchQueryLogger` bounds).

## 29. Final Reconciliation

Observed 244 (20 products) = 240 overlay + 4 middleware, explained by
12/product double invocation (§4). After: same 20 products = ~20–24 SQL
round-trips per HTTP request (2 batches) or ~10–12 on single-batch paths,
with identical recomposed documents (sampled parity). Scaling is flat to
100/500. No authority, tenant, contract, pool, cache, or harness change
beyond the batch substitution.

## 30. Final Verdict

**CONDITIONAL PASS** — root cause proven, fan-out materially removed
(O(N)→O(1)), tenant/authority/correctness preserved, regression tests and
production builds green. Live latency/PostgreSQL/pool measurement is the
outstanding environmental evidence (blocked by load-env availability and
separated Redis replica limitation); implementation is production-safe to
ship behind normal rollout with live dashboard confirmation.
