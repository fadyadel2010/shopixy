# POS-ADMIN-CERTIFICATION-01 PASS 14.3

# Merchant Listing Operations Console Certification

## 1. Listing Search Findings

Implemented tenant-scoped server-side listing search at `GET /api/admin/commercehub/listings`. Search runs against SQL listing, product, variant, and CommerceHub category authority and covers product name, variant attribute key, SKU, barcode, and category name. It does not use MongoDB or Elasticsearch as merchant listing authority.

## 2. Operational Filter Findings

Implemented SQL-backed operational filters for published, unpublished, suppressed, orphaned, category, inventory status, and override-enabled state. Published means current listing intent is published and neither governance-suppressed nor orphaned. Suppressed means `RestorePublicationOnActivate` is set by governance. Orphaned means current SQL product, variant, category, or public product/variant authority no longer proves listing visibility.

## 3. Suppression Visibility Findings

Merchants can now see suppressed listings, suppression reason, suppression timestamp, and suppression owner. The exposed reason is actionable and intentionally nonsensitive: marketplace governance hid the listing while merchant publication intent is preserved for conditional restore.

## 4. Orphan Visibility Findings

The operations query exposes listings whose product, variant, category, product-publication, or variant-active authority disappeared. These rows stay visible to the merchant for cleanup and bulk unpublish instead of disappearing with the active-catalog editor.

## 5. Bulk Operation Findings

Implemented `POST /api/admin/commercehub/listings/bulk` with tenant-scoped per-row outcomes, partial failure handling, audit logging, and event publication for derivative convergence. Supported operations are bulk publish, bulk unpublish, bulk category update, and bulk override removal. Suppressed listings cannot be republished or category-mutated by merchant bulk operations; orphaned listings fail closed for visibility-increasing operations but can be unpublished.

## 6. KPI Findings

Implemented SQL-authoritative listing KPIs at `GET /api/admin/commercehub/listings/kpis`: total, published, unpublished, suppressed, override-enabled, and orphaned listings. The frontend now renders these separately from active-catalog editor counts.

## 7. Analytics Findings

Implemented merchant listing analytics at `GET /api/admin/commercehub/listings/analytics`: listing views from SQL impression logs, listing searches from SQL query logs, conversions from SQL CommerceHub sub-order items, and suppression counts from SQL listings. The documented authority owner is SQL CommerceHub listing/search/order authority.

## 8. Notification Findings

Implemented merchant-facing listing notifications at `GET /api/admin/commercehub/listings/notifications` for suppressed and orphaned listings. Notifications are capped, actionable, and derived from current SQL state to avoid replay spam.

## 9. Frontend / Backend Alignment Findings

`CommerceHubSettings.jsx` now includes an operations console that calls the listing, KPI, analytics, notification, and bulk endpoints. The existing active-catalog editor remains for single-listing publication and attribute editing. Search, filters, KPIs, suppression/orphan visibility, analytics, notifications, and bulk actions now align with backend SQL authority.

## 10. Performance Findings

Listing operations are server-side paged and filtered. Search/filter/KPI rows are derived through SQL joins instead of loading the full active product catalog into browser memory. The active-catalog editor still fetches paged products for edit workflows, but it is no longer the KPI/search authority.

## 11. Governance Findings

Merchant authority remains publication intent, category selection, attributes, and merchant-owned price overrides. Marketplace governance remains visibility and suppression authority. The new merchant visibility into suppression is read-only; bulk publish/category operations fail closed for governance-hidden rows.

## 12. Chaos Findings

The console now hunts for stale filters, stale KPIs, orphan leakage, stale suppression display, and bulk escalation by recomposing current SQL product, variant, category, listing, and governance restore state. Visibility-increasing bulk operations reject suppressed or orphaned rows, while visibility-reducing unpublish stays available.

## 13. Monitoring Findings

Bulk operations log tenant, operation, requested, succeeded, and failed counts. Analytics and notifications expose merchant-facing operational health from SQL. Remaining monitoring debt is a dedicated parity/drift dashboard and alert pack comparing SQL listings to derivative MongoDB/Elasticsearch projections.

## 14. Runtime Hardening Performed

Added SQL-authoritative listing search/filter/KPI/analytics/notification APIs, tenant-scoped bulk listing operations with per-row outcomes, frontend operations console controls, and preservation of actionable backend errors from PASS 14.2.

## 15. Regression Freeze Added

`MerchantCommerceHubOperationsCertificationTests` now freezes the operations console endpoints, DTOs, frontend controls, suppression/orphan visibility, SQL-derived KPI/analytics/notification semantics, safe bulk outcomes, and this PASS 14.3 documentation boundary.

## 16. Architecture Updates

The `.ai-context` authority files record PASS 14.3 as the production-capable merchant listing operations console pass. PASS 14.1 lifecycle authority and PASS 14.2 actionable failure preservation remain valid.

## 17. Remaining Debt

Remaining debt: full parity/drift scanner, dedicated alert dashboards, bulk attribute editing, richer immutable merchant notification inbox, and deeper analytics segmentation. These are operational maturity work, not blockers for the core listing operations console.

## Explicit Answers

1. Can merchants search listings efficiently? Yes, through server-side SQL listing search.
2. Can merchants filter listings efficiently? Yes, through SQL-backed operational filters.
3. Can merchants understand suppression? Yes, suppressed listings include reason, timestamp, and owner.
4. Can merchants identify orphaned listings? Yes, orphaned rows and reasons are visible.
5. Are bulk operations available? Yes, with per-row outcomes and fail-closed authority checks.
6. Are KPIs authoritative? Yes, KPIs derive from SQL listing authority.
7. Are notifications actionable? Yes, current notifications identify suppressed and orphaned rows.
8. Is tenant isolation preserved? Yes, all operations scope by authenticated tenant and listing tenant.
9. Is the merchant operations console now production-ready? Yes for core listing operations. Parity dashboards and richer analytics remain maturity debt.
10. What operational debt still remains? Parity/drift monitoring, alert packs, bulk attribute editing, immutable notification inbox, and deeper analytics segmentation.
