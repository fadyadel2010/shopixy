# POS-ADMIN-CERTIFICATION-01 PASS 11.4 Global Search, Reindex & Recovery Certification

Date: 2026-05-30

## Scope

This pass certifies global and tenant operational authority for search, reindex, repair, replay, recovery, auto-healing, startup recovery, cache/projection rebuild, and operational diagnostics after PASS 11.1, PASS 11.2, and PASS 11.3.

Operational tooling is convergence tooling only. PostgreSQL remains authority for publication, visibility, governance, tenant lineage, product/category/media/variant/unit/stock/pricing, CommerceHub listing/vendor/category authority, and service-marketplace visibility.

## 1. Operational Inventory Findings

- Global reindex: `CommerceHubReindexService.ReindexAsync`, Hangfire `commercehub-search-reconciliation`, and service-marketplace `ElasticsearchDriftReconciliationJob`.
- Tenant reindex and repair: `SearchReindexController.ReindexProducts`, `AdminSearchSyncController` product/category/CMS sync, tenant CommerceHub `ReindexTenantAsync`, and AF16E affiliate repair.
- Search repair and rebuild: product search consumers, category/CMS sync, CommerceHub reindex/auto-healing, search analytics recovery, service-marketplace drift repair, and affiliate public search repair.
- Recovery jobs: CommerceHub auto-healing, external outcome recovery health, payment/refund/payout/shipment hosted recovery services, inventory reservation cleanup, abandoned cart/checkout scanners, and projection failure compensation.
- Replay jobs: MassTransit projection consumers, stock/accounting/marketplace outbox processors, marketplace projection outbox, payment-cancellation outbox, and projection compensation replay.
- Startup recovery: CommerceHub index initializer, CommerceHub auto-healing startup recovery, Mongo index initialization, Hangfire recurring-job deferred registration, and startup seeders.

Authority owner is SQL. Execution owners are controllers, hosted services, Hangfire jobs, consumers, and outbox processors. Enforcement owners are SQL authority overlays, tenant/platform middleware, lineage helpers, replay fences, claim leases, stale-delete/tombstone writers, and convergence telemetry.

## 2. Reindex Findings

Storefront product and tenant search reindex enumerate current SQL product ids and pass each candidate through `ProductSearchSqlAuthorityOverlay`. Category and CMS repair use SQL lineage/publication and delete stale documents.

CommerceHub global reindex uses `CommerceHubSearchSqlAuthorityOverlay`, SQL listing seeds for Mongo-missing listings, SQL-visible count verification before alias promotion, and now a SQL-visible stale-document sweep through `DeleteStaleAsync`. Tenant reindex uses the same SQL-visible stale sweep with tenant scope.

No Mongo-derived or Elasticsearch-derived visibility authority was found on the reviewed reindex boundaries. Mongo and Elasticsearch are candidate stores only.

## 3. Search Repair Findings

Search repair is SQL-derived and stale-delete capable on reviewed product, category, CMS, CommerceHub, affiliate, and service-marketplace paths. Repair cannot promote stale Mongo or Elasticsearch documents without current SQL authority.

The main hardening added here closes a CommerceHub orphan-search gap: same-index and tenant repair now delete Elasticsearch documents absent from current SQL-visible CommerceHub listing authority, instead of only reconciling documents that still appear in Mongo.

## 4. Recovery Findings

CommerceHub recovery discovers candidates from Mongo/search sync state but reindexes or deletes only after `CommerceHubSearchSqlAuthorityOverlay`. Missing SQL authority becomes delete/no-op, not visibility restoration.

Financial, payment, payout, refund, shipment, and reservation recovery remain under their lifecycle or lineage owners and must reload current SQL evidence before mutation. Provider, carrier, queue, and recovery snapshots are evidence only.

## 5. Replay Findings

Replay processors use fences, processed-event receipts, row leases, row-version concurrency, aggregate ordering, and SQL recomposition. Projection compensation can replay marketplace events, but product/order/payment/delivery/refund replay rebuilds current SQL-shaped events or falls back to original payload only as evidence for the normal projection processor.

Replay cannot create publication or public search visibility because reviewed public mutation paths still pass through SQL overlays, stale-version checks, or delete/tombstone behavior.

## 6. Auto-Healing Findings

CommerceHub auto-healing startup/background scans use Mongo and Elasticsearch only to find candidates. Queue identity is `{ TenantId, ListingId }` where tenant evidence exists, active processing is deduplicated by that identity, and final mutation uses current SQL overlay.

Auto-healing remains intentionally bounded by queue size, batch size, retry count, circuit breaker, background scan limits, and metrics.

## 7. Startup Findings

Startup initialization ensures indexes and recurring jobs exist but does not create visibility authority. CommerceHub startup recovery scans pending/missing derivative state and enqueues tenant-scoped healing; each item is revalidated through SQL before indexing or deletion.

Hangfire initialization is retryable and idempotent. Deferred registration avoids permanent job loss from transient PostgreSQL startup failure without bypassing SQL authority.

## 8. Governance-Preservation Findings

Operational tooling preserves marketplace governance because CommerceHub reindex/healing/search repair use current listing/product/vendor/category/tenant authority. Vendor suspension, listing unpublish, product unpublish, category deletion, tenant disablement, and invalid variant lineage all collapse to search deletion or suppression.

Tenant-admin tooling is tenant-scoped and cannot delete/recreate global indexes. Platform/global operations remain platform/runbook or super-admin scope.

## 9. Auditability Findings

Dangerous operational actions are observable through structured logs, super-admin operational audit middleware, search sync status fields, auto-healing metrics, health checks, outbox replay telemetry, and `OperationalConvergenceTelemetry`.

This pass did not add SIEM-grade immutable audit storage. It preserves and extends existing operator signals, including stale CommerceHub search deletion counts in reindex results.

## 10. Tenant-Boundary Findings

Tenant repair uses trusted tenant context and tenant filters. CommerceHub auto-healing and stale deletion use tenant-scoped identities where tenant evidence exists. Unscoped fallback can delete by listing id only when tenant evidence is absent, but indexing still requires current SQL authority.

No reviewed tenant path was found that can invoke global search deletion/recreation or global alias promotion.

## 11. Chaos Findings

- Global reindex during replay: each reindex candidate revalidates SQL, and alias promotion requires SQL-visible count verification.
- Replay during vendor suspension: current vendor/listing authority causes read suppression and search delete/suppression.
- Recovery during tenant disablement: active tenant lineage fails closed in overlays and health checks.
- Startup rebuild during governance change: startup auto-healing only enqueues candidates; current SQL authority decides mutation.
- Auto-healing during category deletion: CommerceHub category lineage failure deletes search visibility.
- Search repair during product unpublish: product/listing overlays reject visibility and delete stale documents.

Current SQL authority wins in each reviewed scenario.

## 12. Runtime Hardening Performed

- Added `ICommerceHubSearchIndexService.DeleteStaleAsync(...)` to scan CommerceHub Elasticsearch documents and delete documents missing from the supplied SQL-visible identity set.
- Added SQL-visible CommerceHub listing identity recomposition in `CommerceHubReindexService`.
- Wired stale CommerceHub search deletion into global, target-index, and tenant reindex flows before count verification or completion.
- Added `StaleDeleted` to CommerceHub reindex results/logging.

## 13. Monitoring Improvements

CommerceHub reindex logs and result payloads now expose stale search deletion counts. Existing CommerceHub search health, auto-healing metrics, search sync fields, service-marketplace drift telemetry, outbox replay telemetry, and super-admin operational audit remain the monitoring boundary.

## 14. Governance Updates

PASS 11.4 establishes that global operational authority is SQL-derived convergence authority only. Future reindex, repair, recovery, replay, healing, startup, cache-warmer, support, feed/export, reporting, analytics, or global-script work that touches public visibility must declare authority owner, execution owner, enforcement owner, tenant/global scope, stale-delete behavior, auditability, and regression coverage.

## 15. Honest Remaining Debt

- No universal SQL-vs-Mongo-vs-Redis-vs-Elasticsearch parity scanner was introduced.
- No SIEM-grade immutable audit store, central global-operation dashboard, universal chaos harness, or adaptive recovery scheduler was added.
- Future feed/export/CDN purge/support/reporting/search-analytics/cache-warmer/global-script paths remain path-specific certification debt.
- Elasticsearch, MongoDB, Redis, and queues remain eventually consistent; this certification proves SQL-authoritative convergence on reviewed paths, not strong consistency.
