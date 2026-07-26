# PASS 15.2 Marketplace Operations Certification

## Operations Inventory

- Super Admin operations dashboard: SQL listing counts, Mongo projection counts, Elasticsearch index status, search verification, auto-healing queue/circuit-breaker metrics, pending/failed search sync counts.
- Reindex tools: global CommerceHub reindex, tenant-scoped reindex, post-finalization catch-up reindex.
- Repair tools: listing verification enqueue and tenant verification enqueue through CommerceHub auto-healing.
- Recovery workflows: startup recovery, background reconciliation, false-sync recovery, search delete recovery, retry/circuit-breaker handling.
- Sync surfaces: CommerceHub Mongo projection consumer, search sync status fields, Elasticsearch SQL overlay, stale search deletion.
- Health and diagnostics: `CommerceHubSearchSyncHealthCheck`, CommerceHub platform health checks, search migration verification, auto-healing metrics.
- Operational dashboards: Super Admin Marketplace Operations page and existing governance page.

## Certification Findings

- Reindex remains SQL-authoritative. `CommerceHubReindexService` applies `CommerceHubSearchSqlAuthorityOverlay`, rebuilds Mongo-missing listing candidates from SQL identity only, and deletes stale Elasticsearch documents absent from current SQL-visible authority.
- Recovery converges toward SQL truth. Startup and background auto-healing use Mongo/search state only as candidate discovery, then reindex or delete through the SQL search overlay.
- Replay remains trigger-only. Projection consumers and reindex paths use event/document state as triggers and recompute current SQL listing/product/variant/category/tenant/vendor authority before derivative mutation.
- Auto-healing remains derivative. The queue cannot create listing authority; it indexes only when SQL overlay returns a visible document and deletes otherwise.
- Search sync remains derivative. Elasticsearch counts, aliases, mappings, smoke tests, and stale document deletion are diagnostics or derivative repair outcomes, not visibility authority.
- Projection sync remains derivative. Mongo search sync status is an operations watermark and retry state only.
- Diagnostics are trustworthy for the reviewed surfaces because the Super Admin dashboard separates SQL counts, Mongo counts, search counts, and auto-healing counters instead of presenting one derivative count as truth.
- Dashboard coverage is implemented for core search/recovery operations. It does not replace deployment-specific alert rules or full SQL-vs-derivative parity dashboards.
- Repair tooling is implemented for listing and tenant verification; both enqueue auto-healing with tenant scope and converge through SQL overlay.
- Audit coverage is implemented for dashboard views, global/tenant/catch-up reindex, and manual verification actions.
- Frontend/backend alignment is implemented through `MarketplaceOperationsPage.tsx`, `marketplaceOperationsService.ts`, route registration, and Super Admin navigation.
- Performance controls remain bounded by existing reindex options, auto-healing queue limits, batch sizes, throttling delays, retry limits, and circuit breaker behavior.
- Chaos behavior fails closed on reviewed paths: deleted, suppressed, orphaned, inactive, suspended, invalid category, superseded variant, or missing tenant authority collapses to delete/no-index rather than resurrection.
- Monitoring coverage exists through health checks, metrics snapshots, search verification, and dashboard counters; external alert packs remain deployment debt.

## Runtime Hardening Performed

- Added `SuperAdminMarketplaceOperationsController` with SQL/Mongo/Elasticsearch/auto-healing diagnostics.
- Added Super Admin global reindex, tenant reindex, catch-up reindex, listing verification, and tenant verification endpoints.
- Added reason validation and Super Admin audit records for marketplace operations.
- Added `MarketplaceOperationsPage.tsx` and service bindings.
- Added navigation and route registration for `/super-admin/marketplace/operations`.

## Regression Freeze Added

- `MarketplaceOperationsConsoleCertificationTests` freezes the operations controller, SQL-authoritative reindex routing, tenant-scoped verification, frontend/backend alignment, and architecture documentation updates.

## Explicit Answers

1. Can Super Admin operate marketplace recovery safely? Yes, for reviewed CommerceHub search/projection recovery operations.
2. Can reindex resurrect deleted listings? No. Reindex routes through current SQL overlay and stale search deletion.
3. Can replay resurrect deleted listings? No on reviewed CommerceHub projection/search paths; replay is a trigger and current SQL authority wins.
4. Can recovery resurrect deleted listings? No. Auto-healing deletes when SQL authority is absent.
5. Can auto-healing become authority? No. It has no listing write authority and uses SQL overlay before derivative mutation.
6. Are diagnostics trustworthy? Yes for the reviewed dashboard surfaces; they distinguish SQL, Mongo, Elasticsearch, and queue state.
7. Are operational dashboards trustworthy? Yes for core operations; full deployment alert dashboards remain debt.
8. Is tenant isolation preserved? Yes. Tenant operations validate tenant existence and enqueue tenant-scoped recovery; listing operations use the listing tenant.
9. Are any operations unsafe? No unsafe reviewed operations remain; global reindex is powerful and audited/rate-limited.
10. Is marketplace operations console production-ready? Yes for core marketplace search/projection recovery operations. Remaining debt is operational maturity, not authority safety.

## Remaining Debt

- Dedicated SQL-vs-Mongo-vs-Elasticsearch parity drill reports and alert rules.
- Immutable operator notification inbox for long-running operations.
- Per-operation progress history beyond audit and service logs.
- Production query-plan/index review for high-volume diagnostic counts.
- Broader dashboard coverage for non-search CommerceHub finance, payout, refund, and review recovery surfaces.
