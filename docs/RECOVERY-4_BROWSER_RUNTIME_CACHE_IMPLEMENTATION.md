# Recovery 4 — Browser Runtime Cache Implementation

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Canonical baseline:** `CACHE_RUNTIME_CERTIFICATION.md`  
**Scope:** Browser Runtime Cache Recovery only

## Certification Result

**PASS**

Browser Runtime Cache Recovery is complete for the authorized Sprint 1 scope. The implementation recovers C-01, C-02, and the applicable browser-key portion of C-10 for StoreConfig, Navigation, Footer, Categories, and Homepage section products.

No Redis, server cache, projection, Elasticsearch, MongoDB, HTTP cache, dormant cache, Product Authoring, Catalog, Runtime Ownership, Theme Runtime, or Builder Runtime recovery was opened or changed.

## Canonical Baseline

The following references were read completely before source changes:

1. `CACHE_RUNTIME_CERTIFICATION.md`
2. `docs/STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md`
3. `docs/STOREFRONT_RUNTIME_RECOVERY_MASTER_PLAN.md`
4. `RUNTIME_OWNERSHIP_CERTIFICATION.md`
5. `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`
6. `THEME_RUNTIME_CERTIFICATION.md`
7. `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`
8. `BUILDER_RUNTIME_CERTIFICATION.md`
9. `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`

`CACHE_RUNTIME_CERTIFICATION.md` was treated as the sprint authority. The completed Product Authoring, Projection Pipeline, Catalog Ownership, Runtime Ownership, Theme Runtime, and Builder Runtime boundaries remained frozen.

## Recovered Violations

| Violation | Before | After | Result |
|---|---|---|---|
| C-01 — No browser invalidation | Successful StoreConfig, Navigation, Footer, Category, and Homepage snapshots had no complete Runtime invalidation surface. | Every in-scope owner now exposes explicit invalidation and refresh. Invalidating an in-flight generation prevents its late response from repopulating the cache. | Recovered |
| C-02 — Unbounded successful browser caches | Successful entries had no freshness bound and could be reused for the browser module lifetime. | One shared five-minute lazy max-age policy applies to all five owners. Expiration is evaluated on Runtime access; no timer or polling loop exists. Manual refresh and force refresh remain available. | Recovered |
| C-10 — Incomplete applicable browser keys | Footer did not include language identity. Homepage responses were retained without language or affiliate request identity. | Footer is keyed by tenant and language and reloads on language change. Homepage keys include tenant, language, affiliate identity, source, size, seed, and curated IDs. StoreConfig also distinguishes signed Theme preview state. | Recovered |

## Architecture Before and After

### StoreConfig Runtime Cache

**Before:** Bootstrap fetched StoreConfig directly, committed it to Zustand, and permanently marked bootstrap successful. Concurrent bootstrap callers polled the singleton every 50 ms. The store had no canonical refresh owner and could not distinguish Runtime data from a controlled preview candidate.

**After:**

- `storeConfigRuntime.ts` is the single Runtime-owned StoreConfig fetch/apply/commit path.
- Bootstrap calls `refreshStoreConfig()` rather than fetching and committing independently.
- `refreshStoreConfig`, `forceRefreshStoreConfig`, and `invalidateStoreConfigRuntimeCache` provide manual refresh, controlled force refresh, and explicit invalidation.
- Concurrent bootstrap and StoreConfig refresh calls share promises without polling.
- Runtime commits use `setRuntimeConfig`; existing `setConfig` remains the controlled preview/editor commit.
- Ordinary refresh cannot overwrite a preview candidate. `allowPreviewOverride` is required for an explicit controlled replacement.
- Theme application remains part of the canonical Runtime refresh commit.

### Navigation Runtime Cache

**Before:** The first successful `loadNavigation()` permanently suppressed later loads.

**After:**

- Navigation retains Zustand ownership.
- `loadNavigation({ force })`, `refreshNavigation`, `invalidateNavigation`, `refreshNavigationCache`, and `invalidateNavigationCache` provide the complete Runtime control surface.
- Tenant identity and lazy max-age determine whether a stored result is reusable.
- Same-identity concurrent refreshes deduplicate.
- Generation fencing prevents an invalidated or superseded request from overwriting a newer result.
- A refresh failure preserves the last valid presentation value while marking the entry invalid for retry.

### Footer Runtime Cache

**Before:** The first successful footer survived for module lifetime, and a language change could retain the previously localized response.

**After:**

- Footer retains Zustand/`FooterRuntime` ownership.
- `loadFooter({ force })`, `refreshFooter`, `invalidateFooter`, `refreshFooterCache`, and `invalidateFooterCache` provide refresh and invalidation.
- Cache identity includes tenant and language.
- `FooterRuntime` invokes the owner when language changes; the changed identity forces a new localized request.
- Concurrent requests deduplicate and stale generations cannot commit.

### Category Runtime Cache

**Before:** The tenant-keyed module `Map` stored successful arrays indefinitely. Force refresh could race an older request, and the last completion could repopulate stale data.

**After:**

- `useCategories` remains the owner and retains its module `Map` architecture.
- `refreshCategoryCache` and `invalidateCategoryCache` are production Runtime controls; `clearCategoryCache` remains a compatibility alias.
- Existing same-key in-flight deduplication is preserved.
- Each entry carries `loadedAt`, generation, and its current promise.
- Cache invalidation removes the entry; a late request can no longer repopulate it.
- Hook request sequencing prevents an older completion from replacing a newer local result.

### Homepage Runtime Cache

**Before:** The module `Map` retained successful shelf responses indefinitely. Manual refetch deleted one value but did not have generation-safe invalidation or complete request identity.

**After:**

- `useHomepageSectionProducts` remains the owner and retains its module `Map` and request deduplication.
- `refreshHomepageSectionProductsCache` and `invalidateHomepageSectionProductsCache` provide Runtime refresh, force refresh, and invalidation; the old clear function remains a compatibility alias.
- Entries carry lazy expiry, generation, and an in-flight promise.
- Invalidated entries cannot be resurrected by late requests.
- Hook request sequencing protects mounted presentation state.
- Keys include all applicable existing request identity: tenant, language/locale, affiliate identity, source, page size, deterministic seed, and curated product IDs.

## Shared Browser Runtime Cache Policy

`runtimeCachePolicy.ts` owns the common browser policy:

- five-minute maximum reusable age;
- lazy expiration on owner access;
- tenant identity for every in-scope cache;
- optional language, affiliate, and signed Theme-preview identity;
- no interval, timeout, background polling, focus loop, WebSocket, SignalR, EventSource, or service-worker mechanism.

Lazy expiration preserves browser speed: a fresh value remains an immediate cache hit, while an expired value refreshes only when its existing Runtime owner accesses it.

## Ownership

| Cache | Owner after Recovery 4 | May refresh/invalidate | Key identity |
|---|---|---|---|
| StoreConfig | StoreConfig Runtime through `storeConfigRuntime.ts` and the Zustand state container | Runtime refresh API; bootstrap is a client; controlled preview remains protected | tenant + signed Theme-preview mode |
| Navigation | Navigation Zustand store, invoked by `HeaderRuntime` | Navigation store actions and exported Runtime helpers | tenant |
| Footer | Footer Zustand store, invoked by `FooterRuntime` | Footer store actions and exported Runtime helpers | tenant + language |
| Categories | `useCategories` module cache | Hook refetch and exported Runtime helpers | tenant |
| Homepage products | `useHomepageSectionProducts` module cache | Hook refetch and exported Runtime helpers | tenant + language + affiliate + collection inputs |

Theme does not refresh or invalidate these caches. Builder does not refresh or invalidate them. Runtime remains the exclusive execution owner.

## Files Changed

### Runtime policy and StoreConfig owner

- `PointOfSale.Storefront/src/storefront/runtime/cache/runtimeCachePolicy.ts` — new shared lazy-expiration and identity policy.
- `PointOfSale.Storefront/src/storefront/runtime/cache/storeConfigRuntime.ts` — new canonical StoreConfig refresh/invalidation owner.
- `PointOfSale.Storefront/src/storefront/runtime/cache/index.ts` — Runtime cache exports.
- `PointOfSale.Storefront/src/bootstrap/bootstrap.ts` — delegates StoreConfig loading and replaces bootstrap polling with shared promise completion.
- `PointOfSale.Storefront/src/bootstrap/index.ts` — exports StoreConfig Runtime controls.
- `PointOfSale.Storefront/src/store/storeConfigStore.ts` — separates Runtime commits from controlled preview commits.

### Navigation and Footer owners

- `PointOfSale.Storefront/src/stores/navigationStore.ts`
- `PointOfSale.Storefront/src/stores/footerStore.ts`
- `PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx` — language change re-enters the Footer owner.

### Category and Homepage owners

- `PointOfSale.Storefront/src/hooks/useCategories.ts`
- `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts`
- `PointOfSale.Storefront/src/hooks/index.ts`

### Regression coverage and implementation record

- `PointOfSale.Storefront/src/storefront/runtime/cache/browserRuntimeCacheRecovery.test.ts`
- `RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md`

No backend, Redis, MongoDB, Elasticsearch, Projection, Web Builder, or Theme source file was changed by Browser Runtime Cache Recovery.

## Regression Tests

The focused Storefront regression suite proves:

- lazy max-age becomes stale at the configured boundary;
- StoreConfig cache hits, force refresh, invalidation, and concurrent deduplication;
- controlled preview StoreConfig is not overwritten by ordinary Runtime force refresh;
- a late invalidated Navigation request cannot overwrite a newer result;
- concurrent Navigation force refreshes deduplicate;
- Footer reloads for a changed language identity;
- concurrent Footer force refreshes deduplicate;
- Category refreshes deduplicate and invalidation requires a new request;
- Homepage refreshes deduplicate, language creates a separate identity, and invalidation requires a new request;
- recovered production sources contain no polling, timer, WebSocket, SignalR, or EventSource mechanism.

## Validation

| Validation | Result |
|---|---|
| Focused browser cache recovery tests | **PASS** — 1 file, 11 tests |
| Full Storefront suite: `npm test -- --run --reporter=dot` | **PASS** — 25 files, 234 tests |
| Storefront production build: `npm run build` | **PASS** — TypeScript and Vite completed; 513 modules transformed |
| Web tests | Not affected — Recovery 4 changed no `PointOfSale.Web` source |
| Web production build: `npm run build` | **PASS** — 5,065 modules transformed |
| .NET solution build: `dotnet build PointOfSale.sln --no-restore --nologo --verbosity:minimal -m:1` | **PASS** — 0 warnings, 0 errors |
| Scoped `git diff --check` | **PASS** |

The Storefront build reported existing informational Browserslist, mixed-import, and bundle-size warnings. They did not affect compilation or the cache recovery result.

## Search Evidence

Positive source searches confirm:

- StoreConfig exposes `refreshStoreConfig`, `forceRefreshStoreConfig`, and `invalidateStoreConfigRuntimeCache`, and bootstrap calls the canonical refresh owner.
- Navigation exposes `refreshNavigation`/`invalidateNavigation` and public Runtime helper functions.
- Footer exposes `refreshFooter`/`invalidateFooter`, includes language identity, and `FooterRuntime` re-enters the owner on language change.
- Categories expose `refreshCategoryCache`/`invalidateCategoryCache`.
- Homepage exposes `refreshHomepageSectionProductsCache`/`invalidateHomepageSectionProductsCache` and uses language-aware identity.
- Every cache records freshness metadata and uses generation or request-identity commit guards.

The prohibited-mechanism search across all recovered production files returned:

```text
NO_PROHIBITED_RUNTIME_MECHANISMS_FOUND
```

The search covered `setInterval`, `setTimeout`, `WebSocket`, `EventSource`, `SignalR`, and `HubConnectionBuilder` execution patterns.

## Stop Boundary

Recovery stops after Browser Runtime Cache Recovery.

The following were not started:

- Redis Recovery
- Cache Ownership Recovery
- Dormant Cache Cleanup
- HTTP Cache Contract
- Final Storefront Certification

## Final Certification

All authorized Browser Runtime Cache violations are recovered:

- browser caches have explicit Runtime refresh and invalidation;
- successful values have a common finite lazy freshness bound;
- StoreConfig supports manual, invalidated, forced, concurrent, and preview-safe refresh;
- Navigation, Footer, Categories, and Homepage support safe concurrent refresh;
- Footer refreshes on language identity change;
- applicable browser keys contain the existing tenant/language/affiliate/preview identity;
- no polling, timer, WebSocket, SignalR, event-bus redesign, React Query, or TanStack Query mechanism was introduced;
- server caches and frozen architecture remain unchanged.

**PASS**
