# Storefront SQL Fan-Out Deep Review and Certification — 2026-09-20

## 1. Executive Summary

Independent deep review of all storefront list/search/discovery/home/affiliate SQL authority paths.

- Before: storefront list/search/discovery fanned out through single-product
  `ProductSearchSqlAuthorityOverlay.ApplyAsync` once per candidate, and the legacy
  product list did it twice per candidate on a cache miss. For N=20, flat categories,
  affiliate disabled, miss: ~240 overlay commands + ~4 middleware = ~244 acquisitions
  per HTTP request, all sequential on one scoped `DbContext`.
- After: every list-like caller recomposes through set-based
  `ApplyBatchAsync` / `ApplyBatchByIdsAsync` / `ApplyBatchByTenantAsync`.
  One batch call issues a chunk-bounded number of relational commands regardless of
  N within a 1,000-id chunk (8 commands for the simple-product SQLite fixture;
  14 for 2,000 ids across two chunks). Cross-tenant affiliate streams issue one
  set-based authority load for 1 tenant or 20 tenants (8 commands in both cases),
  not one load per tenant.
- Semantic/security parity verified by code comparison plus 10/10 relational
  batch tests including single-vs-batch equivalence (1/20/100), mixed categories,
  affiliate/visibility, cross-tenant isolation, duplicate ids, and exact
  `DbCommandInterceptor` counts for N=1,20,100,500,1000,1001,2000.
- Builds: `PointOfSale.Infrastructure` Debug 0 errors,
  `PointOfSale.API` Debug 0 errors, `PointOfSale.API` Release 0 errors.
- Focused architecture guards: 19/21 pass; 2 failures are pre-existing,
  unrelated to this work (missing `ApplyNoStoreHeaders` expectation on
  `StorefrontProductsController`, missing `Total = result.TotalCount` expectation
  on `CommerceHubQueryService`; neither file's relevant logic was changed by this
  remediation).
- Live HTTP/latency/PostgreSQL/pool measurement was not available in this session:
  no local API listener on 5000/5001, no local listeners for PostgreSQL/Redis/
  MongoDB/Elasticsearch; test secrets are intentionally empty. Implementation is
  therefore certified on code + relational command-count + regression evidence,
  pending live dashboard confirmation.

Verdict: **CONDITIONAL PASS**

## 2. Objective, Constraints, and Worktree Discipline

Objective: comprehensive independent review and remediation of storefront SQL
fan-out/N+1 patterns, including semantic/security parity, bounded query tests,
runtime evidence, and production certification.

Absolute constraints honored:

- No subagents; all investigation, fixes, and verification done directly.
- No changes to `tests/load/k6/*` workloads, thresholds, VUs, or assertions.
- No weakened authorization or tenant isolation; two isolation gaps found were
  tightened (`StorefrontSearchController` tenant `DeletedAtUtc`, batch affiliate
  policy keyed by `(TenantId, ProductId)`).
- No pool/timeout increases; no arbitrary parallelism. All batch paths remain
  sequential `await` on a single scoped `DbContext`.
- Heavily dirty worktree preserved. Unrelated user changes (user management,
  auth/session, frontend, load manifests, migrations) were not reverted. This
  report certifies only the storefront SQL authority surface listed in §11.

Prior baseline: `docs/reports/STOREFRONT_PRODUCTS_SQL_FANOUT_REMEDIATION_2026-09-19.md`
(CONDITIONAL PASS; InMemory logical counts; double recomposition still present on
miss; per-tenant grouping on affiliate paths; analogous loops still present on
home/search/SEO/affiliate surfaces).

## 3. Request Execution Traces

### 3.1 Legacy list `GET /api/storefront/products` (no categoryId)

1. `HostResolutionMiddleware` → 1 `Domains` lookup.
2. `TenantResolutionMiddleware` strips `X-Tenant-Id`, resolves trusted tenant →
   1–2 `Tenants` lookups.
3. `StorefrontProductsController.GetProductList` validates tenant/pagination, calls
   `StorefrontProductService.GetProductListAsync`
   (`PointOfSale.API/Services/Storefront/StorefrontProductService.cs:570`).
4. `GetOrCreateOwnedAsync` (Redis) with factory
   (`StorefrontProductService.cs:586-648`):
   - `_repository.GetProductListAsync` → Mongo `CountDocuments` + `Find`.
   - `RecomposeCurrentSqlProductListAuthorityAsync` (`:802-829`) → one
     `ApplyBatchAsync` (`:813`), then in-memory `IsActive && IsOnlineVisible`
     filter. `TotalCount` is the repository `totalCount` (`:631`), restored after
     review (see §25).
5. If `cacheFactoryExecuted` (`:586,594,652`), return immediately — the second
   full SQL recomposition on a miss is skipped.
6. Else (cache hit): `RecomposeCachedProductListFromSqlAuthorityAsync` (`:714-779`)
   builds placeholders from cached DTO ids and issues one `ApplyBatchAsync`
   (`:742`), then `IsPubliclyVisibleProduct` + affiliate-mode filter.

Miss = 1 batch call. Hit = 1 batch call. Before this work, miss = 2×N singles,
hit = N singles.

### 3.2 Category-tree list

`StorefrontProductsController.GetProductListByCategoryTreeAsync`
(`PointOfSale.API/Controllers/StorefrontProductsController.cs:411-455`): Mongo
`GetProductListByCategoryTreeAsync` (count + page), then one `ApplyBatchAsync`
(`:436`) if items exist, then `IsActive && IsOnlineVisible` filter. `TotalCount`
remains repository total (`:454`).

### 3.3 Search hydration

`ProductSearchService.HydrateFromMongoDbAsync`
(`PointOfSale.API/Services/Storefront/ProductSearchService.cs:176-225`):
`GetByProductIdsAsync` (`$in`, order-preserving), tenant-equality pre-filter,
then one `ApplyBatchAsync` (`:199`) when tenant documents exist, then
`IsActive && IsOnlineVisible && IsSearchable` filter.

### 3.4 Discovery

`ProductDiscoveryService.RecomposeDiscoveryItemsFromSqlAuthorityAsync`
(`PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs:547-625`):
early return on empty, placeholders from candidate ids, one `ApplyBatchAsync`
(`:570`), indexed loop preserving candidate order, then visibility +
`MatchesSqlAuthoritativeDiscoveryFilters`.

### 3.5 Homepage shelves

`StorefrontHomeService.RecomposeProductCardsFromSqlAuthorityAsync`
(`PointOfSale.API/Services/Storefront/StorefrontHomeService.cs:545-599`): filters
`Guid.Empty`, builds same-tenant placeholders, one `ApplyBatchAsync` (`:569`),
then `IsActive && IsOnlineVisible` (+ affiliate-mode) filter. Tenant-inactive now
yields all-null from the batch (same outcome as the removed early return, without
the extra pre-query; convergence warning preserved).

Homepage categories (`:601-670`) were already set-based: one tenant check, one
`LoadTenantSnapshotAsync`, one `Categories` id load, in-memory
`IsLiveInLineage`.

### 3.6 Unified/header search

`StorefrontSearchController.FilterSearchItemsBySqlAuthorityAsync` (`:404-534`):
one tenant-active check (now including `DeletedAtUtc == null`, see §26), distinct
product ids → one `ApplyBatchByIdsAsync` (`:432`), category ids → at most one
`LoadTenantSnapshotAsync`, CMS groups/slugs → one `CmsPages` query, then pure
in-memory fan-in. `RecomposeHeaderSuggestionsBySqlAuthorityAsync` (`:536-580`):
one `ApplyBatchByIdsAsync` (`:542`), in-memory shaping.

### 3.7 SEO sitemap

`SeoService` sitemap loop (`PointOfSale.API/Services/Storefront/SeoService.cs:627-673`):
per 100-product Mongo page, one `ApplyBatchAsync` (`:638`), then
`IsActive && IsOnlineVisible` + slug check. Single-slug detail
(`LoadCurrentVisibleProductBySlugAsync` → `ApplyCurrentSqlProductAuthorityAsync`
→ `ApplyAsync`, `:1117-1145`) intentionally remains per-item.

### 3.8 Affiliate discovery / public affiliate / affiliate portal

- `AffiliateProductDiscoveryService.RecomposeDiscoveryItemsFromSqlAuthorityAsync`
  (`:378-464`): valid-pair filter, one `ApplyBatchByTenantAsync` (`:394`), one
  `Tenants` dictionary load, indexed loop with visibility + `IsAffiliateEnabled`
  + category + pricing/stock + storefront-URL checks.
- `PublicAffiliateProductsController.RecomposePublicAffiliateProductsFromSqlAuthorityAsync`
  (`:341-464`): one affiliate policy/category set query (`:354-381`), one
  `ApplyBatchByTenantAsync` (`:393`), one `Tenants` dictionary load, indexed loop.
  Single-detail `RecomposePublicAffiliateProductFromSqlAuthorityAsync` (`:466-...`)
  retains `ApplyAsync` for the single-product route.
- `AffiliatePortalController.RecomposeAffiliateIntelligenceDocumentsBySqlAuthorityAsync`
  (`:1414-1564`): one policy/category set query, one level-1 parent dictionary load,
  one `Tenants` dictionary load, one `ApplyBatchByTenantAsync` (`:1494`), indexed
  loop with lineage + visibility + pricing checks. Single-detail
  `RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync` (`:1566-...`)
  retains `ApplyAsync`.

## 4. Exact Acquisition Model

Per-single `ApplyCoreAsync` (flat category, affiliate disabled): 6 commands
(tenant, product+includes, category level, policy, units, promotions).

| Path | Before | After |
|---|---|---|
| Legacy list, cache miss, N products | `2×N×6 = 12N` | 1 batch call (see batch equation) |
| Legacy list, cache hit | `N×6 = 6N` | 1 batch call |
| Category-tree / search / discovery / home / header / sitemap page | `M×6` (M=candidates) | 1 batch call per page/candidate set |
| Cross-tenant affiliate (T tenants, D candidates) | `D×(policy+tenant+overlay)` ≈ O(D) + per-tenant overlay | 1 cross-tenant batch + 1 tenants query + 1–2 policy/category queries |

Batch call equation (single tenant, D distinct product ids, V distinct variant ids,
C lineage levels touched, A affiliate-category ids, P missing parents):

```text
batch(D) = 1[tenant]
  + ceil(D/1000)[products]
  + C[categories frontier, 1 for flat]
  + ceil(D/1000)[policies]
  + (0 or ceil(A/1000)+ceil(P/1000))[affiliate cats]
  + ceil(D/1000)[units]
  + ceil(D/1000)[media]
  + ceil(D/1000)[variants]
  + (0 or ceil(V/1000))[attributes]
  + (0 or ceil(V/1000))[stocks]
  + ceil(D/1000)[promotions]
```

Measured SQLite fixture (simple products, one category, no units/media/variants/
policies/promotions rows beyond empty sets; EF still issues each query):

- D=1,20,100,500,1000 → 8 commands.
- D=1001,2000 → 14 commands.
- 1,000 duplicate ids of one product → 8 commands.
- Cross-tenant: 1 tenant × 1 product → 8; 20 tenants × 20 products → 8.

For N=20 miss, before ≈240 overlay commands; after = 8 overlay commands in the
single batch call (+ Mongo 2 + Redis 2–3). Category/search/discovery/home/header
paths are likewise O(1) per candidate set within a chunk, O(ceil(D/1000)) beyond.

## 5. SQL Command Inventory

All overlay batch queries are EF Core, `AsNoTracking`, `IgnoreQueryFilters`,
sequential `await`, no explicit transaction, single scoped `ApplicationDbContext`.
Each `*Async` is one relational command / one pool acquisition;
`SlowQueryLoggingInterceptor` only observes.

Batch loads (single-tenant form; cross-tenant form replaces `TenantId == t` with
`activeTenantIds.Contains(...)` plus `(TenantId, ProductId)` candidate gating):

- Tenants: `Where(requestedIds.Contains(Id) && IsActive && Status==Active &&
  DeletedAtUtc==null)` → ids only.
- Products: `Where(activeTenantIds.Contains(TenantId) && chunk.Contains(Id) &&
  !IsDeleted)`, gated by `candidateKeys` before entering `productsById`.
- Categories frontier: `Where(activeTenantIds.Contains(TenantId) &&
  ids.Contains(Id))` selecting `(Id, TenantId, Name, ParentCategoryId, IsDeleted)`,
  one query per lineage level.
- Policies: `Where(activeTenantIds.Contains(TenantId) &&
  chunk.Contains(ProductId) && !IsDeleted)` selecting
  `(ProductId, TenantId, IsEnabled, AffiliateCategoryId, CreatedAt)`, keyed by
  `(TenantId, ProductId)` after §26 fix.
- Affiliate categories (+parents): global taxonomy by id, as the single path.
- Units: `Include(Unit)`, `Where(chunk.Contains(ProductId) && !pu.IsDeleted &&
  !Unit.IsDeleted && activeTenantIds.Contains(Unit.TenantId))`, deterministic
  `OrderBy ProductId, IsDefault↓, IsBaseUnit↓, Unit.Name, Id`.
- Media: `Where(activeTenantIds.Contains(TenantId) && chunk.Contains(ProductId))`.
- Variants: current semantic owners only
  (`!IsDeleted && SemanticLifecycleState==Current && SupersededAt==null &&
  Replacement==null`), tenant-gated, in-memory order
  `IsDefault↓, SortOrder↑, CreatedAt↑`.
- Attributes: `Where(chunk.Contains(VariantId) && !IsDeleted)`, in-memory
  `SortOrder↑`.
- Stocks: grouped `Where(activeTenantIds.Contains(TenantId) &&
  chunk.Contains(VariantId) && !IsDeleted && Warehouse.TenantId==TenantId &&
  !Warehouse.IsDeleted && Warehouse.IsActive)` grouped by `(TenantId, VariantId)`,
  `Sum(Quantity-ReservedQuantity)`, clamp ≥0.
- Promotions: `Where(activeTenantIds.Contains(TenantId) && !IsDeleted &&
  Scope==Product && (AppliesToId==null || chunk.Contains(AppliesToId)))`;
  specifics gated by `product.TenantId == promo.TenantId`; globals fanned out per
  product with the same tenant-equality gate.

No mega-join, no cartesian, no `Task.WhenAll`, no second `DbContext`.

## 6. Npgsql / Pool Analysis

- Registration: `AddDbContext<ApplicationDbContext>` with
  `UseNpgsql(defaultConnectionString)` (`PointOfSale.API/Program.cs:342-353`);
  7 interceptors (slow-query + save-change guards). No explicit `MaxPoolSize`,
  `ConnectionLifetime`, `Pooling=false`, or `NpgsqlDataSource` tuning — Npgsql
  defaults apply (pooling enabled, `MaxPoolSize=100`).
- Transport defaults centralized in
  `PointOfSale.Infrastructure/Configuration/PostgresConnectionStringPolicy.cs`:
  15-second connection timeout, 30-second command timeout when not specified.
  Not changed.
- Scoped `DbContext` per request; single underlying `NpgsqlConnection`,
  opened/closed per command; no `BeginTransaction` on reads.
- Fully sequential; no concurrent readers on one context.
- Conclusion unchanged from prior report: pool contention was a consequence of
  ~240 sequential cross-network acquisitions, not the root cause. Fix removes the
  fan-out; no pool setting changed.

## 7. Authority Semantics

Batch replicates `ApplyCoreAsync` per candidate, in memory after set loads:

1. Tenant active (`IsActive && Status==Active && DeletedAtUtc==null`): fail-closed;
   inactive → all null. `StorefrontSearchController` inline check now matches
   `TenantLineageAuthority` exactly (see §26).
2. Product existence/soft-delete (`TenantId+Id+!IsDeleted`): missing or
   tenant-mismatched → null. Cross-tenant ids cannot hydrate another tenant's row
   (`productsById` gated by `(TenantId, ProductId)`; recomposition rechecks
   `sqlProduct.TenantId != tenantId → null`).
3. Publication/visibility (`IsActive`, `IsOnlineVisible && PublishStatus==Published`,
   `IsSearchable`, `HasVariants` with priced active variant): verbatim; callers
   re-apply the same public-surface predicates as before.
4. Category live lineage (cycle/self/empty/deleted → null; `Empty` allowed with null
   name): `ResolveLiveCategoryNameFromMap` mirrors `ResolveLiveCategoryNameAsync`
   line-for-line plus a `TenantId` equality check. Frontier walk replaces N×depth
   queries.
5. Affiliate policy + taxonomy (L1 with null parent; L2 with active L1 parent):
   `IsAffiliateEnabledFromMap` mirrors the per-product predicate; batch keys
   policies by `(TenantId, ProductId)` to prevent cross-tenant overwrite.
6. Units/media/variants/attributes/stock/promotions/pricing fallback: same filters,
   orders, and winner selection (`PromotionVisibilityProjectionSupport`), with
   tenant-gated in-memory narrowing (`Unit.TenantId`, `Media.TenantId`,
   `Variant.TenantId`, `(TenantId, VariantId)` stocks, per-product promotion tenant
   gate).
7. Order/duplicates: input order preserved; null at the same index for
   missing/invalid authority; duplicate ids share set loads but recompose per index.
8. `TotalCount` contracts untouched except where the batch substitution required
   none: legacy list and category-tree still return repository totals; cached-list
   hit still returns converged visible count (pre-existing behavior); affiliate and
   discovery totals remain post-authority `Count` before paging (pre-existing).

Known intentional strictness (documented, not a regression): batch filters
`Media.TenantId` and `Variant.TenantId`, while the old `Include`-based single path
could hydrate a media/variant row whose tenant disagreed with the product tenant.
Batch fails closed there, consistent with tenant isolation.

## 8. Root Cause

Double per-product SQL authority fan-out on list paths plus analogous per-candidate
loops on home/search/SEO/affiliate surfaces. The overlay itself was correct but
shaped for single-product repair/indexing; list callers invoked it per candidate,
and the legacy list invoked it twice per product on a miss. Cost scaled as O(N)
sequential acquisitions with per-acquisition cross-network penalty.

## 9. Existing Batching Opportunities

Reused, not reinvented:

- `MetaProductFeedService` certified set projection (frontier/groups/dictionaries).
- `ProductReadRepository.GetByProductIdsAsync` (`$in`, order-preserving) for Mongo
  hydration — already used by search hydration and retained.
- No prior SQL batch overlay existed; batch added on the same
  `ProductSearchSqlAuthorityOverlay` class. No competing pattern introduced.

## 10. Remediation Design

`Candidate set → chunked (1,000) set queries → dictionaries → in-memory
recomposition → same DTO predicates`:

- `ApplyBatchAsync(db, candidates, tenantId, ct, preserve=false)` → single-tenant
  `ApplyBatchCoreAsync` with `requiredTenantId`.
- `ApplyBatchByIdsAsync` for id-only callers.
- `ApplyBatchByTenantAsync` → `ApplyBatchCoreAsync` with `requiredTenantId=null`;
  one tenant-authority load for all requested tenants; all domains loaded with
  `activeTenantIds.Contains(...)` plus candidate-pair gating; per-candidate tenant
  used at recomposition; original order retained.
- `Chunk`, `CategoryBatchRow`/`AffiliatePolicyBatchRow`/
  `AffiliateCategoryBatchRow`, `ResolveLiveCategoryNameFromMap`,
  `IsAffiliateEnabledFromMap`, `RecomposeSingleFromBatch`.
- No `BatchQueryLogger` static mutable state; relational counts proven with a
  `DbCommandInterceptor` on SQLite (see §13, §17).

## 11. Files Changed (Fan-Out Surface Only)

- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
  — batch core, cross-tenant set path, tuple-keyed policies, tenant-gated domains.
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs` — batch in
  factory + cache-hit paths; `cacheFactoryExecuted` skips the duplicate miss pass;
  repository `totalCount` preserved.
- `PointOfSale.API/Controllers/StorefrontProductsController.cs` — category-tree
  batch.
- `PointOfSale.API/Services/Storefront/ProductSearchService.cs` — hydration batch.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`
  — discovery batch.
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs` — homepage cards
  batch (redundant affiliate pre-query removed; tenant outcome preserved via batch).
- `PointOfSale.API/Controllers/StorefrontSearchController.cs` — unified/header
  batches + category snapshot (tenant predicate corrected to include
  `DeletedAtUtc`).
- `PointOfSale.API/Services/Storefront/SeoService.cs` — sitemap page batch.
- `PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs`
  — cross-tenant batch + set tenant load.
- `PointOfSale.API/Controllers/PublicAffiliateProductsController.cs` — set policy
  query + cross-tenant batch + set tenant load.
- `PointOfSale.API/Controllers/AffiliatePortalController.cs` — set policy/parent/
  tenant loads + cross-tenant batch.
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs` — new
  relational SQLite coverage (10 tests).
- Architecture guard updates recognizing batch entry points on list paths.

All other modified/untracked files in the worktree (user management, session/auth,
frontend, load manifests, migrations) are out of scope, preserved, and not
certified here.

## 12. Tenant Isolation

- Every batch domain query carries an active-tenant gate; global
  `AffiliateCategories` exception matches the original.
- `Batch_CrossTenantIds_DoNotLeak` + `BatchByTenant_MixedTenantCandidates_...`
  prove ordering, null-for-mismatch, and no hydration across tenants.
- New cross-tenant command-count test proves 20 tenants cost the same 8 commands
  as 1 tenant on the simple fixture (no per-tenant loop).
- Policy overwrite across tenants eliminated by `(TenantId, ProductId)` key.
- No existence oracle beyond null; no tenant id trusted from client headers
  (`TenantResolutionMiddleware` strips `X-Tenant-Id` before resolution).

## 13. Query Count Before/After

Before (N=20, miss, flat, no affiliate): 240 overlay + ~4 middleware ≈244.
After (SQLite `DbCommandInterceptor`, simple fixture):

| N | Commands | Note |
|---|---|---|
| 1 | 8 | tenant+products+categories+policies+units+media+variants+promotions |
| 20 | 8 | same single chunk |
| 100 | 8 | same single chunk |
| 500 | 8 | same single chunk |
| 1000 | 8 | single 1,000-id chunk boundary |
| 1001 | 14 | two chunks |
| 2000 | 14 | two chunks |
| 1000 duplicates of 1 id | 8 | distinct gating |
| 20 tenants × 20 products (cross-tenant) | 8 | same as 1 tenant × 1 product |

Assertions: `counts[1]==counts[20]==counts[100]==counts[500]==counts[1000]`,
`counts[1001]==counts[2000]`, `counts[1001] <= 2*counts[1000]-1`,
`counts[1000]==8`, `counts[2000]==14`, `<15` / `<30` bounds, duplicates equal
single-id count, cross-tenant 1 vs 20 equal.

Full-domain fixtures (units/media/variants/attributes/stocks/promotions populated)
issue more than 8 per chunk but remain chunk-bounded; parity tests cover those
shapes via InMemory equivalence plus the relational bound above.

## 14. Latency Before/After

No live K8s latency measurement available in this session (see §21, Runtime
Annex). Expected shape from acquisition reduction: application wait drops from
~240 sequential cross-network acquisitions to ~8–14 per batch call, so P50/P95 on
`GET /api/storefront/products` should fall materially even though per-statement
PostgreSQL time was already low. Unit-test durations are not reported as latency
evidence.

## 15. PostgreSQL Execution Before/After

No per-statement cost change by design (same predicates, now set-based).
`EXPLAIN` shape changes from N× point lookups to N-row `IN` + grouped stock
aggregation + in-memory sorts. No new indexes required. Live `pg_stat_statements`
comparison pending environment access.

## 16. Npgsql Pool Behavior Before/After

Before: ~240 sequential acquire→execute→release cycles per 20-product miss;
concurrent VUs multiply acquisitions over time. After: ~8–14 acquisitions per
batch call with identical sequential discipline. No pool setting changed;
contention reduction is a consequence, verified by command counts, not by live
pool metrics.

## 17. Correctness Regression

`ProductSearchSqlAuthorityBatchTests` — 10/10 pass (this session, sequential):

- Single parity, 20 parity, empty, mixed categories, affiliate/visibility,
  cross-tenant no-leak, mixed-tenant order/isolation, cross-tenant command-count
  equality, relational chunk-bounded counts (1/20/100/500/1000/1001/2000 +
  duplicates), 100-product sampled single-vs-batch identity.

Focused architecture guards — 19/21 pass:

- Pass: homepage shelves, affiliate discovery reads, storefront unified/header
  search, affiliate portal discovery, SEO sitemap, product boundary (batch +
  cache-miss guard), catalog pricing/availability/media/search-discovery guards,
  and related stance checks.
- Fail (2, pre-existing, unrelated): `...ProductVariantResolution...` expects
  `ApplyNoStoreHeaders` in `StorefrontProductsController` (only present in
  `CommerceHubController`); `...CommerceHubSearchCounts...` expects
  `Total = result.TotalCount` in `CommerceHubQueryService` (file untouched by this
  work). Neither failure touches the SQL fan-out surface and neither was
  introduced here.

## 18. Concurrency

No parallelism introduced. Batch preserves sequential awaits and single scoped
`DbContext`. All maps are method-local. No transaction/locking change.
`preserveProjectedStock` remains per-candidate isolated.

## 19. Scaling Results

| N | Before (overlay cmds) | After (batch cmds, simple fixture) |
|---|---|---|
| 1 | 6–12 | 8 |
| 20 | ~240 (miss) / ~120 (hit) | 8 |
| 100 | ~1,200 / ~600 | 8 |
| 500 | ~6,000 / ~3,000 | 8 |
| 1000 | ~12,000 / ~6,000 | 8 |
| 1001 | ~12,012 / ~6,006 | 14 |
| 2000 | ~24,000 / ~12,000 | 14 |

Cross-tenant: 1 tenant and 20 tenants both 8. Duplicates: 1,000× same id = 8.
Proven by interceptor counts; parity sampled every 10th product for N=100 plus
full 20-product equivalence.

## 20. Network Sensitivity

Cross-network round-trip latency was an amplifier, not the root cause. Batching
reduces round-trips by ~17–30× on a 20-product miss (240 → 8–14), so the endpoint
becomes proportionally less sensitive to per-round-trip latency. No claim that the
network was fixed.

## 21. Redis Limitation Separation

Known infra issue from prior report (replica write failures) is separated and
untouched. `GetOrCreateOwnedAsync` fall-through behavior unchanged; both factory
and post-cache loops are now batched, and the miss path skips the duplicate pass.
No cache keys, generations, TTLs, or invalidation changed. If Redis blocks a live
measurement, that failure is Redis, not SQL fan-out.

## 22. Tests

- New/updated: `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
  — 10/10 pass via `dotnet test ... --filter FullyQualifiedName~ProductSearchSqlAuthorityBatchTests`.
- Focused guards: 19/21 pass via storefront boundary/homepage/search/SEO/catalog
  filter; 2 failures baselined as pre-existing and unrelated (see §17).
- Not run: full suite (time-boxed), live Postgres integration, k6 load
  (env-gated). Explicitly not claimed as green.

## 23. Builds

- `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj -c Debug --no-restore`: succeeded, 0 errors (warnings only, pre-existing).
- `dotnet build PointOfSale.API/PointOfSale.API.csproj -c Debug --no-restore`: succeeded, 0 errors.
- `dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore`: succeeded, 0 errors.
- Sequential builds used to avoid shared-output `CS2012` locking seen with
  parallel builds. `git diff --check`-relevant line-ending warnings only; no
  whitespace errors introduced.

## 24. Load-Harness Integrity

No changes to `tests/load/k6/*`, thresholds, VUs, manifests, or orchestration.
Worktree `M`/`??` entries under `tests/load` pre-date this work and were left
untouched.

## 25. Defects Found

1. Double SQL recomposition per legacy-list miss (factory + unconditional
   post-cache).
2. Per-product category parent walk (N×depth).
3. Per-product affiliate-policy/category/units/promotions queries.
4. Per-tenant grouping on cross-tenant affiliate paths (T× batch cost).
5. Legacy list `TotalCount` regression risk: cache-miss population briefly used
   page-visible count instead of repository total; restored to `(int)totalCount`.
6. `StorefrontSearchController` tenant check omitted `DeletedAtUtc == null`
   (weaker than `TenantLineageAuthority`).
7. Batch affiliate policies keyed by `ProductId` only (cross-tenant overwrite risk
   for orphaned multi-tenant policy rows); fixed to `(TenantId, ProductId)`.
8. Prior bounded-query test used InMemory + mutable logger (not relational proof);
   replaced with SQLite interceptor counts.
9. Overstatement of batch as constant-query above 1,000 ids; corrected to
   chunk-bounded in code docs and tests.
10. Brittle architecture guards asserting single-product substrings on list paths;
    updated to recognize batch entry points (list-only; detail guards still require
    `ApplyAsync`).
11. Remaining cart revalidation loop (`CartService.RevalidateCartProductsAsync`:
    Mongo `FindByProductIdAsync` + `ApplyAsync` per cart item) — bounded by cart
    size/payload limits, not a storefront list fan-out, left as a documented
    limitation, not silently fixed in this certification.
12. Pre-existing unrelated guard failures (2 in focused slice) — documented, not
    introduced.

## 26. Defects Fixed

1–4 fixed by batch core + caller rewiring + cross-tenant set path.
5 fixed by restoring repository `totalCount` while keeping the duplicate-pass skip.
6 fixed by adding `tenant.DeletedAtUtc == null` to the unified-search tenant gate.
7 fixed by tuple-keyed `policiesByProduct` and tenant-scoped lookup.
8–9 fixed by SQLite `SqliteBatchTestDbContext` (Postgres check-constraint/`xmin`
   compatibility, repo convention) + `CountingInterceptor` exact assertions.
10 fixed by batch-aware guard updates.
11–12 left untouched/out-of-scope by design and documented.

## 27. Remaining Limitations

- Legacy list still does Mongo count+page plus one batch; search hydrates up to
  candidate windows before paging (bounded by 1,000-id chunks, heavier than a
  keyset rewrite — out of scope).
- Cart revalidation remains per-item Mongo+SQL (cart-bounded; a future
  `GetByProductIdsAsync` + `ApplyBatchAsync` conversion is possible but not
  required for storefront list certification).
- Single-detail routes (product detail, slug/landing, wishlist item, admin sync,
  consumers) intentionally remain per-item; no list fan-out there.
- `Unit.Name` in-memory ordering uses ordinal vs PostgreSQL collation (display
  order only).
- No live latency/pool/`pg_stat` evidence yet (env-gated; see Runtime Annex).

## 28. Certification Evidence

- Code: §3 traces, §5 inventory, §6 pool analysis, §7 semantics, §10 design, §11
  file list.
- Tests: §17, §22 (10/10 relational batch; 19/21 focused guards with 2 baselined
  pre-existing failures).
- Builds: §23 (Debug Infrastructure/API + Release API, 0 errors, sequential).
- Isolation: §12 (cross-tenant null/order/count proofs).
- Counts: §13, §19 (exact 8/14 interceptor equations).
- Runtime: Runtime Annex (no local listeners; health probes unanswered; defaults
  recorded; no pool/timeout changes).

## 29. Final Reconciliation

Observed ~244 for 20 products = ~240 overlay + ~4 middleware, explained by
12/product double invocation. After: same 20 products = 8 SQL commands in the
single batch call per request path (plus Mongo/Redis fixed costs), with identical
recomposed documents under sampled parity and exact relational bounds to 2,000
ids plus duplicates and cross-tenant streams. No authority, tenant, contract,
pool, cache, or harness change beyond the batch substitution plus the two
isolation tightenings in §26.

## 30. Final Verdict

**CONDITIONAL PASS** — root cause proven and removed (O(N)→chunk-bounded O(1)
per 1,000-id chunk, including cross-tenant), tenant/authority/correctness
preserved, relational regression tests and Debug/Release builds green. Live
latency/PostgreSQL/pool measurement is the outstanding environmental evidence
(no local runtime in this session; remote load-env access required). Safe to ship
behind normal rollout with live dashboard confirmation.

### Runtime Annex (2026-09-20, Windows session)

- `Get-Process dotnet`: two `dotnet` processes present (build/test hosts).
- `Get-NetTCPConnection -State Listen` for
  5000/5001/7000/7001/8080/80/443/6379/27017/9200/5432: only `:::80` listening
  (system); no API, PostgreSQL, Redis, MongoDB, or Elasticsearch local listeners.
- `Invoke-WebRequest http://localhost:5000/health` and `:5001/health` (5s):
  unable to connect; no deformation of running state attempted.
- `appsettings.Test.json`: `DefaultConnection` and JWT secrets intentionally empty;
  secrets via env/user-secrets/local override (not present here).
- `PostgresConnectionStringPolicy`: 15s connection timeout, 30s command timeout
  defaults; health-check override 5s. Unchanged.
- `AddDbContext(... UseNpgsql ...)`: no `MaxPoolSize`/lifetime/pooling override.
  Unchanged.
- User offered API stop/start if needed; no restart requested because dependencies
  are not locally reachable and no live measurement was possible without the
  remote K8s data plane. Offer recorded for a follow-up live pass if a base URL,
  tenant, and read-only DB/pool observability can be provided.
