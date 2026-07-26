# POS-ADMIN-CERTIFICATION-01 PASS 10.3 Mongo Projection Convergence Certification

Date: 2026-05-29

## Scope

This pass certifies MongoDB projection convergence after PASS 9 established SQL-authoritative public visibility, PASS 10.1 certified event propagation and projection freshness, and PASS 10.2 certified Redis cache freshness.

MongoDB remains a derivative read model. It may lag. It may not become authority for publication, visibility, lineage, media, pricing, inventory, CMS publication, homepage eligibility, affiliate discovery, CommerceHub listing visibility, or financial truth.

## Projection Inventory Findings

Reviewed Mongo projections include:

- Storefront catalog: `ProductDocument`, `CategoryDocument`, `ProductVariantDocument`, product media, coupons, promotions, shipping methods, payment methods, storefront config, navigation, footer, and CMS pages.
- Storefront operational reads: orders, online customers, sessions, wishlists, abandoned checkouts, attribution context, order attribution, and processed-event inbox documents.
- CommerceHub and marketplace: `CommerceHubProductDocument`, `CommerceHubOrderDocument`, marketplace product/order/homepage read models, marketplace review documents, review summaries, and provider trust snapshots.
- Affiliate: affiliate dashboard/order/earning documents, public affiliate store/product documents, affiliate product intelligence, affiliate analytics summaries/daily documents, and affiliate projection receipts.
- Analytics and support projections: analytics event documents, WhatsApp message logs, and cold/diagnostic projection documents.

No new Mongo projection type was introduced. Several projections are intentionally soft-state or analytics-oriented and are not public visibility authority.

## Projection Consumer Findings

Product create/update/lifecycle consumers route storefront `ProductDocument` mutation through `IProductSqlAuthorityProjectionWriter`, which recomposes current SQL product/category/media/variant/unit/stock/publication authority before Mongo upsert or tombstone.

CMS, footer, config, category, coupon, shipping, marketplace homepage, CommerceHub, affiliate intelligence, and search-adjacent projection consumers use combinations of SQL reload, tenant lineage checks, `ProjectionEventFence`/`ProjectionConsumerEventFence`, freshness filters, processed-event ids, or SQL-derived tombstone/delete behavior. Consumer downtime remains recoverable through MassTransit/outbox retry, SQL leases, and health/readiness lag diagnostics from PASS 10.1.

## Projection Freshness Findings

Freshness is measurable on reviewed Mongo projections through document-level metadata:

- `ProductDocument`: product, media, and stock event ids/timestamps/versions plus `SyncedAtUtc`.
- `StorefrontPageDocument`: last event id, event timestamp, event version, `UpdatedAt`, and published timestamp.
- `CommerceHubProductDocument`: per-domain listing/product/lifecycle/store/currency/shipping/price/stock/search timestamps and versions.
- Marketplace product/order/homepage read models: event timestamp/version, entity mutation version where applicable, and projection update timestamps.
- Affiliate product intelligence: processed event ids plus separate product, stock, policy, and store-profile freshness markers.
- Category and operational projections: synced/update timestamps plus tenant-scoped document keys.

Projection freshness blind spots remain on lower-tier analytics/support documents where strong public-visibility convergence is not claimed.

## Product Projection Findings

Storefront product projections converge from SQL via `ProductSearchSqlAuthorityOverlay` and `IProductSqlAuthorityProjectionWriter`. Missing, inactive, unpublished, offline, invalid-lineage, or non-purchasable SQL authority converges to tombstone rather than product resurrection.

Product media convergence is SQL-derived through current `ProductMedia` and variant `MediaId` authority. Product stock and variant availability convergence use SQL stock-owner and variant-lineage authority rather than raw Mongo stock totals.

## Category/CMS Findings

Category projection repair derives from `CategoryLineageAuthority` and deletes tenant-scoped stale Mongo category documents absent from current SQL authority. Public category reads remain SQL lineage-filtered.

CMS projection consumers reload current SQL published/non-draft/non-deleted page authority and active tenant lineage before Mongo replacement. Missing authority fails closed by fresh tombstone/update instead of stale page restoration. CMS search repair deletes stale Elasticsearch documents, and public reads remain SQL publication-filtered.

## Homepage/Discovery Findings

Marketplace homepage projections rebuild from current SQL homepage sections and sanitize curated product/category references through current SQL CommerceHub listing/category/vendor authority before Mongo persistence and again before public read return.

Affiliate product intelligence uses Mongo/storefront data only as candidate or display context and recomposes product visibility, pricing, stock, media, tenant currency, policy, and storefront URL through SQL-backed authority before upsert. Public affiliate discovery from PASS 9.4 still revalidates returned items through SQL.

## Rebuild Findings

Reviewed rebuild paths are SQL-derived:

- Product read-model rebuild uses `IProductSqlAuthorityProjectionWriter`.
- Category and shipping sync declare SQL rebuild authority and delete stale tenant-scoped Mongo documents.
- CMS/category/product search repair uses SQL authority and stale-delete behavior.
- CommerceHub reindex and marketplace read-model rebuild use SQL listing/product/category/vendor/media/stock authority before write or delete.

Additive-only rebuild was not found on the reviewed public-visibility Mongo boundaries.

## Repair Findings

Repair tooling remains tenant-scoped and SQL-first on reviewed public visibility surfaces. Repair may discover candidates from Mongo/search state, but final mutation must pass through current SQL recomposition, stale-delete, tombstone, or no-op.

Discount repair, admin product repair, category/CMS repair, CommerceHub reindex, and affiliate repair/reindex preserve the rule that SQL wins when Mongo and SQL disagree.

## Drift Detection Findings

Mongo drift is observable indirectly and operationally through:

- projection and MassTransit outbox readiness checks;
- `OperationalConvergenceTelemetry` lag/drift diagnostics;
- document freshness markers and processed-event receipts;
- CommerceHub search sync status/retry timestamps;
- stale-delete repair and reindex count verification on reviewed paths;
- Mongo debug/admin diagnostics for category and product projection counts.

There is no universal row-by-row Mongo-versus-SQL drift scanner. Correctness on reviewed public surfaces is provided by SQL-filtered reads plus SQL-derived repair/rebuild, not by strong parity.

## Cross-System Convergence Findings

Certified convergence chain:

```text
PostgreSQL
  -> MassTransit / SQL outboxes
  -> MongoDB projections
  -> Redis invalidation or SQL-filtered cache population
  -> Elasticsearch/search repair where applicable
```

MongoDB is a canonical read model in the derivative chain, but not a truth source. Redis and Elasticsearch may use Mongo as candidate evidence only where public reads or repair paths still revalidate current SQL authority.

## Startup/Recovery Findings

Startup Mongo index creation is idempotent and safe to retry. Startup and background recovery paths such as CommerceHub auto-healing use derivative Mongo/search state only to find candidates, then reprocess through SQL authority before public search mutation.

Mongo loss or corruption is recoverable by SQL-derived rebuild/repair for reviewed storefront, CMS/category, CommerceHub, homepage, and affiliate visibility boundaries.

## Chaos Findings

Reviewed chaos expectations:

- Consumer down: SQL outbox/backlog remains pending and lag is observable.
- Projection lag: public reads revalidate SQL or omit stale candidates.
- Duplicate event: fences, processed-event ids, duplicate-key handling, or freshness filters collapse side effects.
- Out-of-order event: event/version/timestamp/entity-mutation filters reject stale overwrites.
- Mongo rebuild: SQL-derived rebuild upserts current authority and deletes/tombstones missing authority.
- Projection repair: SQL-derived repair deletes stale state instead of promoting Mongo truth.

## Runtime Hardening Performed

This pass adds Mongo-specific regression-freeze coverage. It does not add new MongoDB features or change the runtime authority model.

The tests freeze the reviewed Mongo projection inventory, freshness markers, SQL writer usage, SQL-derived rebuild/repair, stale-delete/tombstone behavior, drift/lag telemetry, and documentation/governance requirements.

## Monitoring Improvements

No new meter was added. PASS 10.3 certifies that existing projection/outbox readiness telemetry, operational convergence diagnostics, projection document freshness metadata, CommerceHub search sync metadata, and repair/delete diagnostics are the reviewed monitoring boundary.

## Governance Updates

Mongo convergence truth and freeze rules are recorded in `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, and `.ai-context/CODEX_RULES.md`.

Future Mongo projection, rebuild, repair, startup recovery, cache warmer, feed/export, support, analytics, or global-script paths that touch public visibility must recompose current SQL authority, include freshness markers, collapse duplicate or stale replay, delete/tombstone missing authority, expose bounded lag/drift diagnostics, and add path-specific tests plus documentation.

## Honest Remaining Debt

Mongo projections remain eventually consistent, not strongly consistent.

There is no universal Mongo drift scanner, no adaptive per-tenant lag SLO, no centralized projection chaos framework, and no repo-wide migration of every older projection to a single shared freshness helper. Analytics, support, feed/export, CDN purge, global script, and lower-tier reporting projections still require path-specific certification before they can be treated as public-visibility convergence infrastructure.
