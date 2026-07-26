# POS-ADMIN-CERTIFICATION-01 PASS 9.6 Storefront Recovery, Cache & Projection Rebuild Certification

Date: 2026-05-29

## Scope

This pass audited storefront and CommerceHub recovery paths that mutate derivative state: product/category/CMS/search repair, Mongo projection rebuild, Elasticsearch reindex, Redis invalidation and cache refresh, startup auto-healing, replay consumers, deletion cleanup, and CommerceHub search/listing recovery.

## Projection Rebuild Certification

- Storefront product read-model rebuild remains SQL-authoritative through `IProductSqlAuthorityProjectionWriter` and `ProductSearchSqlAuthorityOverlay`. Deleted, unpublished, inactive, offline, invalid-lineage, or non-purchasable SQL products converge to tombstone/delete rather than full-document resurrection.
- Tenant-admin category, shipping-method, and CMS sync paths are stale-delete capable. They declare SQL rebuild authority and remove tenant-scoped Mongo/search documents absent from current SQL authority.
- Promotion repair mutates only promotion overlay fields on product/category projections and refreshes product search through SQL overlay before indexing.

## Search Rebuild Certification

- Storefront product search repair and reindex derive documents from current SQL product/category/media/unit/stock/publication authority before indexing. Missing SQL authority deletes tenant-scoped search visibility.
- CommerceHub reindex and catch-up reindex use `CommerceHubSearchSqlAuthorityOverlay` for Mongo candidates and SQL listing seeds. Alias promotion remains gated on SQL-visible document count verification.
- CMS and category search repair delete stale tenant-scoped search documents missing from current SQL publication or category-lineage authority.

## Cache Rehydration Certification

- Reviewed cache recovery is invalidation-first. Redis product, CMS, homepage, footer, marketplace product/order, and marketplace review caches are derivative infrastructure and do not restore visibility.
- Redis invalidation replay is fenced before key or pattern fanout, preventing duplicate replay from amplifying cache scans.
- Product promotion repair invalidates tenant-scoped product caches after SQL-overlay recomposition rather than treating cache contents as rebuild input.

## Startup Bootstrap Certification

- CommerceHub startup auto-healing scans derivative Mongo/search sync evidence only to discover candidates. Each queued listing is reprocessed through current SQL listing/product/category/vendor authority before Elasticsearch mutation.
- This pass tightened CommerceHub auto-healing queue identity so startup and background recovery group documents by `{ TenantId, ListingId }`, fetch tenant-scoped Mongo candidates when tenant scope is known, and deduplicate/process by tenant-scoped recovery identity.

## Replay Certification

- Product, category, CMS, footer, promotion, CommerceHub listing, search, and Redis invalidation replay paths reviewed in the previous passes remain trigger-only. They revalidate SQL authority or delete/tombstone when SQL no longer accepts visibility.
- Existing replay fences and outbox leases remain the duplicate and active-overlap controls; replay payloads do not own public visibility.

## Repair Tool Certification

- Tenant-admin search and data sync tools remain tenant-scoped and SQL-first.
- Global index deletion/recreation remains blocked from tenant-admin scope.
- Discount repair, product read-model repair, CMS search repair, category search repair, and CommerceHub reindex all include stale-delete behavior instead of additive-only rebuild.

## Deletion Convergence Certification

- Deleted or unpublished SQL products, CMS pages, categories, and CommerceHub listings converge by search deletion, projection tombstone/delete, cache invalidation, or public read suppression.
- No reviewed recovery path is allowed to recreate public visibility from Mongo, Elasticsearch, Redis, replay payloads, or repair snapshots after SQL no longer proves eligibility.

## CommerceHub Recovery Certification

- CommerceHub listing recovery remains SQL-first through `CommerceHubSearchSqlAuthorityOverlay`, vendor suppression, category lineage checks, SQL media/review recomposition, and SQL-visible count verification.
- Startup and background auto-healing now keep tenant identity in the recovery key, closing a queue-level tenant-isolation weakness where listing id alone was used for dedupe and batch document lookup.

## Tenant Isolation Certification

- Reviewed tenant-admin repair endpoints consume trusted tenant context and mutate tenant-scoped derivative documents.
- CommerceHub auto-healing now uses `{ TenantId, ListingId }` as recovery identity where tenant scope is known, and tenant-scoped enqueues are used for startup/background Mongo candidates.
- Unscoped recovery remains only a fallback for callers that genuinely lack tenant evidence; index mutation still fails through SQL overlay or tenant-scoped delete when authority is absent.

## Recovery Resurrection Chaos Certification

Simulated sequence:

Product published -> indexed -> cached -> projected -> SQL deleted/unpublished -> projection deleted -> cache cleared -> rebuild -> repair -> reindex -> replay -> startup bootstrap.

Expected and certified outcome on reviewed paths:

- Product read-model rebuild routes through SQL authority and tombstones/deletes.
- Search repair/reindex deletes missing SQL authority.
- Redis cache recovery cannot restore visibility because caches are invalidated or repopulated only after SQL-authoritative reads.
- CommerceHub recovery indexes only if current SQL listing/product/vendor/category authority still accepts the listing.
- Replay is trigger-only and cannot restore public visibility from historical payload state.

## Runtime Hardening

- Hardened `CommerceHubAutoHealingService` tenant recovery identity from listing-id-only to `{ TenantId, ListingId }` for in-flight dedupe, active processing, startup/background recovery grouping, and batch Mongo document resolution.
- Added source-freeze coverage in `StorefrontRecoveryRebuildCertificationTests`.

## Governance Updates

- Added PASS 9.6 architecture truth and CODEX freeze rules.
- Recorded the narrowed risk in `.ai-context/KNOWN_RISKS.md`.
- This pass certifies reviewed recovery/rebuild boundaries, not every future feed/export/cache-warmer/support/global-script path.

## Remaining Debt

- No universal recovery framework was introduced.
- Product/feed/export, CDN purge, global scripts, support tooling, analytics/reporting indexes, marketplace CMS, localization rebuilds, and future cache warmers still require path-specific SQL authority certification before they can mutate public visibility.
- Some older projection paths retain local SQL-authority equivalents instead of shared helper adoption; future changes should prefer shared lineage/overlay/fence primitives and add focused chaos tests.
