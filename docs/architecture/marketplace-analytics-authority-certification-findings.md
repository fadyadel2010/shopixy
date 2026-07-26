# PASS M13 Marketplace Analytics Authority Certification Findings

Date: 2026-06-11

## Verdict

Marketplace Analytics Authority is production-certifiable for the inspected runtime after analytics ingress and merchant conversion-window hardening. Analytics owns calculations, aggregations, dashboards, reporting, exports, trending calculations, and popularity calculations. It consumes SQL business truth and does not own revenue, order, payment, refund, catalog, promotion, attribution, customer, vendor, settlement, payout, or ledger truth.

## Findings

- Revenue findings: GMV, marketplace revenue, vendor revenue, platform commission, refunds, promo costs, payouts, balances, trends, vendor finance dashboards, and CSV finance exports derive from PostgreSQL `CommerceHubFinancialTransactions`, SQL sub-order/order/refund lineage, wallet/ledger services, and freshness-checked daily aggregates. Stale aggregate rows fail back to live ledger queries.
- Conversion findings: merchant listing conversions now derive from tenant-owned SQL `CommerceHubSubOrderItems` joined through non-deleted SQL sub-orders and marketplace orders inside the requested range. Clicks and impressions are behavioral signals only, revalidated against current SQL-visible listing authority before persistence.
- Attribution findings: analytics can group by query, campaign, promo, or affiliate labels, but canonical attribution remains owned by M10 promotions and P3-AA affiliate attribution SQL lineage. Analytics does not create or override attribution.
- Vendor findings: vendor dashboards and vendor exports use trusted tenant context or Super Admin-selected vendor IDs and SQL ledger rows. Vendor listing analytics filters by `StoreId == tenantId` and product lineage from tenant listings.
- Customer findings: inspected marketplace analytics paths do not expose customer PII analytics. Customer/order access remains M8/M2 SQL ownership authority; analytics telemetry session IDs are behavioral evidence only.
- Trending findings: popularity and trending signals can influence ordering only after current SQL-visible catalog authority is proven through `CommerceHubSearchSqlAuthorityOverlay`. Popularity replay cannot resurrect hidden listings because queue-time and reindex-time SQL overlays delete or skip rejected candidates.
- Reporting findings: finance statements and CSV exports derive from SQL ledger queries with vendor scope and date filters. No inspected export bypasses SQL authority or reads Mongo/Redis as final reporting truth.
- Projection findings: MongoDB product popularity fields, Elasticsearch ranking documents, Redis/memory caches, and daily aggregate rows are derivative. Daily aggregates are accepted only when complete, fresh, valid, pending-job-free, and currency-normalized; otherwise live SQL ledger queries are used.
- Isolation findings: vendor analytics binds tenant scope from authenticated context; Super Admin finance surfaces select vendors through SQL tenant/vendor rows; public search telemetry stamps SQL-visible listing tenant ownership instead of trusting DTO tenant IDs.
- Concurrency findings: duplicate client telemetry may append behavioral rows, but it cannot create order, revenue, attribution, catalog, or customer truth. Current SQL visibility is rechecked at controller and queue flush boundaries before click/impression persistence or popularity mutation.
- Historical findings: revenue and GMV history is recomputed from immutable SQL financial transactions and order/refund lineage as-of the requested range. Conversion history is bounded by SQL order creation time. Aggregate rebuilds converge from current SQL, not from stale projections.
- Adversarial findings: revenue inflation, revenue suppression, attribution poisoning, export abuse, tenant guessing, DTO tampering, route tampering, stale aggregate authority, trending resurrection, popularity replay, and vendor analytics leakage are blocked or fail closed on inspected paths by SQL ledger/order/catalog overlays, trusted tenant context, date bounds, and derivative-only projections.

## Runtime Hardening Performed

- Changed public CommerceHub impression ingestion to group accepted impressions by SQL-visible listing tenant and enqueue that SQL tenant instead of DTO/null tenant authority.
- Changed search analytics background persistence to re-prove current SQL-visible listing authority for clicks and impressions before inserting SQL telemetry or mutating popularity projections.
- Changed merchant listing conversion analytics to require tenant-owned, non-deleted SQL order/sub-order lineage inside the requested time window.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/CommerceHubController.cs`
- `PointOfSale.API/Controllers/AdminCommerceHubController.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/MarketplaceAnalyticsAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns analytics authority? Analytics owns derivative calculations, aggregations, dashboards, reports, exports, popularity, and trending signals; SQL business authorities own the source truth.
- Can analytics inflate revenue? No. Revenue/GMV surfaces consume SQL ledger/order/refund truth or freshness-checked SQL-built aggregates.
- Can analytics create attribution? No. Attribution remains SQL lineage authority from P3-AA/M10; analytics labels are grouping evidence only.
- Can trending override catalog authority? No. Trending and popularity can order only SQL-visible catalog results.
- Can exports bypass SQL authority? No inspected export bypasses SQL ledger/vendor scope authority.
- Can projections become authority? No. Projections, popularity fields, caches, and aggregates are derivative and must be revalidated or rebuilt from SQL.
- Can tenant leakage occur? No inspected marketplace analytics path permits tenant leakage after hardening.
- Were runtime code changes required? Yes.
- Is Marketplace Analytics Authority production-certifiable? Yes, for the inspected runtime.

## Residual Risk

Traffic telemetry remains approximate and client-influenceable by nature, so it must not be interpreted as financial, attribution, order, catalog, or customer truth. Production still needs analytics parity dashboards, bot/anomaly monitoring, export audit retention, high-volume replay chaos evidence, and query-plan review for large analytics windows.
