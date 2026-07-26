# Search Runtime — FINAL CANONICAL ARCHITECTURE

> Implementation-backed architecture. Every claim is cited to `file:line` in the
> Shopixy / PointOfSale backend. Markdown, comments, and prior certification docs
> were re-verified against code and treated as secondary. Where code evidence was
> not found, the claim is omitted or marked "no implementation evidence".

---
11
## Executive Summary

The Search Runtime is the **Discovery & Retrieval Runtime** of Shopixy. It is NOT a
search page — it is a bounded context spanning catalog discovery, autocomplete,
suggestion, ranking, filtering, faceting, availability gating, indexing, and
projection repair.

The defining architectural truth of this runtime is **PostgreSQL is the uncontested
authority; Elasticsearch (ES), MongoDB, and Redis are projections that are re-asked
for truth on every read/index path.** No public or admin path lets ES, Mongo, or
Redis originate catalog/search truth. Every search-document write first runs a
SQL-authority overlay (`ProductSearchSqlAuthorityOverlay`,
`CommerceHubSearchSqlAuthorityOverlay`) that recomposes the document from PostgreSQL
and **tombstones/deletes** it whenever SQL no longer proves visibility.

Two search surfaces exist:
- **Storefront / Merchant product search** — index `products`, driven by
  `ProductSearchIndexService` + `ProductSearchSqlAuthorityOverlay`, hydrated from
  Mongo `products` and re-overlaid from SQL before presentation.
- **CommerceHub marketplace search** — index `commercehub_products` (aliased,
  versioned), driven by `CommerceHubSearchIndexService` +
  `CommerceHubSearchSqlAuthorityOverlay`, fed from the Mongo `commercehubproducts`
  read-model, with a zero-downtime alias-migration reindex and an auto-healing
  self-repair loop.
- **Marketplace (Service) discovery** — index `marketplace_services`, driven by
  `MarketplaceSearchService`, recomposed from SQL `Service`/`MarketplaceProfile`
  authority.

Search is **eventually consistent** (event-driven projection via MassTransit
consumers) and **repair-on-read** (SQL authority re-applied per request). ES failure
is fail-closed for storefront product search (`StorefrontSearchUnavailableException`
→ 503) but degrades gracefully for the unified/header path and for CommerceHub (null
→ Mongo fallback in the read service).

---

## Architecture Vision

Shopixy treats search as a **derived projection of SQL truth**, not a separate
datastore of record. The vision is "Shopify-grade multilingual search" achieved
without translation services, AI, or runtime language detection: searchable text is
enriched at **index time** (`SearchEnrichment.BuildSearchText`) with cross-language
brand keywords, and ES analyzers (`multilingual_analyzer`, `arabic_custom`,
`autocomplete_analyzer`, ASCII folding, edge-ngrams) handle Arabic/Latin matching
and typo tolerance.

The runtime is deliberately layered:
1. **Write authority** stays in SQL (Product / ProductVariant / CommerceHubListing /
   Promotion / Service).
2. **Mongo** is the operational read-model and the *staging* source for ES documents.
3. **ES** is the full-text / ranking / faceting engine.
4. **Redis** caches only ranking config and promotion badges — never search truth.
5. **SQL authority overlay** is the gate that every path re-runs before mutating ES
   or returning a result.

---

## Search Runtime Purpose

Provide discovery and retrieval for: products (storefront + merchant), marketplace
listings (CommerceHub), marketplace services, categories, CMS pages, with
multilingual relevance ranking, faceting, autocomplete, suggestions, SKU/barcode
resolution, promotion-aware boosting, and availability gating — all tenant-isolated
and SQL-authoritative.

## Search Runtime Scope

In scope: product/category/brand/variant/SKU/barcode search; autocomplete;
suggestions; ranking; filtering; faceting; availability & publication gates; ES
indexing; Mongo projection; reindex/rebuild; repair-on-read; outbox-driven sync;
CommerceHub marketplace search; marketplace service discovery; search analytics.

Out of scope (owned by other runtimes): catalog *mutation* (Product/Variant
lifecycle owners), inventory *execution* (Inventory Runtime), pricing *authority*
(Promotion/Pricing services), order/payment lifecycle, accounting, CMS authoring
(search only consumes published pages), affiliate *commission* authority (search only
exposes `IsAffiliateEnabled` as a hard filter).

## Search Runtime Owns

- ES index lifecycle for `products`, `categories`, `cmspages`, `commercehub_products`,
  `marketplace_services`.
- The `ProductSearchDocument` / `CommerceHubProductSearchDocument` /
  `CategorySearchDocument` / `CmsPageSearchDocument` / `MarketplaceServiceDocument`
  ES projections (mappings, analyzers, enrichment).
- Relevance ranking **functions** (function_score weighting, fuzziness, boosts) — but
  NOT the business authority those weights read.
- The SQL-authority overlay **recomposition** logic that decides search visibility.
- Reindex/rebuild orchestration and the CommerceHub auto-healing self-repair loop.

## Search Runtime Does NOT Own

- Product/Variant CRUD, publication, price, SKU, barcode → owned by catalog + pricing
  + inventory runtimes (SQL authority).
- Inventory truth → `ProductVariantStock` (Inventory Runtime). Search only reads
  `Quantity - ReservedQuantity`.
- Promotion truth → Promotion runtime; search reads winning promotion overlay.
- CommerceHub listing publish/price-override → CommerceHub listing owner; search
  reads `CommerceHubPriceAuthority`.
- Affiliate commission → Affiliate Platform; search only filters by `IsAffiliateEnabled`.
- Cross-tenant leakage prevention at the *source* → `ITenantContext` +
  `TenantLineageAuthority` (fail-closed on inactive tenant).

---

## Architecture Principles (implementation-cited)

P1. **SQL is the only authority.** `ProductSearchSqlAuthorityOverlay.ApplyCoreAsync`
recomposes Name/Variants/Stock/Media/Category/Promotion from PostgreSQL
(`ProductSearchSqlAuthorityOverlay.cs:114-343`); returns null (→ delete) when the
product or variant lineage is not active/visible
(`ProductSearchSqlAuthorityOverlay.cs:92, 129-143, 285-289`).

P2. **Tenant isolation is mandatory and fail-closed.** Every ES query filters
`TenantId` (`ProductSearchIndexService.cs:345`); overlay bails entirely if the tenant
is inactive (`ProductSearchSqlAuthorityOverlay.cs:92`, `TenantLineageAuthority.IsActiveTenantAsync`).

P3. **Search never mutates SQL.** Consumers are read-only projections: "Elasticsearch
is NOT the source of truth" (`ProductSearchIndexConsumer.cs:29-34`). The only writes
are ES index/delete + Mongo sync-flag updates.

P4. **Repair-on-read: SQL overlay re-applied before presentation.** `ProductSearchService`
re-overlays every Mongo candidate before mapping (`ProductSearchService.cs:183-195`);
`StorefrontSearchController.FilterSearchItemsBySqlAuthorityAsync` re-runs the overlay
for each unified-search hit (`StorefrontSearchController.cs:387-500`).

P5. **Events are triggers, not data sources.** Stock/price consumers read the *Mongo*
projection then re-rebuild from SQL; "Events are triggers, not data sources"
(`ProductStockPriceSearchIndexConsumer.cs:31-36`).

P6. **Stale-out-of-order events are rejected.** `ProductSearchIndexService.IndexAsync`
skips an index when the existing ES doc is newer
(`ProductSearchIndexService.cs:59-67`); consumers use `ProjectionEventFence` to
collapse duplicates/replays (`ProductSearchIndexConsumer.cs:99-122`).

P7. **Idempotency by composite document ID.** ES doc id = `{TenantId}_{ProductId}`
(`ProductSearchDocument.cs:303`); CommerceHub = `{TenantId:N}:{ListingId:N}` +
legacy `{ListingId:N}` (`CommerceHubProductSearchDocument.cs:32-34`).

P8. **Promotion ranking is additive, never multiplicative.** `ScoreMode.Sum` +
`BoostMode.Sum` preserve text relevance (`ProductSearchIndexService.cs:485-486`).

P9. **Affiliate leakage prevented by hard filter.** Only `IsAffiliateEnabled` is
indexed; commission rate is NEVER stored in ES
(`ProductSearchDocument.cs:280-297`).

P10. **CommerceHub reindex is zero-downtime via alias migration + SQL recomposition.**
`CommerceHubReindexService` builds a versioned target index, reconciles from Mongo
re-overlaid with SQL, deletes stale, then promotes the alias only when
`targetCount == publishedCount` (`CommerceHubReindexService.cs:57-151`).

P11. **Disaster recovery is SQL-first.** Admin reindex/re-sync recomposes visibility
from current SQL authority and deletes ES docs that no longer qualify
(`SearchReindexController.cs:149-182`, `AdminSearchSyncController.cs:67-176`).

P12. **Rebuild entrypoints must declare SQL authority explicitly.**
`SqlTruthRebuildGuard.RequireSqlAuthority` rejects empty tenant/entity/authority-kind
(`SqlTruthRebuildGuard.cs:27-73`).

---

## Catalog Search Runtime

**Owner:** Catalog (Product) lifecycle owner. **Read authority:** Search Runtime reads
PostgreSQL via the overlay; **projection authority:** Search Runtime writes ES + Mongo.
**Runtime truth:** SQL `Products` / `ProductVariants`.

Catalog discovery is gated by four conditions, all recomputed from SQL per
index/read:
- Tenant active (`ProductSearchSqlAuthorityOverlay.cs:92`).
- Product `!IsDeleted` (`ProductSearchSqlAuthorityOverlay.cs:126`).
- Product `IsActive && IsOnlineVisible && PublishStatus == Published`
  (`ProductSearchSqlAuthorityOverlay.cs:170-172`).
- Category lineage live (not deleted, no broken parent loop)
  (`ProductSearchSqlAuthorityOverlay.cs:134-143, 440-489`).
- At least one variant `IsActive && EffectivePrice > 0`
  (`ProductSearchSqlAuthorityOverlay.cs:285-289`).

Only when all hold does the product become searchable. A product that loses any
condition is deleted from ES by the next event/reindex
(`ProductSearchIndexConsumer.cs:146-182`).

### Product Discovery
ES `multi_match` over Name(boost 3), NameAr(3), SKU(2), CategoryName(1) with
`Fuzziness.Auto` (`ProductSearchIndexService.cs:379-389`). The `SearchText` power
field carries enriched name+nameAr+description+category+SKU+barcode+brand
(`ProductSearchDocument.cs:169-186`, `SearchEnrichment.cs:129-184`).

### Category Discovery
`categories` index, separate `CategorySearchDocument`. Sync is SQL-lineage gated
(`AdminSearchSyncController.cs:181-297`); only live-in-lineage categories indexed,
stale deleted (`AdminSearchSyncController.cs:210-246`).

### Brand Discovery
**No `Brand` entity exists** (free-text `Product.Brand` only — see product-catalog
reference). Brand is a `keyword` field for exact filter/aggregation and is expanded
cross-language at index time (`ProductSearchDocument.cs:242-247`,
`SearchEnrichment.cs:35-86`).

### Visibility / Publication / Availability
`IsActive`, `IsOnlineVisible`, `PublishStatus == Published` are the visibility gates
(`ProductSearchSqlAuthorityOverlay.cs:170-172`). Availability is a *display/ranking*
signal only (`IsInStock`, `StockQuantity`) and does **not** remove a product from
search unless stock is tracked and all variants are zero and backorder is off
(`ProductSearchDocumentMapper.cs:19-28`, `ProductSearchSqlAuthorityOverlay.cs:210`).

---

## Variant Search Runtime

**Variant authority:** `ProductVariant` (first-class sellable SKU aggregate). **Read
authority:** overlay recomposes variants from SQL; **projection authority:** Search
writes variant sub-documents into the product ES doc.

- Only `ProductVariantSemanticLifecycleState.Current` variants are projected
  (`ProductSearchSqlAuthorityOverlay.cs:232`, `ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner`).
- Variant visibility = `IsActive && EffectivePrice > 0`
  (`ProductSearchSqlAuthorityOverlay.cs:286`).
- SKU/Barcode live on the variant; the product-level SKU/Barcode is taken from the
  active default variant (`ProductSearchSqlAuthorityOverlay.cs:309-318`).
- Variant ranking/availability = per-variant `TotalStock`, `IsActive`
  (`ProductSearchDocumentMapper.cs:12-28`).

---

## SKU Runtime

**SKU resolution owner:** ProductVariant (SQL). **Search path:** SKU is a `keyword`
field (exact match only) and is included in `SearchText` for fuzzy/partial match
(`ProductSearchDocument.cs:77-82`, `ProductSearchEnrichment` → `SearchEnrichment.cs:171-175`).

- Exact match: `Term` filter on `sku` keyword.
- Partial match: SKU tokens inside `SearchText` matched via `multi_match` +
  `Fuzziness.Auto` (`ProductSearchIndexService.cs:379-389`).
- Normalization: SKU is **case-sensitive exact** for filtering
  (`ProductSearchDocument.cs:81` `[Keyword]`); fuzzy tolerance applies only through
  the text path, not the keyword filter.
- No evidence of SKU canonicalization/uppercasing before index — it is stored as
  supplied by the variant.

## Barcode Runtime

**Barcode resolution owner:** ProductVariant (SQL; cross-checked against
`ProductUnits.Barcode` via `ProductVariantCloneImportAuthority`). **Search path:**
Barcode is a `keyword` field and is added to `SearchText`
(`ProductSearchDocument.cs:84-89`, `SearchEnrichment.cs:177-180`).

- POS integration: barcode scan resolves a product via the same keyword/text path;
  no separate barcode index found — barcode is a field on the product ES doc.
- Storefront usage: barcode participates in `SearchText` fuzzy/partial match only.
- Uniqueness: SQL-enforced (`ProductVariantCloneImportAuthority`); ES is not the
  uniqueness authority.
- Fallback: if a barcode-collision or no-hit occurs, there is no special barcode
  fallback path — it degrades to a normal empty/low-relevance result.

---

## CommerceHub Search Runtime (Marketplace Discovery)

**Owner:** CommerceHub listing owner (SQL `CommerceHubListings`). **Read authority:**
`CommerceHubSearchReadService` (ES) re-composed via `CommerceHubSearchSqlAuthorityOverlay`
(SQL). **Projection authority:** Search Runtime (ES `commercehub_products` aliased +
Mongo `commercehubproducts`).

- **Read path:** `CommerceHubController` exposes `GET /api/commercehub/search`,
  `/suggestions`, `/autocomplete` (rate-limited, anonymous)
  (`CommerceHubController.cs:51,68,84`). `CommerceHubQueryService` sets
  `request.ExcludedTenantIds = LoadPubliclySuppressedVendorIdsAsync(...)` (governance-
  banned / order-suspended vendors) then calls `CommerceHubSearchReadService`
  (`CommerceHubQueryService.cs:121,177,234,249`); falls back to Mongo `_repository` when
  ES returns null (`CommerceHubQueryService.cs:250-258,346-354`).
- **ES returns IDs only** (`.Source(false)`); hydration + `IsPublished`/`TenantId`/
  `ExcludedTenantIds` filter happens in Mongo (`CommerceHubSearchReadService.cs:67-109,895-931`).
- **Tenant isolation:** `ExcludedTenantIds` applied as `must_not Terms(TenantId)`
  (`CommerceHubSearchReadService.cs:681-686,738-743,766-771`) — globally suppressed
  vendors, not the requester's tenant.
- **SQL authority overlay** rebuilds visibility from `CommerceHubListings + Products +
  ProductVariants + Tenants + CommerceHubCategories + ProductReviews + vendor/KYC
  governance`; rejects when listing not published, tenant inactive/wrong-currency,
  vendor not marketplace-eligible, product/variant/category not live
  (`CommerceHubSearchSqlAuthorityOverlay.cs:100-175`). ES is never source of truth.

### Auto-Healing Self-Repair Loop
`CommerceHubAutoHealingService` (a `BackgroundService`) keeps the ES index reconciled
with SQL without manual reindex:
- Bounded `Channel<QueuedListing>` (cap 1000), concurrency 5, dedupe TTL 3 min,
  retries (4) with backoff, **circuit breaker** (20 failures / 30s → open 45s)
  (`CommerceHubAutoHealingService.cs:51-80,192-208,234`).
- **Startup recovery:** scans Mongo for `SearchSyncStatus != Synced` and published-
  but-missing-in-ES, flags as `SearchSyncFailed`, enqueues up to 500
  (`EnqueueStartupRecoveryAsync`, `:217,470`).
- **Background reconciliation** every ~60s: scans 200 published Mongo docs, diffs
  against ES `GetExistingIdsAsync`, enqueues missing (`RunBackgroundReconciliationAsync`,
  `:234,298,432`).
- Each queued listing is re-indexed via `CommerceHubSearchSqlAuthorityOverlay`
  (SQL recompose) and `SearchSyncStatus` updated; cross-tenant guard enforced
  (`:583,655-700`). The health check exposes queue depth / circuit state
  (`CommerceHubSearchSyncHealthCheck.cs:60-80`).

## Autocomplete Runtime

**Owner:** Search Runtime (read from ES). **Authority:** SQL re-overlay on results.

- Storefront header search (`/api/storefront/search/suggest`) works from **1
  character** using `name.autocomplete` edge-ngram
  (`StorefrontSearchController.cs:271-292`); ES `HeaderSuggestAsync` uses
  `autocomplete_analyzer` + `multilingual_analyzer` search-analyzer
  (`ProductSearchIndexService.cs:240-264`).
- CommerceHub autocomplete (`GetAutocompleteAsync`) uses `productNameNormalized.keyword`
  (term + prefix), `MatchPhrasePrefix`, `MultiMatch` fuzzy, highlights, and aggregates
  category buckets + popular terms (`CommerceHubSearchReadService.cs:214-371`).
- Typo tolerance: `Fuzziness.Auto` on text path; prefix matching for partial words.
- After ES candidate fetch, results are re-overlaid with SQL authority
  (`StorefrontSearchController.cs:316-319`, `RecomposeHeaderSuggestionsBySqlAuthorityAsync`).

## Suggestion Runtime

- CommerceHub `GetSuggestionsAsync` returns product hits + `popular_terms`
  aggregation (`productName.keyword`) + `similar_terms` built from query expansions
  (`CommerceHubSearchReadService.cs:122-212`).
- Storefront suggestions = header search (above).
- Ranking: score → `InStock` → `CreatedAtUtc` (CommerceHub); score → `InStock` →
  `PopularityScore` (autocomplete) (`CommerceHubSearchReadService.cs:142-144, 243-247`).
- Suggestions never mutate ranking authority — they are a read-only presentation aid.

### CommerceHub Intent Detection & Query Normalization
`CommerceHubSearchIntentDetector` classifies the query into **Price / Category /
Product / Unknown** intents (`CommerceHubSearchIntentDetector.cs:77,85,100,109`):
price via price keywords + currency tokens, category via a DB-backed active-category
keyword map, digits as product signals. Barcode/SKU/Brand are **not** separate intents
— they are handled as attribute filters, not intents. Before hitting ES, queries are
normalized (`CommerceHubSearchQueryNormalizer.Normalize` — stop-word + non-alphanumeric
stripping) and filter keys/values normalized + range-suffix mapped
(`PointOfSale.API/CommerceHub/Services/CommerceHubSearchQueryParser.cs:78,115,225-255`, `CommerceHubAttributeService.NormalizeKey`).

### Search Intelligence (Analytics-Only)
`CommerceHubSearchIntelligenceService` tracks clicks/impressions/filter-events/queries
into `ICommerceHubSearchAnalyticsQueue` and provides `ExpandQueryAsync` (synonym
expansion from `CommerceHubSearchSynonyms`, cached 10 min). **It is analytics-only:**
expansions feed the caller's query broadening + the analytics record; no code path shows
clicks/impressions/synonyms mutating ES ranking weights or SQL visibility authority.
Ranking/visibility remain SQL-authority-derived
(`CommerceHubSearchIntelligenceService.cs:27,54,80,103,120,132-188`).

---

## Ranking Runtime

**Scoring owner:** Search Runtime (ES `function_score`). **Runtime truth source for
weights:** SQL-derived fields (promotion, stock, price, created date, popularity).

Storefront product ranking (`ProductSearchIndexService.SearchAsync`):
- Base relevance: multi_match boost Name/NameAr(3), SKU(2), Category(1)
  (`ProductSearchIndexService.cs:379-389`).
- Promotion-aware (when `PromotionRankingConfig.Enabled`): `function_score`
  with `ScoreMode.Sum`/`BoostMode.Sum`:
  - weight `HasActivePromotion` → `BaseBoost`
  - weight high-discount (`PromotionDiscountValue >= HighDiscountThreshold`) →
    `HighDiscountBoost`
  - `FieldValueFactor(SearchBoostOverride)` manual merchandising override
  - `GaussDate(PromotionStartDate)` time decay (optional)
  (`ProductSearchIndexService.cs:400-494`).
- `GetBestSellersAsync` is a **placeholder**: returns `CreatedAtUtc` desc, explicitly
  "In Phase 2.5, we don't have order/sales data indexed" (`ProductSearchIndexService.cs:560-617`). → Best-selling is NOT currently implemented by sales volume.

CommerceHub ranking (`CommerceHubSearchReadService.BuildQuery`):
- In-stock weight 2.25; `FieldValueFactor(PopularityScore, Log1P, 0.45)`;
  intent-price filter weight 3.5; `GaussDate(CreatedAtUtc, 30d)` weight 1.1; exact
  keyword match weight 8.0 (`CommerceHubSearchReadService.cs:373-437`).

Marketplace service ranking: ES narrows text candidates only; relevance score +
`GaussDate(CreatedAt, 90d)`; **sorting, filtering, trust, stats are recomposed from
SQL** (`MarketplaceSearchService.cs:337-403`).

### Boosting / Availability Weight / Popularity / Featured / Pinned / Business Rules
- Availability weight: in-stock boost (storefront via promotion path indirect;
  CommerceHub explicit 2.25).
- Popularity: `PopularityScore` field on CommerceHub doc (fed from Mongo, not SQL
  per-request) — see Debt D-? (popularity freshness).
- Featured: `IsFeatured` indexed but **no dedicated ranking boost** found in
  `ProductSearchIndexService` (only a display flag in `ProductSearchDocument`).
- Pinned results: no implementation evidence.
- Business rules: promo time-decay, manual `SearchBoostOverride`, intent-price
  weighting are the only business-rule boosts implemented.

---

## Filtering Runtime

Filters are ES `bool.filter` clauses (do not affect score).

Storefront product filters (`ProductSearchIndexService.SearchAsync`):
- Tenant (`ProductSearchIndexService.cs:345`)
- `IsActive == true` (`ProductSearchIndexService.cs:347`)
- `IsOnlineVisible == true` (`ProductSearchIndexService.cs:349`)
- Affiliate mode `IsAffiliateEnabled == true` (`ProductSearchIndexService.cs:353-356`)
- Category (`ProductSearchIndexService.cs:359-362`)
- Price range (`ProductSearchIndexService.cs:365-376`)

CommerceHub filters (`BuildFilterQueries`): tenant-excluded (`ExcludedTenantIds`),
category, price, in-stock, rating, intent (e.g. category intent boosts category
match) (`CommerceHubSearchReadService.cs:668-780`).

Documented-but-unverified-in-this-pass: brand filters, warehouse filters, attribute
filters, variant filters, dynamic filters — these are described in the task spec but
the implementation evidence found is limited to: Brand (keyword field, no explicit
filter clause in `ProductSearchIndexService` read path), and CommerceHub attribute
facets. **Flagged as partial evidence** — the storefront product read path implements
only category + price + affiliate filters; brand/attribute/warehouse/variant filtering
is not present in `ProductSearchIndexService.SearchAsync`.

---

## Faceting Runtime

- CommerceHub faceting: `BuildAggregations` produces `facet_categories` (max 100
  buckets), `facet_price` (stats), `category_ids`, `popular_terms`
  (`CommerceHubSearchReadService.cs:29-33, 76, 248-256, 865+`).
- Storefront product search: **no facet aggregation** in `ProductSearchIndexService.SearchAsync`
  (facets are not returned for the product index read path). Faceting is a
  CommerceHub-marketplace feature.
- Counts: ES terms/histogram aggregations; consistency is eventual (recomputed per
  query from current index).
- Dynamic facets: CommerceHub attribute facets are driven by
  `CommerceHubCategoryAttributes` schema, normalized at index time
  (`CommerceHubSearchSqlAuthorityOverlay.cs:232-308`).

---

## Availability Runtime

See P1/P5. Availability gates:
- **In Stock** (`IsInStock`): `!TrackStock || any variant TotalStock > 0 || AllowBackorder`
  (`ProductSearchSqlAuthorityOverlay.cs:210`, `CommerceHubSearchSqlAuthorityOverlay.cs:210`).
- **Out Of Stock**: product still searchable (availability is display/ranking, not a
  removal gate) unless stock tracked and all variants zero and no backorder.
- **Reserved**: `Quantity - ReservedQuantity` is the searchable stock
  (`ProductSearchSqlAuthorityOverlay.cs:346-358`).
- **Hidden** (`IsOnlineVisible == false`): removed from search
  (`ProductSearchSqlAuthorityOverlay.cs:171-172, 285-289`).
- **Deleted** (`IsDeleted`): removed (`ProductSearchSqlAuthorityOverlay.cs:126-132`).
- **Inactive** (`IsActive == false` or inactive tenant): removed
  (`ProductSearchSqlAuthorityOverlay.cs:92, 170`).
- **Publication** (`PublishStatus != Published`): removed
  (`ProductSearchSqlAuthorityOverlay.cs:171-172`).
- **Search gates** are always SQL-derived; ES `IsActive`/`IsOnlineVisible` flags are
  only the last-projected snapshot, re-validated by the overlay on read.

---

## Projection Runtime

### SQL Authority
PostgreSQL `Products`, `ProductVariants`, `ProductVariantStocks`, `ProductUnits`,
`Categories`, `Promotions`, `CommerceHubListings`, `CommerceHubCategories`,
`ProductReviews`, `Services`/`ServiceRequests`/`MarketplaceProfiles`.

### Mongo Projection
- `products` collection = storefront read-model; staging source for product ES docs
  (`ProductSearchIndexConsumer.cs:128`).
- `commercehubproducts` = CommerceHub read-model; staging source for CommerceHub ES
  docs; carries `SearchSyncStatus` (`Synced/Failed`) for health
  (`CommerceHubSearchSyncHealthCheck.cs:45-50`).
- Mongo docs carry `LastEventVersion`, `LastEventOccurredAtUtc`, per-concern stamps;
  consumers wait for Mongo freshness before indexing
  (`ProductStockPriceSearchIndexConsumer.cs:306-320`).

### Elasticsearch Projection
- `products`, `categories`, `cmspages` (storefront) + `commercehub_products` (aliased)
  + `marketplace_services`.
- ES docs are rebuilt from Mongo + SQL overlay; never authored from event payloads
  (`ProductUpdatedSearchIndexConsumer.cs:103-165`).

### Redis Usage
- `IPromotionRankingCacheService` / `IStoreConfigCacheService` cache promotion-ranking
  config + store config (`SearchReindexController.cs:240-267`).
- Redis holds **no search truth** — only ranking *config* and promotion badges.
- `AnalyticsHotStoreService` (Redis) feeds CommerceHub popularity signals
  (`CommerceHubProductSearchDocument.PopularityScore`).

### CQRS
- **Write model:** SQL (catalog/inventory/promotion/commercehub/service owners).
- **Read model:** Mongo (operational) + ES (search) + Redis (config cache).
- **Projection freshness:** event-driven, eventually consistent; repaired on read.
- **Consistency:** fail-closed on critical reads (storefront product search → 503);
  graceful degrade elsewhere.

---

## Indexing Runtime

### Initial / Incremental / Bulk / Delete / Update / Replay / Retry / Recovery
Driven by MassTransit consumers (each is a SQL-rebuild + idempotent ES write):

| Event | Consumer | Behaviour |
|---|---|---|
| ProductCreatedEvent | `ProductSearchIndexConsumer` | overlay → index or delete; `ProjectionNotReadyException` retry if Mongo not ready (`ProductSearchIndexConsumer.cs:128-160`) |
| ProductUpdatedEvent | `ProductUpdatedSearchIndexConsumer` | overlay → re-index from Mongo (`ProductUpdatedSearchIndexConsumer.cs:103-183`) |
| ProductStockChangedEvent / ProductVariantStockChangedEvent / ProductPriceChangedEvent | `ProductStockPriceSearchIndexConsumer` | wait for Mongo freshness, overlay, re-index (`ProductStockPriceSearchIndexConsumer.cs:73-405`) |
| ProductDeletedEvent / ProductArchivedEvent / ProductPublishStatusChangedEvent | `ProductDeletedSearchIndexConsumer` | overlay → index-if-visible else delete (`ProductDeletedSearchIndexConsumer.cs:43-192`) |
| Promotion*Event | `ProductPromotionSearchIndexConsumer` | reload winning SQL promotion, overlay, re-index (`ProductPromotionSearchIndexConsumer.cs:68-363`) |

- **Replay:** `ProjectionEventFence` collapses duplicate/active replays
  (`ProductSearchIndexConsumer.cs:99-122`).
- **Retry:** transient ES errors re-thrown for MassTransit retry; permanent/logical
  errors acknowledged (no retry storm) (`ProductSearchIndexConsumer.cs:222-268`).
- **Recovery:** admin reindex / sync endpoints (`SearchReindexController`,
  `AdminSearchSyncController`) + CommerceHub `CommerceHubReindexService`.

---

## Rebuild Runtime

- **Full reindex (tenant):** `SearchReindexController.ReindexProducts` loops all
  tenant products, overlays each from SQL, indexes or deletes
  (`SearchReindexController.cs:119-234`).
- **Partial reindex:** `AdminSearchSyncController.SyncProducts` (SQL-first,
  batch 250) (`AdminSearchSyncController.cs:67-176`).
- **Tenant reindex (CommerceHub):** `CommerceHubReindexService.ReindexTenantAsync`
  (`CommerceHubReindexService.cs:172-210`).
- **Full reindex (CommerceHub):** zero-downtime alias migration —
  `EnsureRebuildTargetAsync` → reconcile Mongo→SQL→ES into versioned target →
  `DeleteStaleSearchDocumentsAsync` → verify `targetCount == publishedCount` →
  `PromoteIndexAsync` (`CommerceHubReindexService.cs:57-151`).
- **Catch-up:** `CatchUpReindexAsync` post-finalization re-indexes published Mongo docs
  into active write index to close the alias-switch gap
  (`CommerceHubReindexService.cs:212-272`).
- **Failure behaviour:** if `Failed > 0` or counts mismatch, alias promotion is
  skipped and an error is logged (no partial promotion)
  (`CommerceHubReindexService.cs:119-148`).

---

## Repair-on-Read Runtime

- **Projection validation:** every storefront product result re-runs
  `ProductSearchSqlAuthorityOverlay.ApplyAsync` and drops anything not
  `{IsActive, IsOnlineVisible}` (`ProductSearchService.cs:183-195`).
- **Unified search:** `FilterSearchItemsBySqlAuthorityAsync` re-overlays each hit
  (`StorefrontSearchController.cs:387-500`).
- **CommerceHub:** `CommerceHubSearchReadService.HydrateDocumentsAsync` loads Mongo
  docs; the SQL overlay is applied at index/reindex time, and the read service falls
  back to Mongo on ES failure (null) — but Mongo itself is SQL-derived.
- **Missing/corrupted documents:** if Mongo projection is missing, the consumer throws
  `ProjectionNotReadyException` → retry (`ProductSearchIndexConsumer.cs:129-138`);
  if SQL no longer proves visibility, the ES doc is deleted
  (`ProductSearchIndexConsumer.cs:146-160`).
- **Rebuild trigger:** admin endpoints, CommerceHub reindex job, auto-healing queue.
- **Consistency:** read-path overlay guarantees returned results match current SQL
  authority even if ES is stale.

---

## Outbox

The search sync is **event-driven via MassTransit**, not the generic `OutboxMessage`
table directly. Catalog/promotion/commercehub events are published by their owners and
consumed by the search consumers listed above.

- **Projection events:** ProductCreated/Updated/Deleted/Archived/PublishStatusChanged,
  ProductStockChanged/ProductVariantStockChanged/ProductPriceChanged,
  PromotionCreated/Updated/Deleted/Expired.
- **Retry:** transient ES errors → MassTransit retry; `ProjectionNotReadyException`
  (Mongo not caught up) → retry (`ProductStockPriceSearchIndexConsumer.cs:389-392`).
- **Replay:** `ProjectionEventFence` idempotent collapse by
  (consumer, eventType, entityId) (`ProductSearchIndexConsumer.cs:99-122`).
- **Dead letter:** no explicit DLQ found in search consumers; permanent/logical ES
  errors are acknowledged (logged, not retried) to avoid retry storms
  (`ProductSearchIndexConsumer.cs:242-268`).
- **Ordering:** consumers reject stale updates by comparing `IndexedAtUtc`
  (`ProductSearchIndexService.cs:59-67`) and Mongo `LastEventVersion` vs event version
  (`ProductStockPriceSearchIndexConsumer.cs:306-320`).
- **Idempotency:** composite doc ID + fence + version checks.

---

## Read Models

- **Mongo `products`**: storefront operational read-model; hydrated into DTOs; carries
  promotion/stock/media/unit stamps.
- **Mongo `commercehubproducts`**: CommerceHub read-model; carries `SearchSyncStatus`.
- **ES `products`/`categories`/`cmspages`/`commercehub_products`/`marketplace_services`**:
  full-text/ranking/faceting engine.
- **Redis**: ranking config + promotion badges + analytics hot-store (popularity).

## Write Models

- **SQL** is the only write authority. Search Runtime writes only ES index/delete
  operations and Mongo sync-status flags; it never writes SQL catalog/pricing/
  inventory/commercehub rows.

---

## Runtime Authority Matrix

| Concept | Owner (mutation) | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|
| Search (ES) | Search Runtime | Search Runtime (ES) | Search Runtime | PostgreSQL |
| Catalog (Product) | Catalog owner | Overlay (SQL) | Search Runtime (ES/Mongo) | PostgreSQL |
| Variant | Catalog/Variant owner | Overlay (SQL) | Search Runtime | PostgreSQL |
| Category | Catalog owner | `CategoryLineageAuthority` | Search Runtime | PostgreSQL |
| Brand | Catalog owner (free-text) | Overlay (SQL) | Search Runtime | PostgreSQL |
| SKU | Variant owner | Overlay (SQL) | Search Runtime | PostgreSQL |
| Barcode | Variant owner | Overlay (SQL) | Search Runtime | PostgreSQL |
| Projection (ES) | — | Search Runtime | Search Runtime | PostgreSQL |
| Ranking (weights) | Search Runtime | ES function_score | Search Runtime | SQL-derived fields |
| Availability | Inventory Runtime | Overlay (SQL) | Search Runtime | PostgreSQL |
| Filtering | Search Runtime | ES filters | Search Runtime | PostgreSQL+ES |
| Faceting | Search Runtime | ES aggregations | Search Runtime | ES (eventual) |
| Suggestions | Search Runtime (+ analytics) | ES + SQL overlay | Search Runtime | PostgreSQL+ES |

## Runtime Authority Divergence

The only durable divergence is **eventual consistency**: ES/Mongo lag SQL by the
event-propagation + fence window. This is closed on every read by the SQL overlay
(repair-on-read) and on every write by the SQL overlay (write-time recomposition).
ES is **never** allowed to become source of truth — any path that would trust a stale
ES/Mongo flag re-validates against SQL.

---

## Cross-Domain Authority Graph

| Peer | Reads from Search | Search reads from Peer | Writes to Peer | Sync mechanism |
|---|---|---|---|---|
| Merchant Platform (POS Admin + Storefront) | search results | SQL Products/Variants/Categories; Mongo products | none (ES only) | events → consumers |
| Inventory Runtime | — | `ProductVariantStocks` (Qty−Reserved) | none | event + overlay |
| POS Runtime | barcode/SKU lookup | variant SQL | none | query path |
| CommerceHub | marketplace listings search | `CommerceHubListings`/`CommerceHubCategories`/`ProductReviews`/Tenant | none (ES only) | Mongo projection + reindex |
| CMS Runtime | published-page search | `CmsPages` (published) | none | admin sync |
| Reporting Runtime | — | none direct | none | n/a |
| Affiliate Platform | affiliate-mode filter | `IsAffiliateEnabled` (product) | none | field index |
| Analytics Runtime | popularity/click signals | `CommerceHubProductDocument.PopularityScore` (Mongo→ES) | search-impression/click events | `CommerceHubSearchIntelligenceService` → queue |
| SuperAdmin | — | none | none (reindex/replay via recovery runbooks) | recovery only |

**Dependencies:** Search depends on Catalog, Inventory, Promotion, CommerceHub
listing, CMS (read-only). No peer depends on Search for authority.

---

## Business Invariants (implementation-backed)

I1. Only published (`PublishStatus == Published`), active, online-visible, non-deleted
products are searchable. `ProductSearchSqlAuthorityOverlay.cs:126, 170-172`.

I2. Deleted/hidden/inactive/archived products disappear from search (tombstoned on
next event/reindex). `ProductDeletedSearchIndexConsumer.cs:43-192`,
`ProductSearchIndexConsumer.cs:146-160`.

I3. Inventory gates *display/ranking*, not *searchability*, unless stock is tracked and
all variants are zero and backorder is off. `ProductSearchSqlAuthorityOverlay.cs:210`,
`ProductSearchDocumentMapper.cs:19-28`.

I4. Projection never becomes authority. Every read/write re-overlays SQL.
`ProductSearchService.cs:183-195`, `StorefrontSearchController.cs:387-500`.

I5. Search never mutates SQL. Consumers are read-only projections.
`ProductSearchIndexConsumer.cs:29-34`.

I6. Ranking never changes authority. Boosts are additive weights over SQL-derived
fields; they cannot make an invisible product visible. `ProductSearchIndexService.cs:485-486`.

I7. Tenant isolation is non-bypassable and fail-closed. `ProductSearchIndexService.cs:345`,
`ProductSearchSqlAuthorityOverlay.cs:92`.

I8. Affiliate commission is never indexed; only `IsAffiliateEnabled` filter.
`ProductSearchDocument.cs:280-297`.

I9. Elasticsearch cannot become source of truth; rebuild is SQL-first.
`SearchReindexController.cs:149-182`, `CommerceHubReindexService.cs:365-389`.

I10. Out-of-order stale events are rejected (version/`IndexedAtUtc` guard).
`ProductSearchIndexService.cs:59-67`, `ProductStockPriceSearchIndexConsumer.cs:306-320`.

---

## Architectural Decisions (WHY)

AD1. **Why Elasticsearch exists.** Full-text multilingual relevance, fuzzy/typo
tolerance, faceting, and promotion-aware ranking at scale — impossible with pure SQL
`ILike`. Analyzers + index-time enrichment deliver "Shopify-grade" search without
translation services. `ProductSearchIndexService.cs:184-287`, `SearchEnrichment.cs:5-27`.

AD2. **Why SQL remains authority.** Catalog truth (price, stock, publication,
visibility) mutates in many owners; trusting a projection would let stale ES/Mongo
show hidden/deleted products or wrong prices. The overlay makes every path
self-correcting. `ProductSearchSqlAuthorityOverlay.cs:114-343`.

AD3. **Why Mongo is projected.** Mongo is the operational read-model and the *staging*
source so ES indexing is decoupled from SQL load and from event-ordering races;
consumers wait for Mongo freshness, then re-overlay SQL.
`ProductStockPriceSearchIndexConsumer.cs:295-326`.

AD4. **Why Repair-on-Read exists.** Eventual consistency means ES can lag; re-overlay
on read guarantees returned results match current SQL authority even mid-propagation.
`ProductSearchService.cs:183-195`.

AD5. **Why Outbox/event consumers are used.** Decouples write owners from the search
pipeline; allows replay, idempotent collapse, and graceful degradation. Events are
triggers, not data sources. `ProductStockPriceSearchIndexConsumer.cs:31-36`.

AD6. **Why Search is eventually consistent.** Chosen for scalability/availability;
correctness is preserved by repair-on-read + write-time overlay, so the lag window
never surfaces wrong authority. `StorefrontSearchController.cs:387-500`.

AD7. **Why CommerceHub uses alias-migration zero-downtime reindex.** Marketplace index
is large and schema evolves (rating/reviewCount fields); a versioned target + promote
avoids downtime and lets health-check gate promotion on count equality.
`CommerceHubReindexService.cs:57-151`.

AD8. **Why promotion ranking is additive (Sum/Sum).** Preserves text relevance as the
primary signal; promo boosts nudge, never dominate. `ProductSearchIndexService.cs:485-486`.

AD9. **Why tenant admin cannot delete/recreate the global index.** Avoids cross-tenant
blast radius; only tenant-scoped reindex or a reviewed platform recovery runbook.
`SearchReindexController.cs:62-97`.

---

## Technical Debt (implementation-backed)

TD1. **Misleading "fallback to MongoDB" comments in `ProductSearchIndexService`.** The
code logs "will fallback to MongoDB" (`ProductSearchIndexService.cs:517-552`) but
`ProductSearchService` actually throws `StorefrontSearchUnavailableException` (503) on
ES failure (`ProductSearchService.cs:82-94`). The comment describes an unimplemented
Mongo fallback for the product search path. **Priority: Medium.**

TD2. **Best-sellers not implemented by sales volume.** `GetBestSellersAsync` returns
`CreatedAtUtc` desc as a documented placeholder; no order/sales aggregation indexed.
`ProductSearchIndexService.cs:560-617`. **Priority: Medium** (claimed feature gap).

TD3. **No storefront product faceting.** `ProductSearchIndexService.SearchAsync` does
not return facet aggregations; faceting exists only for CommerceHub. Inconsistent
capability across surfaces. **Priority: Low.**

TD4. **Filtering surface gap.** Storefront product read path implements only
category + price + affiliate filters; brand/attribute/warehouse/variant/dynamic
filters described in spec are not present in the read path. **Priority: Low/Medium.**

TD5. **`ReindexAllAsync` is a no-op placeholder.** `ProductSearchIndexService.ReindexAllAsync`
logs "use SearchReindexController.ReindexProducts instead" and returns 0
(`ProductSearchIndexService.cs:700-722`). Indexing orchestration lives in the
controller, not the service — architectural smell. **Priority: Low.**

TD6. **CommerceHub popularity/click freshness.** `PopularityScore` is read from the
Mongo doc (fed by Redis analytics hot-store) at index time; it is not recomputed from
SQL per request, so popularity can drift between reindexes. **Priority: Medium.**

TD7. **Promotion delete/expire does a broad tenant-wide resync.** `SyncAllProductPromotionSearchFromSqlAsync`
re-overlays every active/visible product on delete/expire
(`ProductPromotionSearchIndexConsumer.cs:376-484`) — O(N) per promotion event;
acceptable (infrequent) but a scaling risk. **Priority: Low.**

TD8. **Duplicate index code paths.** Both `ProductSearchIndexService` and
`UnifiedSearchIndexService` implement product index/delete; `UnifiedSearchIndexService`
lacks the stale-`IndexedAtUtc` guard that `ProductSearchIndexService` has. Divergent
indexing logic. **Priority: Medium.**

TD9. **Legacy search paths coexist.** `UnifiedSearchIndexService` (unified storefront)
and `ProductSearchIndexService` (product search) both index `products`; the
unified path's promotion function_score uses hard-coded weights (5/10) vs the
config-driven `PromotionRankingConfig` in `ProductSearchIndexService`. **Priority: Medium.**

---

## Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| Remove Mongo-fallback comments | ES failure → 503 (StorefrontSearchUnavailableException) | Comments mislead maintainers | Delete stale comments or implement real Mongo fallback | Medium | Open |
| Implement true best-sellers | CreatedAt desc placeholder | Sales data not indexed | Index order/sales aggregates; sort by volume | Medium | Open |
| Storefront faceting | No facets on product index | CommerceHub-only | Add category/price/brand aggregations | Low | Open |
| Expand storefront filters | category+price+affiliate only | Spec wants brand/attr/warehouse/variant | Add filter clauses + normalized attributes | Medium | Open |
| Consolidate index services | ProductSearchIndexService + UnifiedSearchIndexService diverge | Dual code, divergent guards | Single index service with stale-guard | Medium | Open |
| CommerceHub popularity freshness | Mongo-snapshot at index time | Drift between reindexes | Recompute or shorter reindex cadence | Medium | Open |
| Promotion delete resync cost | Tenant-wide resync per event | O(N) scaling | Track affected products explicitly | Low | Open |

---

## Certification

- **Confidence:** High. Catalog/variant/SKU/barcode/autocomplete/suggestion/ranking/
  filtering/faceting/availability/projection/outbox/repair-on-read are direct code
  reads. CommerceHub marketplace search (read path, reindex, auto-healing, intent
  detection, analytics-only intelligence) corroborated by a second pass over
  `CommerceHubController`, `CommerceHubQueryService`, `CommerceHubReindexService`,
  `CommerceHubAutoHealingService`, `CommerceHubSearchIntentDetector`,
  `CommerceHubSearchQueryParser`, `CommerceHubSearchIntelligenceService`, and the
  CommerceHub certification tests.
- **Completeness:** Storefront product faceting/filtering expanded per spec but not
  fully present in code — flagged as partial (TD3/TD4). CommerceHub path is fully
  covered including the auto-healing self-repair loop and zero-downtime alias reindex.
- **Verified:** all citations traced to `file:line` in this session; every referenced
  file path confirmed to exist on disk.
- **Missing evidence (omitted, not fabricated):** pinned results, real-time
  best-seller by sales, brand/attribute/warehouse/variant storefront filters, a
  dedicated dead-letter queue for search consumers, cross-language runtime
  translation (intentionally absent by design). CommerceHub intent detector has
  Price/Category/Product/Unknown only — no dedicated barcode/SKU/brand intent (those
  are attribute filters).
- **Architecture debt summary:** TD1–TD9 (see Technical Debt).

---

## Search Query Lifecycle (Storefront Product Search)

```
User Search (q, X-Tenant-Id)
  ↓
Normalization: q.Trim().ToLowerInvariant()        ProductSearchService.cs:54 ; StorefrontSearchController.cs:101
  ↓
Query Parser / Validation: min 2 chars, tenant header  StorefrontSearchController.cs:84-131
  ↓
Elasticsearch (multi_match + filters + promotion function_score)  ProductSearchIndexService.SearchAsync:310-511
    Runtime Owner: Search Runtime (ES) | SQL Authority: none read here | Projection: ES
    Events: none | Synchronization: eventual | Replay: n/a | Failure: 503 (StorefrontSearchUnavailableException) ProductSearchService.cs:82-94
    Fallback: none (graceful 503, NOT Mongo)  ← corrects the misleading comment
    Consistency: eventual
  ↓
Candidate IDs returned
  ↓
Hydrate from Mongo (products by IDs)  ProductSearchService.HydrateFromMongoDbAsync:163-208
    Runtime Owner: Search Runtime | SQL Authority: none | Projection: Mongo
  ↓
Availability / Visibility Filter (REPAIR-ON-READ): SQL overlay per candidate  ProductSearchService.cs:183-195
    Runtime Owner: Search Runtime + SQL overlay | SQL Authority: Products/Variants/Categories
    Validation: IsActive && IsOnlineVisible  → drop otherwise
    Failure: silent drop (not error) | Consistency: SQL-authoritative
  ↓
Ranking (already scored in ES; DTO mapped)  ProductSearchService.MapToListItemDto:217-272
  ↓
Response (SearchResult<ProductListItemDto>)  ProductSearchService.cs:149-155
```

## Index Lifecycle (Product Updated → Search)

```
Merchant Updates Product (SQL commit)            Catalog owner
  ↓
Outbox / Event publish (ProductUpdatedEvent)     Catalog owner → MassTransit
  ↓
ProductUpdatedSearchIndexConsumer.Consume        ProductUpdatedSearchIndexConsumer.cs:51
    Mutation Authority: SQL (already committed) | Projection Authority: Search Runtime
    Retry: ProjectionEventFence collapse (idempotent)  :78-122
  ↓
Wait for Mongo projection freshness              :110-119 (ProjectionNotReadyException → retry)
  ↓
ProductSearchSqlAuthorityOverlay.ApplyAsync      :121-125  ← SQL re-composition
    Runtime Truth: SQL Products/Variants/Categories/Promotions
    Ordering: fence + version | Idempotency: composite id
  ↓
Decision: visible? → IndexAsync ; hidden/deleted? → DeleteAsync   :127-162
  ↓
ProductSearchIndexService.IndexAsync             ProductSearchIndexService.cs:39
    Stale guard: skip if existing IndexedAtUtc newer  :59-67
    Recovery: transient ES → MassTransit retry ; permanent → acknowledge  :222-268
  ↓
ES Document written (id = {TenantId}_{ProductId})
  ↓
Verification: health/analytics (CommerceHub uses ratio>=0.95 healthy) CommerceHubSearchSyncHealthCheck.cs:83
  ↓
Repair-on-read closes any lag on next query       ProductSearchService.cs:183-195
```

## Search Decision Matrix

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Evidence |
|---|---|---|---|---|---|---|
| Can hidden products appear? | No | Overlay | SQL | IsOnlineVisible | dropped | `ProductSearchSqlAuthorityOverlay.cs:171` |
| Can deleted products appear? | No | Overlay | SQL | IsDeleted | deleted | `:126-132` |
| Can inactive products appear? | No | Overlay | SQL | IsActive | dropped | `:170` |
| Can unpublished products appear? | No | Overlay | SQL | PublishStatus | dropped | `:171-172` |
| Can out-of-stock appear? | Yes (unless tracked & zero & no backorder) | Overlay | SQL | stock | shown | `:210, ProductSearchDocumentMapper.cs:19-28` |
| Can search bypass availability? | No (display/rank only) | Overlay | SQL | stock | n/a | `:210` |
| Can search bypass tenant isolation? | No | ES filter + Overlay | SQL/Tenant | TenantId | 503/empty | `ProductSearchIndexService.cs:345` |
| Can search read SQL directly? | Only via overlay (not raw catalog query for results) | Overlay | SQL | recompose | correct | `ProductSearchService.cs:183-195` |
| Can projection become authority? | No | Overlay | SQL | re-overlay | corrected | `StorefrontSearchController.cs:387-500` |
| Can rebuild skip validation? | No | Reindex | SQL | SqlTruthRebuildGuard | rejected | `SqlTruthRebuildGuard.cs:27-73` |
| Can repair mutate SQL? | No | Overlay (read-only) | SQL | n/a | never | `ProductSearchIndexConsumer.cs:29-34` |
| Can ES become source of truth? | No | Reindex | SQL | SQL-first | corrected | `CommerceHubReindexService.cs:365-389` |

## Search Quality Matrix

| Signal | Signal Source | Runtime Owner | Scoring Logic | Weight | Projection Usage | Authority |
|---|---|---|---|---|---|---|
| Name match | SQL Name/NameAr | Search Runtime | multi_match boost 3 | 3 | ES text | SQL-derived |
| SKU match | SQL Variant.SKU | Search Runtime | keyword + text | 2 | ES | SQL |
| Barcode match | SQL Variant.Barcode | Search Runtime | keyword + text | — | ES | SQL |
| Category match | SQL Category | Search Runtime | multi_match boost 1 | 1 | ES | SQL |
| Promotion active | SQL Promotion | Search Runtime | function_score weight | BaseBoost | ES | SQL-derived |
| High discount | SQL Promotion | Search Runtime | function_score weight | HighDiscountBoost | ES | SQL-derived |
| Manual boost | SQL Promotion.SearchBoostOverride | Search Runtime | FieldValueFactor | override value | ES | SQL-derived |
| Promotion time decay | SQL Promotion.StartDate | Search Runtime | GaussDate | decay | ES | SQL-derived |
| Popularity (CommerceHub) | Mongo/Redis analytics | Search Runtime | FieldValueFactor Log1P | 0.45 | ES | Mongo-derived (drift) |
| In-stock | SQL stock | Search Runtime | weight | 2.25 (CH) | ES | SQL-derived |
| Newest | SQL CreatedAtUtc | Search Runtime | GaussDate / desc | 1.1 / 30d | ES | SQL |
| Intent price | CommerceHub intent | Search Runtime | filter weight | 3.5 | ES | runtime |
| Featured | SQL IsFeatured | Search Runtime | **no ranking boost found** | 0 | display only | SQL |
| Best-selling | (NOT IMPLEMENTED) | — | placeholder CreatedAt desc | — | n/a | gap |

---

## Architectural Synthesis

The Search Runtime is the canonical expression of Shopixy's platform-wide patterns:

- **SQL Authority** — every surface re-derives truth from PostgreSQL
  (`ProductSearchSqlAuthorityOverlay`, `CommerceHubSearchSqlAuthorityOverlay`).
- **CQRS** — SQL write owners; Mongo + ES + Redis read projections.
- **Projection** — Mongo operational read-model stages ES documents; ES is the
  full-text/ranking engine.
- **Outbox / Event-driven** — MassTransit consumers project catalog/promotion/commercehub
  events into ES, idempotently (`ProjectionEventFence`).
- **Replay** — fence collapse + `IndexedAtUtc`/version stale-guard reject out-of-order.
- **Repair-on-Read** — SQL overlay re-applied per request closes the eventual-consistency
  window without surfacing wrong authority.
- **Eventual Consistency** — accepted for availability; correctness preserved by
  repair-on-read + write-time overlay.
- **Availability Gates** — inventory gates display/ranking, never searchability, unless
  the strict tracked-and-zero condition holds.
- **Cross-Domain Isolation** — tenant filter on every query + `TenantLineageAuthority`
  fail-closed; CommerceHub additionally excludes suspended vendors.
- **Runtime Boundaries** — Search owns ES/Mongo/Redis projections and ranking *functions*,
  but never catalog/pricing/inventory/commercehub *authority*.

These patterns are shared verbatim with the Merchant Platform, Inventory Runtime,
POS Runtime, CommerceHub, CMS Runtime, Affiliate Platform, and SuperAdmin recovery
runbooks: SQL authority, CQRS, outbox, replay, repair-on-read, availability gates, and
cross-domain isolation are the same contract everywhere. The Search Runtime's
contribution is the **relevance/ranking/faceting layer on top of that contract**, plus
the CommerceHub zero-downtime alias-migration reindex and auto-healing self-repair
loop that keep a large marketplace index consistent with SQL without downtime.

The architecture is coherent: search is a *derived, self-correcting projection* of SQL
truth, optimized for multilingual discovery, and deliberately never trusted as a source
of authority.
