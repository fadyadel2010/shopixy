# MAIN M4.5 Mongo Demand Remediation B1 Report

**Date:** 2026-09-25
**Scope:** MAIN production code remediation only. Mongo capacity, server configuration, driver version, projections, list/search/category caching, and load infrastructure are out of scope.

Evidence labels used below: **CODE-PROVEN**, **TEST-PROVEN**, **LOAD-PROVEN**, and **INFERRED**. QA load evidence remains QA evidence and is never classified as MAIN load proof.

## 1. MAIN Baseline

- Git root: `C:/Users/fadya/source/repos/PointOfSale` - **CODE-PROVEN**.
- Branch: `main`, tracking `origin/main` - **CODE-PROVEN**.
- Baseline commit: `32ffffcc2fdcd376354c19e016a67596eefa88a5` (`fix performance issues`) - **CODE-PROVEN**.
- Visible branches: `main`, `feature/pearl-golden-theme`, and their `origin/*` counterparts. No separate release branch is visible - **CODE-PROVEN**.
- The baseline worktree was already extensively modified and contained untracked files before B1. In particular, `CartService.cs`, `StorefrontProductService.cs`, `Program.cs`, `ApplicationDbContext.cs`, `RedisJsonCacheService.cs`, tenancy primitives/tests, and the QA reports already had user changes. B1 does not reset, clean, stash, or overwrite unrelated work - **CODE-PROVEN**.
- Solution: `PointOfSale.sln`; production projects: `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`; test project: `PointOfSale.Tests`; two frontend projects are also present - **CODE-PROVEN**.

## 2. QA Evidence Reviewed

- `docs/reports/M4.5_TENANT_AUTHORITY_CACHE_A1_AB_VALIDATION_REPORT_2026-09-25.md`
- `docs/reports/M4.5_MONGO_ROOT_CAUSE_DISCRIMINATION_REPORT_2026-09-25.md`
- `docs/reports/M4.5_MONGO_HIGH_CEILING_POOL1000_DIAGNOSTIC_REPORT_2026-09-25.md`

QA comparison source: `D:\PointOfSale` (not a Git repository according to the QA report). B1 reference implementation files:

- `D:\PointOfSale\PointOfSale.Infrastructure\MongoDB\Services\ProductLookupValidationCacheOptions.cs`
- `D:\PointOfSale\PointOfSale.Infrastructure\MongoDB\Services\ProductLookupValidationCache.cs`
- `D:\PointOfSale\PointOfSale.Infrastructure\MongoDB\Services\ProductReadRepository.cs`
- `D:\PointOfSale\PointOfSale.Infrastructure\MongoDB\MongoDbServiceCollectionExtensions.cs`
- `D:\PointOfSale\PointOfSale.Infrastructure\Redis\Services\StorefrontProductCacheInvalidator.cs`
- `D:\PointOfSale\PointOfSale.Tests\Mongo\M45ProductLookupValidationCacheTests.cs`

The QA implementation uses a singleton, per-process bounded `MemoryCache`, stores serialized full `ProductDocument` values or negative markers, uses `KeyedSingleFlight`, and bumps only an in-process tenant generation from `StorefrontProductCacheInvalidator` - **CODE-PROVEN**.

## 3. Root Cause Verification In MAIN

MAIN contains the source-backed demand mechanism - **CODE-PROVEN**:

- `FindByProductIdAsync` and `FindBySlugAsync` each issue one full-document Mongo `find` on every repository invocation.
- Neither method has a positive or negative repository-level cache.
- Nulls from the higher Redis product cache are not stored by `RedisJsonCacheService`.
- Cart add and variant update perform one product-id lookup per operation; ApplyDraft performs sequential per-item lookups and can perform a compensating lookup.
- Storefront detail and SEO resolution use these methods on higher-cache misses.
- Mongo reads are async and sequential per request. No retry loop, global semaphore, projection, or Mongo transaction is involved.
- MAIN has one singleton `MongoDbContext`/`MongoClient`; no pool-capacity override is present in production source.

QA established client pool/wait-queue capacity as the primary QA boundary and application-generated Mongo reads as a secondary mechanism - **LOAD-PROVEN in QA only**. No MAIN load validation is claimed.

## 4. MAIN Current Architecture

- Mongo is a candidate read model; PostgreSQL is current product, tenant, lifecycle, price, stock, variant, media, category, promotion, and publication authority - **CODE-PROVEN**.
- `ProductSearchSqlAuthorityOverlay` rebuilds those mutable fields from SQL for all production lookup callers before authorization, cart mutation, or visible response shaping, except the ApplyDraft default-variant fallback currently reads projected variants before retrying live validation - **CODE-PROVEN**.
- Redis product/detail/list caches are distributed and fenced by `StorefrontProductsInvalidationGenerationKey` - **CODE-PROVEN**.
- `StorefrontProductCacheInvalidator` is the single product-cache invalidation owner. Product projection writes call it after Mongo upsert/tombstone; stock, price, variant, media, promotion, repair, and operational paths also converge through it - **CODE-PROVEN**.
- MAIN deployment manifests do not define an authoritative production API replica count. Multi-replica operation must therefore be treated as possible - **INFERRED**.

## 5. MAIN Versus QA Comparison

| Area | MAIN current implementation | QA implementation | Difference and impact |
|---|---|---|---|
| Repository lookups | Direct full-document Mongo reads | Repository-wide local lookup cache | Root mechanism exists in MAIN. QA changes every caller, including a projection consumer. |
| Representation | No repository cache | Serialized full `ProductDocument` | Full documents duplicate mutable price/stock/media/variant state and consume materially more memory than caller requirements. MAIN can use identity only because SQL recomposes authority. |
| Invalidation | Distributed Redis generation plus post-projection invalidation | Local generation added to the existing invalidator | QA invalidation does not propagate to other API replicas. This is unsafe for the required immediate negative/slug convergence contract. |
| Concurrency | Existing `KeyedSingleFlight` primitive, used by Redis cache | Reused for local lookup loads | Primitive is portable, but invalidation architecture requires adaptation. |
| Transactions | SQL commits/outbox precede asynchronous Mongo projection; Mongo writer invalidates after projection mutation | Same basic hook | Commit-first semantics remain required. Cache invalidation must not authorize pre-commit state. |
| Security | Trusted tenant middleware strips inbound tenant header and restores only the resolved tenant; Mongo and SQL queries are tenant-scoped | Tenant ID embedded in local keys | Tenant keying is portable. Raw inbound headers must remain outside cache identity. |

Security impact: the QA key includes tenant identity, but replica-local invalidation can retain stale negatives/slug mappings elsewhere - **CODE-PROVEN**. Correctness impact: full-document hits are later SQL-recomposed in most paths, but ApplyDraft directly reads variants and therefore cannot safely receive a stale full snapshot without adaptation - **CODE-PROVEN**. Concurrency impact: same-key local single-flight is sound; no global lock is needed - **CODE-PROVEN**. Cache impact: MAIN's distributed generation is the existing native cross-replica fence and should be reused - **CODE-PROVEN**.

## 6. Complete Caller Inventory

| CALLER | METHOD | INPUT | TENANT SCOPE | PURPOSE | FIELDS USED | SECURITY-SENSITIVE | MUTABLE STATE USED | FINANCIAL STATE USED | INVENTORY/STOCK STATE USED | VISIBLE RESPONSE DATA | SAFE TO CACHE | WHY | REQUIRED INVALIDATION |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `StorefrontProductService.GetProductByIdAsync` | ID | trusted tenant + route product ID | Mongo + SQL tenant predicates | Product detail | identity before SQL; all output after SQL | Yes | lifecycle/affiliate/media | price | stock/variants | full detail DTO | Identity only | SQL rebuilds every returned field | Tenant product generation |
| `StorefrontProductService.GetProductBySlugAsync` | slug | trusted tenant + normalized slug | Mongo + SQL tenant predicates | Product detail/SEO route | identity/slug before SQL; all output after SQL | Yes | lifecycle/slug/affiliate/media | price | stock/variants | full detail DTO | Identity only | Current slug and all fields are SQL-checked | Tenant product generation |
| `SeoService.LoadCurrentVisibleProductBySlugAsync` | slug | trusted tenant + normalized slug | Mongo + SQL tenant predicates | Product/category/CMS slug resolution | identity then SQL; SQL fallback on Mongo miss | Yes | lifecycle/slug | no authority from Mongo | no authority from Mongo | resource identity/canonical slug | Identity/negative | SQL reauthorization and SQL fallback preserve fresh claims | Tenant product generation |
| `StorefrontLandingPageController.GetLandingPage` | slug | trusted `TenantContext` + route slug | Mongo + SQL tenant predicates | Affiliate landing response | identity then SQL product/policy fields | Yes | lifecycle/slug/affiliate/media | price | stock/variants | landing product/variants | Identity only after current-slug guard | SQL rebuilds mutable fields | Tenant product generation |
| `CartService.AddItemAsync` / `ValidateProductAsync` | ID | trusted tenant + request product ID | Mongo + SQL tenant predicates | Candidate lookup before cart mutation | identity then SQL-composed product | Yes | lifecycle/variant rules | live SQL price | live SQL stock | cart line | Identity/negative | owner check remains before lookup; all sellability is live SQL | Tenant product generation |
| `CartService.UpdateItemByVariantAsync` | ID | trusted tenant + product ID from tenant-owned cart | Mongo + SQL tenant predicates | Quantity/stock validation | identity then SQL product/variant | Yes | lifecycle/variant | live SQL price | live SQL stock | cart line | Identity/negative | fail-closed behavior and live SQL remain | Tenant product generation |
| `CartService.ApplyDraftAsync` fallback | ID | trusted tenant + draft product ID | Mongo + SQL tenant predicates | Select default variant after required-variant error | variants | Yes | variant lifecycle | variant price indirectly | variant active/stock later | cart line | Identity only after adaptation | Fallback must SQL-recompose before selection; retry still performs live validation | Tenant product generation |
| `ProductUpdatedEventConsumer` | ID | event tenant + product ID | validated event tenant; writer checks seed identity | Seed SQL-authoritative projection rewrite | identity only is sufficient | Yes | all rebuilt from SQL | all rebuilt from SQL | all rebuilt from SQL | none | Identity/negative | writer rejects mismatched seed and recomposes SQL | Tenant product generation after projection write |

No other production callers of these exact `IProductReadRepository` methods were found - **CODE-PROVEN**.

## 7. Port / Adapt / Replace / Reject Decision

**Decision before implementation: ADAPT.**

- PORT is rejected because QA's full-document, replica-local cache is larger than necessary and its local invalidation cannot prove immediate multi-replica convergence - **CODE-PROVEN**.
- REPLACE is not selected because the core mechanism (short absolute positive/negative TTLs plus keyed single-flight) is sound; MAIN only needs native representation and invalidation adaptations - **INFERRED from CODE-PROVEN constraints**.
- REJECT is not selected because MAIN contains the exact repeated read/null-miss mechanism and has an existing distributed generation suitable for safe freshness fencing - **CODE-PROVEN**.

Selected pre-implementation design:

- Cache only immutable product identity/existence: `Found`, `TenantId`, and `ProductId`; slug query identity remains in the exact cache key. Do not cache price, stock, variants, media, financial state, authorization results, exceptions, or mutable `ProductDocument` instances.
- Use a dedicated bounded `MemoryCache` per replica with absolute 30-second positive and 5-second negative TTLs.
- Before serving any local hit, read MAIN's distributed storefront-product invalidation generation from Redis and require an exact generation match. Redis/generation read failure bypasses the cache and loads Mongo.
- Read the distributed generation before and after a cold load; do not store if invalidation raced the load.
- Reuse `KeyedSingleFlight` for one local loader per exact tenant/lookup key. Different tenants and keys remain independent.
- Preserve exact Mongo slug query identity with a collision-resistant hash of the exact slug plus the visibility flag; do not case-fold at the repository cache layer.
- Keep all authorization, tenant, SQL lifecycle, pricing, stock, quantity, ownership, and response guards live.

This design is **CODE-PROVEN as compatible** with the caller and mutation architecture; implementation and test evidence are recorded in later sections after execution.

## 8. Implementation Summary

B1 implements the selected ADAPT design - **CODE-PROVEN**:

- `ProductLookupValidationCache` is a singleton, bounded, per-process identity/existence cache in front of the two exact repository lookup methods.
- The cache stores only `Found`, `TenantId`, `ProductId`, and the distributed generation observed when the value was loaded.
- Positive entries expire after 30 seconds by default; negative entries expire after 5 seconds by default; the default process-local cardinality ceiling is 8,192 entries.
- A local value is served only when the current Redis product-generation value is available and exactly matches the entry generation.
- Cold loads read generation before and after Mongo. A racing invalidation prevents the result from entering the local cache.
- Redis generation failure bypasses the local cache. Mongo loader failures and cancellation retain their original behavior and are not cached.
- ID and slug misses use per-key single-flight; no global lock or Mongo semaphore was added.

No mutable product authority was moved from PostgreSQL to the cache - **CODE-PROVEN**.

## 9. Cached Contract

The cached contract is deliberately narrower than `ProductDocument` - **CODE-PROVEN**:

| Cached | Not cached |
|---|---|
| found/not-found verdict | name, description, tags, SEO text |
| tenant ID | active/published/searchable flags |
| product ID | slug as mutable product state |
| generation fence | price, discounts, promotions, tax |
| lookup scope in the key | stock, reservations, backorder policy |
| | variants, attributes, media, categories |
| | affiliate policy, authorization verdicts, exceptions |

Repository callers receive a new identity-only `ProductDocument` on a positive result. Existing SQL-authority overlay paths then populate current state. This avoids sharing a mutable cached object between requests - **CODE-PROVEN and TEST-PROVEN**.

## 10. Key Design

- Product ID key: exact tenant ID plus exact product ID.
- Slug key: exact tenant ID, exact `onlineVisibleOnly` scope, and SHA-256 of the exact UTF-8 slug.
- Slugs are not trimmed, normalized, or case-folded at this layer because the cache key must match the existing Mongo equality query exactly.
- Raw request headers, user identity, authorization state, and response state are absent from all keys.
- Empty tenant/product IDs and empty slugs bypass the cache and preserve direct repository behavior.

Tenant, slug-case, visibility-scope, and distinct-key isolation are **TEST-PROVEN**.

## 11. Positive Cache Behavior

- Cold ID lookup: one Mongo loader call.
- Warm ID lookup at the same generation: zero additional Mongo loader calls.
- Cold slug lookup: one Mongo loader call.
- Warm slug lookup at the same generation: zero additional Mongo loader calls.
- Positive TTL expiry: the next request reloads Mongo.
- Returned values contain identity only and cannot retain caller mutations to mutable product state.

These behaviors are **TEST-PROVEN** by `M45ProductLookupValidationCacheTests`.

## 12. Negative Cache Behavior

- A null Mongo result is represented as an identity-free negative entry.
- A warm negative at the same generation costs zero additional Mongo loader calls.
- Negative TTL is independently bounded to 5 seconds by default and may not be configured above 10 seconds.
- Product creation/reactivation invalidation advances the tenant generation and makes an existing negative unusable immediately on the next lookup.
- Exceptions are never converted to negative entries.

These behaviors are **TEST-PROVEN**. "Immediately" means the next lookup after the distributed invalidation-generation advance completes; no MAIN load or distributed-latency claim is made.

## 13. Distributed Freshness Fence

`IRedisJsonCacheService.GetInvalidationGenerationAsync` exposes the same generation used by the existing distributed storefront product cache. The implementation returns `null` when Redis is disabled/unavailable or the generation read fails. `ProductLookupValidationCache` treats `null` as a mandatory bypass, not as generation zero - **CODE-PROVEN**.

This differs materially from QA's process-local generation. Every MAIN replica reads the shared generation before accepting its own local identity entry, so a completed generation advance fences all replicas without requiring process-local callbacks - **CODE-PROVEN**.

Warm B1 hits therefore exchange one cheap Redis generation read for avoiding a full Mongo product-document lookup. This is a demand-shaping tradeoff, not a zero-I/O cache - **CODE-PROVEN**.

## 14. Invalidation Integration

No second invalidation owner was introduced. `StorefrontProductCacheInvalidator` remains the product invalidation boundary and already advances `StorefrontProductsInvalidationGenerationKey` for product and tenant invalidation - **CODE-PROVEN**.

The B1 focused suite now invokes the real invalidation owner against the generation state and proves that a cached positive is not served after `InvalidateProductAsync` advances the shared key - **TEST-PROVEN**.

Existing projection/mutation code remains responsible for calling this owner only after the relevant mutation boundary. B1 does not introduce pre-commit cache authorization or a direct invalidation call from request handlers - **CODE-PROVEN**.

## 15. Invalidation Races

For a cold key, B1 performs:

1. Read generation G1.
2. Load Mongo.
3. Read generation G2.
4. Store only when G1 equals G2.

A focused test advances the generation while the Mongo loader is held open and proves that the raced value is returned to its original caller but is not cached; the following lookup reloads - **TEST-PROVEN**.

## 16. Concurrency And Cancellation

- Concurrent misses for the same exact key share one detached loader operation.
- Distinct product IDs, slug scopes, and tenants retain independent flights.
- A cancelled follower stops waiting without cancelling the shared loader or poisoning the resulting cache entry.
- A failed leader propagates the failure to joined callers, removes the in-flight entry, and permits a later retry.
- No global semaphore, process-wide Mongo throttle, or lock spanning unrelated keys was added.

These properties are **CODE-PROVEN and TEST-PROVEN**.

## 17. Failure Semantics

| Failure | B1 behavior |
|---|---|
| Redis disabled/unavailable | bypass local cache and execute Mongo loader |
| generation read exception | log warning, bypass local cache, execute Mongo loader |
| Mongo loader exception | propagate unchanged; do not cache |
| request cancellation before cache check | propagate cancellation |
| cancelled single-flight follower | cancel only that waiter |
| invalidation races cold load | return load to current caller, do not cache it |
| local cache write exception | log warning and return the loaded identity result uncached |
| mismatched tenant/product candidate | return loader result but never cache it |

Generation-read, loader-failure, cancellation, race, and mismatched-candidate behavior are **TEST-PROVEN**. The local write exception branch is **CODE-PROVEN** but was not fault-injected because `MemoryCache` is constructed internally.

## 18. Caller Adaptations

Two caller-specific adaptations were required - **CODE-PROVEN**:

- `CartService.ApplyDraftAsync`: after an identity lookup, the default-variant fallback now applies `ProductSearchSqlAuthorityOverlay` before reading variants. Variant selection therefore uses current SQL-composed state rather than a Mongo snapshot or identity shell.
- `StorefrontLandingPageController.GetLandingPage`: after SQL recomposition, the current SQL slug must equal the requested slug using ordinal comparison. A stale old-slug identity cannot produce a response under the old route.

All other exact lookup callers already SQL-recompose or use the Mongo result only as identity/seed evidence. Existing cart ownership, quantity, pricing, stock, publication, affiliate, and authorization checks remain live.

## 19. Dependency Injection And Configuration

- `IProductLookupValidationCache` is registered as a singleton.
- `IProductReadRepository` remains scoped.
- `ProductLookupValidationCacheOptions` binds optional section `MongoProductLookupCache`.
- Startup validation allows positive TTL `(0, 60]` seconds, negative TTL `(0, 10]` seconds, and maximum entries `[1, 100000]`.
- Defaults are embedded in the option type, so no deployment configuration change is required for B1.
- Existing two-argument/test repository constructors remain direct/uncached; production DI selects the three-argument constructor and injects the singleton cache.

Registration and repository wiring are **CODE-PROVEN and TEST-PROVEN**.

## 20. Expected Mongo Demand Effect

For repeated exact ID/slug lookups within TTL and without a product-generation change:

- pre-B1: one full Mongo `find` per repository call;
- B1: one full Mongo `find` for the cold key, then zero Mongo finds for warm positive or negative keys;
- same-key cold bursts: one shared Mongo loader per process rather than one per request.

Loader-count tests prove this mechanism - **TEST-PROVEN**. The production reduction in Mongo connections, wait-queue pressure, request failure rate, or latency is not known until a separate MAIN load run - **NOT LOAD-PROVEN**.

## 21. Capacity And Deployment Freeze

B1 changed none of the following:

- Mongo connection string options, `MaxConnectionPoolSize`, `MaxConnecting`, wait queue, timeouts, retry policy, server settings, indexes, topology, driver version, or deployment manifests;
- Npgsql/PgBouncer/PostgreSQL, Redis capacity, Kestrel limits, CPU/memory limits, API replica count, or k6 workload;
- list/search/category repository caching or publication caching.

This is a single demand-remediation mechanism, not a capacity increase - **CODE-PROVEN**.

## 22. Focused B1 Test Evidence

Command:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~M45ProductLookupValidationCacheTests --logger "console;verbosity=minimal"
```

Result: **21 passed, 0 failed, 0 skipped**, duration 887 ms - **TEST-PROVEN**.

Coverage includes positive ID/slug hits, positive and negative expiry, negative caching, create/reactivate/update/delete generation changes, slug rename, actual invalidation-owner wiring, tenant/key/scope isolation, identity-only representation, mismatched candidates, Redis generation failure, invalidation/load race, loader failure, same-key single-flight, distinct-key independence, follower cancellation, failed-leader recovery, and repository wiring.

## 23. Regression Evidence

Clean B1-adjacent regression command selected existing tests for cart identity, SQL product overlay and projection writing, slug authority, tenant isolation, production security, storefront identity mapping, product generation, recovery/rebuild, Npgsql acquisition remediation, and landing/pricing authority.

Result: **137 passed, 0 failed, 0 skipped**, duration 26 seconds - **TEST-PROVEN**.

The focused and clean regression commands together provide 158 passing executions. They are reported separately rather than claimed as unique test methods.

## 24. Build Evidence

- `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj -c Release --no-restore`: **0 errors**, 86 warnings.
- `dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore`: **0 errors**, 325 warnings.
- The warnings are existing repository/compiler/analyzer warnings; no B1 build error remains.
- An earlier focused test attempt overlapped the API build and failed with `CS2012` because both processes wrote shared Release `obj` outputs. The same test command passed when rerun serially; this was a command-concurrency artifact, not a test or product failure.

Build results are **TEST-PROVEN**.

## 25. Unrelated Dirty-Tree Regression Failures

Two broad exploratory sweeps were also run. They produced `122/128` and `141/144`. The nine failed executions are source-text architecture certifications for pre-existing modified files outside the B1 implementation:

- affiliate discovery active-category source shape;
- Redis owned-population/CAS source shape;
- direct HTTP `Cache-Control` header source shape;
- SEO and product-search expected source strings;
- homepage recommendation SQL-overlay source shape;
- affiliate repair parent-category source shape;
- product projection purchase-price source shape.

None of these failures references a B1-touched implementation line. They remain disclosed and unresolved because the worktree was already dirty and B1 must not overwrite unrelated user work. B1 does not claim a clean whole-repository suite.

## 26. MAIN Load Validation

**NOT RUN.** No k6, production-like, container, Kubernetes, Mongo telemetry, or distributed multi-replica load validation was executed in MAIN. QA load results remain evidence about QA only.

Therefore B1 is not labeled `LOAD-PROVEN`, and no throughput, latency, failure-rate, pool-pressure, or boundary-movement claim is made for MAIN.

## 27. Risks And Limits

- Each warm local hit still depends on a Redis generation read. Redis latency becomes part of this repository path, while Redis failure safely falls back to Mongo.
- Local entries consume bounded memory independently on each API replica. The 8,192 default is a count bound, not a byte-accurate budget.
- Completed invalidation is cross-replica fenced, but a Redis generation store reset/loss has the same durability considerations as the existing distributed product-cache generation architecture.
- Identity-only results rely on the inventoried callers continuing to SQL-recompose mutable state. New callers must not treat these two repository methods as full-document authority.
- The cache reduces repeated exact ID/slug demand only. Product lists, category queries, search, publication checks, and other Mongo reads are unchanged.
- Unit/regression evidence cannot prove behavior at the QA 3,000/6,000-VU boundary.

## 28. Rollback

The narrow rollback is:

1. Remove `IProductLookupValidationCache` injection/use from `ProductReadRepository` so both lookup methods call their Mongo loaders directly.
2. Remove the singleton/options registration and the two cache implementation files.
3. Remove `GetInvalidationGenerationAsync` only if no other concurrent work has adopted it.
4. Retain the ApplyDraft SQL overlay and landing current-slug guard unless a separate semantic review explicitly rejects those hardening changes; both are safe authority checks independent of cache demand reduction.

Rollback requires no data migration, cache flush, Mongo index change, or deployment configuration reversal.

## 29. B1 File Inventory

Created:

- `PointOfSale.Infrastructure/MongoDB/Services/ProductLookupValidationCacheOptions.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductLookupValidationCache.cs`
- `PointOfSale.Tests/Infrastructure/M45ProductLookupValidationCacheTests.cs`
- `MAIN_M4.5_MONGO_DEMAND_REMEDIATION_B1_REPORT_2026-09-25.md`

Modified for B1:

- `PointOfSale.Infrastructure/MongoDB/MongoDbServiceCollectionExtensions.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- `PointOfSale.API/Services/Cart/CartService.cs` (ApplyDraft adaptation only; the file contains other pre-existing changes)
- `PointOfSale.API/Controllers/StorefrontLandingPageController.cs`

`RedisJsonCacheService.cs` also contains pre-existing unrelated owned-population/single-flight edits in the dirty worktree. B1's intended addition there is the fail-open generation-read interface/implementation.

## 30. Change Summary

B1 replaces repeated exact product-ID/slug Mongo validation reads with a bounded, short-lived, identity-only local cache. MAIN's existing distributed Redis product generation fences every local hit across replicas, and same-key misses are coalesced. Positive and negative values expire independently, failures fall back to existing Mongo behavior, and mutable/financial/inventory/security state remains live in PostgreSQL and request authorization paths.

The QA approach was not copied verbatim: full document storage and replica-local invalidation were rejected. MAIN-specific caller adaptations close the only direct variant-use path and stale-slug response path discovered by the complete caller inventory.

## 31. Final Verdict

**B1 IMPLEMENTED AND CODE/TEST VERIFIED; MAIN LOAD VALIDATION NOT RUN.**

- Root repeated-read mechanism: present in MAIN and remediated for exact ID/slug lookups.
- Architecture decision: **ADAPT**.
- Focused result: **21/21 passed**.
- Clean B1-adjacent regression result: **137/137 passed**.
- Infrastructure/API Release builds: **0 errors**.
- Tenant isolation, SQL authority, failure fallback, bounded TTL/cardinality, distributed invalidation fencing, and single-flight behavior: retained by design and covered by code/tests.
- Mongo capacity/pool settings: unchanged.
- Performance/load outcome in MAIN: **UNKNOWN / NOT RUN**, not inferred from QA.
