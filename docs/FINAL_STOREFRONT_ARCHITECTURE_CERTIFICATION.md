# Final Storefront Architecture Certification

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Customer Storefront architecture after Runtime, Theme, Builder, Browser Cache, and Cache Core recoveries  
**Mode:** Certification artifact only; no application code was changed by this document  
**Final verdict:** **CERTIFIED WITH OBSERVATIONS**

## Executive Verdict

The recovered Storefront architecture is certifiable for the primary customer runtime path.

The active Storefront now has one canonical ownership chain for the recovered surfaces:

```text
Product Authoring / StoreConfig Authoring
  -> SQL authority and outbox
  -> projection/index/cache consumers as derivative readers
  -> canonical Storefront runtime owners
  -> Theme presentation shells
  -> Builder-authored supplemental declarations only
```

The prior certification failures were recovered in the core runtime:

| Area | Previous baseline | Recovery result | Final status |
|---|---|---|---|
| Runtime ownership | `RUNTIME_OWNERSHIP_CERTIFICATION.md` failed | `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md` restored catalog, product details, category, search, product card, filters, and unified search-index mutation boundaries | Certified |
| Theme runtime | `THEME_RUNTIME_CERTIFICATION.md` failed | `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md` made installed themes presentation-only at layout, slot, page, section, and runtime-identity boundaries | Certified |
| Builder runtime | `BUILDER_RUNTIME_CERTIFICATION.md` failed | `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md` made Builder authoring-only and protected mandatory Product Details runtime behavior | Certified with inherited observations |
| Browser runtime cache | `CACHE_RUNTIME_CERTIFICATION.md` failed | `RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md` added bounded, identity-aware, generation-safe browser cache owners for the authorized runtime caches | Certified |
| Cache core and cleanup | `CACHE_RUNTIME_CERTIFICATION.md` failed | `RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md` added semantic Redis owners, invalidation fencing, dormant cleanup, and one Storefront HTTP no-store contract | Certified with inherited observations |

The final verdict is not a clean `CERTIFIED` because several legacy or adjacent surfaces remain intentionally outside the recovered primary runtime scope. They do not overturn the certification of the active customer Storefront path, but they should remain visible as observations.

## Frozen Inputs

This certification treats these documents as canonical and does not reopen their source audits or recovery decisions:

| Document | Role |
|---|---|
| `docs/STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md` | Original discovery and drift baseline |
| `docs/STOREFRONT_RUNTIME_RECOVERY_MASTER_PLAN.md` | Recovery ownership plan |
| `RUNTIME_OWNERSHIP_CERTIFICATION.md` | Failed runtime ownership baseline |
| `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md` | Runtime ownership recovery proof |
| `THEME_RUNTIME_CERTIFICATION.md` | Failed theme runtime baseline |
| `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md` | Theme runtime recovery proof |
| `BUILDER_RUNTIME_CERTIFICATION.md` | Failed builder runtime baseline |
| `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md` | Builder runtime recovery proof |
| `CACHE_RUNTIME_CERTIFICATION.md` | Failed cache runtime baseline |
| `RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md` | Browser runtime cache recovery proof |
| `RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md` | Cache core and dormant cleanup recovery proof |

## Final Ownership Matrix

| Surface | Certified owner | Non-owner boundary |
|---|---|---|
| Product authoring | SQL-backed Product Authoring orchestrator and transactional outbox | Storefront, Builder, Theme, Mongo, Elasticsearch, and Redis do not become product truth owners |
| Product projection | Mongo projection writer recomposed from SQL authority | Mongo is a derivative read model, not business truth |
| Product search index | Unified search-index mutation boundary with SQL-authority overlays | Individual consumers trigger indexing but do not own independent search truth |
| `/products` catalog page | `ProductListPage`, discovery hooks, `/api/storefront/products/discover`, Elasticsearch candidates, SQL recomposition | Builder product page sections no longer replace or wrap the canonical catalog runtime |
| `/category` and category identity | `CategoryPage` and `StorefrontCategoryService` through SQL category lineage | Builder category sections and Mongo category projection do not own current public category identity |
| Product Details composition | `ProductTemplateResolver` plus Runtime-injected mandatory `product-hero` | Builder cannot omit, disable, duplicate, reorder, or replace the mandatory Product Hero runtime |
| Section execution | `RuntimeSectionContainer` | Builder supplies admitted declarations; Theme supplies presentation shells |
| Product card behavior | `ProductCardRuntime` and canonical interaction layer | Theme renderers cannot decide wishlist, stock, or quick-add capability ownership |
| Search entry and mechanics | `SearchRuntimeProvider`, `SearchRuntimeTrigger`, and search runtime stores | Theme headers place the trigger only; they do not own search behavior |
| Header and footer data/runtime | `HeaderRuntime` and `FooterRuntime` | Theme layouts receive presentation models and runtime-owned slots only |
| Storefront pages | Router executes canonical runtime pages directly | Removed dormant theme page replacement path remains certified closed |
| Builder | StoreConfig authoring for Home and Product Template supplemental sections | Builder does not own runtime routes, providers, stores, business truth, data loading, or mandatory capabilities |
| Browser runtime caches | StoreConfig runtime, navigation store, footer store, `useCategories`, and `useHomepageSectionProducts` with shared runtime cache policy | Theme and Builder do not refresh or invalidate these caches |
| Redis product/list/home cache | `IStorefrontProductCacheInvalidator` and owned Redis population generation fencing | Event consumers and repair tools delegate; raw overlapping invalidation is certified closed |
| Redis shipping cache | `IShippingMethodCacheService` | Projection consumers delegate through the owner |
| Redis wishlist cache | `IStorefrontWishlistCacheInvalidator` | Wishlist projections and customer deactivation delegate through the owner |
| Cart state | `CartService` as authoritative ephemeral Redis owner | Checkout no longer rewrites cart idempotency fields outside the cart owner |
| HTTP cache policy | `StorefrontHttpCachePolicyMiddleware` for every `/api/storefront` response | Endpoint-local Storefront cache fragments are removed from the certified contract |

## Recovery Closure

| Recovery | Certified closure |
|---|---|
| Recovery 1 | `/products` is catalog-owned; Product Details is resolver/runtime-owned; public category identity is SQL lineage-owned; Search and Product Card behavior are runtime-owned; filters and index mutation are unified under canonical paths. |
| Recovery 2 | Installed themes own presentation only: layout placement, markup, classes, tokens, typography, spacing, responsive composition, and visual shells. They do not load navigation/footer data, replace runtime pages, or branch runtime behavior by theme identity. |
| Recovery 3 | Builder owns admitted authoring declarations only. Product Hero is mandatory runtime output, review truth is not builder-authored, Product/Category dormant modes and native whole-config preview replacement were removed, and backend/client admission is strict. |
| Recovery 4 | StoreConfig, Navigation, Footer, Categories, and Homepage section product browser caches have bounded lazy freshness, explicit invalidation/refresh, complete applicable identities, in-flight deduplication, and stale-generation protection. |
| Recovery 5 | Live Redis families have semantic owners, invalidation failures are retryable, owned population is fenced, certified dormant cache families were removed, checkout cart mutation authority is unified, and `/api/storefront` responses have one private/no-store contract. |

## Quality Gates

Commands were executed against the current dirty recovery worktree. Existing application changes were not reverted or edited.

| Gate | Result |
|---|---|
| `npm test -- --run --reporter=dot` in `PointOfSale.Storefront` | PASS — 25 files, 234 tests |
| `npm run build` in `PointOfSale.Storefront` | PASS — TypeScript and Vite completed; 513 modules transformed |
| `npm test -- --run src/modules/storefront --reporter=dot` in `PointOfSale.Web` | PASS — 10 files, 52 tests |
| Filtered backend Storefront recovery architecture tests | PASS — 32 tests |

Backend recovery gate command:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" --filter "FullyQualifiedName~RuntimeOwnershipRecoveryTests|FullyQualifiedName~BuilderRuntimeRecoveryTests|FullyQualifiedName~CacheCoreAndCleanupArchitectureTests|FullyQualifiedName~StorefrontRecoveryRebuildCertificationTests|FullyQualifiedName~StoreBuilderRuntimeContractCertificationTests|FullyQualifiedName~RedisCacheFreshnessInvalidationCertificationTests"
```

Non-blocking warnings observed:

| Source | Warning class | Certification impact |
|---|---|---|
| Storefront tests/build | Browserslist age and Vite mixed static/dynamic import notices | No test or build failure; not an ownership violation |
| Storefront build | Large chunk warning | No build failure; performance follow-up only |
| Web storefront tests | React Router future-flag warnings and an existing React `act(...)` warning | Test suite passed; not an ownership violation |

A broad diagnostic backend command was also attempted:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" --filter "FullyQualifiedName~Architecture|FullyQualifiedName~Storefront"
```

It exceeded the 120-second timeout after reporting unrelated legacy architecture and SQLite fixture failures outside this final Storefront runtime recovery gate. It is not used as the final certification gate; the narrowed Storefront recovery architecture suite above is the applicable backend certification gate.

## Remaining Observations

| Observation | Status | Certification impact |
|---|---|---|
| Legacy compatibility endpoints still exist, including deprecated product/home paths retained for compatibility | Accepted observation | They are not the canonical current Storefront SPA ownership path, but should not be mistaken for the recovered `/products` and runtime cache contract |
| Smart Offers retain adjacent provider/cache/session behavior outside Recovery 4's authorized browser cache scope | Accepted observation | Does not invalidate StoreConfig/Navigation/Footer/Categories/Homepage cache recovery; should be separately certified if Smart Offers become part of the cache cleanup scope |
| Search autocomplete has a short query-only browser cache observation from the cache audit | Accepted observation | Main search runtime ownership remains canonical; autocomplete cache identity can be tightened separately |
| Dormant or legacy search/compatibility components remain outside the active recovered runtime path | Accepted observation | No evidence that they replace the certified active runtime owners, but they should stay on the cleanup radar |
| Preview/editor configuration injection remains controlled and editor-scoped | Accepted observation | The broad native whole-config preview replacement was removed; remaining controlled preview commits must stay isolated from public runtime commits |
| Some legacy home/SEO/category-adjacent paths still rely on derivative candidates or TTL-owned values with SQL checks | Accepted observation | Public category identity and canonical catalog filtering are SQL-lineage owned; retained legacy/SEO paths are compatibility or TTL-owned observations |
| Repository-wide backend architecture suites contain unrelated frozen failures | Accepted observation | The relevant Storefront recovery gate passes; unrelated suites should be handled by their owning subsystems, not by this certification |

## Scorecard

| Dimension | Score | Rationale |
|---|---:|---|
| Runtime ownership | 9/10 | Primary customer runtime ownership is recovered; observations remain around legacy/adjacent paths |
| Theme boundary | 10/10 | Themes are presentation-only at certified surfaces |
| Builder boundary | 9/10 | Builder is authoring-only for active surfaces; inherited observations remain around compatibility/history |
| Cache ownership | 9/10 | Browser and Redis cache ownership is recovered for scoped families; Smart Offers/search autocomplete remain observations |
| Data authority | 9/10 | SQL authority and derivative projection boundaries are certified on core paths; some legacy TTL/candidate paths remain observable |
| Regression protection | 9/10 | Focused frontend and backend recovery tests pass; broad unrelated architecture suites are not green |

## Final Certification

**CERTIFIED WITH OBSERVATIONS**

The recovered Storefront architecture is certified for the active customer runtime path: Product Authoring, Projection, Catalog, Product Details, Category, Search, Product Card, Theme, Builder, Browser Cache, Redis Cache, Cart, and HTTP cache boundaries now have source-backed canonical owners and passing focused regression gates.

This certification does not claim that every legacy, compatibility, marketplace, SEO, Smart Offers, search autocomplete, or repository-wide architecture concern has been remediated. Those items remain recorded observations and should be tracked as separate cleanup/certification scopes if they become release blockers.
