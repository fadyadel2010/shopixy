# PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority

## Scope

Reviewed only PointOfSale.Web admin Order Management runtime responsible for displaying CommerceHub Marketplace-origin orders and Marketplace lineage evidence, plus backend admin order DTO/API/service code directly consumed by that read and presentation path.

Pages reviewed:
- `PointOfSale.Web/src/pages/Orders.jsx`

Backend endpoints reviewed:
- `GET /api/admin/orders`
- `GET /api/admin/orders/{orderId}`

Backend services reviewed:
- `AdminOrdersController`
- `AdminOrderService`
- `IAdminOrderService`
- Admin order DTOs directly consumed by Orders

## Findings

Marketplace Order Lineage is production-certifiable after explicit backend DTO evidence and frontend admission hardening. Backend SQL `CommerceHubMarketplaceOrders` and `CommerceHubSubOrders`, scoped by trusted POS tenant/session authority and Owner/Admin/Manager authorization, own Marketplace Identity, Marketplace Name, Marketplace Order ID, Marketplace Order Number, External Customer Identity, Vendor Identity, Marketplace Metadata, Marketplace Status, Marketplace Financial Metadata, Marketplace Fulfillment Metadata, Marketplace Source Type, canonical vendor sub-order presentation, tenant ownership, persistence, and recovery.

Closed runtime gap: admin Orders identified Marketplace rows with `Source = Marketplace` and `IsMarketplaceOrder`, but it did not expose a dedicated backend-admitted Marketplace lineage evidence object for administrators to inspect. Runtime now emits nullable `AdminMarketplaceLineageDto` evidence for Marketplace rows only, mapped from current SQL parent marketplace order and vendor sub-order evidence. Storefront rows keep `MarketplaceLineage = null`.

Frontend presentation now requires `requireBackendMarketplaceLineageAuthority` before Marketplace source presentation. The admission boundary rejects missing lineage, non-Marketplace lineage injection, foreign tenant/vendor evidence, mismatched order id, malformed Marketplace identity, unsupported source type, incomplete status/payment/currency totals, and malformed payment/fulfillment/financial metadata JSON. React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, modified Marketplace IDs, external IDs, vendor IDs, metadata, statuses, financial/fulfillment metadata, or removed evidence cannot fabricate, modify, remove, preserve, recover, or distribute Marketplace Lineage Authority.

## Pipeline

Marketplace Import -> Backend Validation -> SQL `CommerceHubMarketplaceOrders` and `CommerceHubSubOrders` -> `AdminOrderService` SQL read -> `AdminMarketplaceLineageDto` -> `adminOrdersApi` no-store read -> frontend DTO admission -> Orders presentation.

Marketplace Lineage begins in backend SQL marketplace order/sub-order authority. Canonicalization begins when backend creates the CommerceHub marketplace order and vendor sub-orders. Authority begins at backend trusted session/tenant, SQL, and services. SQL persistence begins in CommerceHub order creation persistence. Presentation begins only after admin DTO emission and frontend lineage admission.

## Boundary Review

Marketplace Boundary: backend CommerceHub marketplace order/sub-order SQL. Recovery source is SQL. Failure behavior is no lineage presentation.

External Identity Boundary: backend SQL marketplace order customer evidence. Recovery source is SQL. Failure behavior is fail-closed DTO admission.

Vendor Boundary: `CommerceHubSubOrder.StoreId` and current POS tenant authority. Recovery source is SQL. Failure behavior is order not found or rejected frontend presentation.

Canonical Boundary: backend marketplace order/sub-order creation and admin DTO mapping. Recovery source is SQL, not projection/cache. Failure behavior is no accepted Marketplace presentation.

Recovery Boundary: refresh/reconnect/restart/Redis flush/Mongo rebuild/projection rebuild re-enter SQL-backed admin reads. Failure behavior is loading/error/empty presentation, never frontend repair.

Presentation Boundary: `requireBackendMarketplaceLineageAuthority` and Orders rendering. Recovery source is backend refetch. Failure behavior is rejected presentation.

## Runtime Hardening Performed

- Added `AdminMarketplaceLineageDto`.
- Mapped Marketplace lineage evidence from SQL `CommerceHubSubOrders` and parent `CommerceHubMarketplaceOrders` in admin list/details.
- Set Marketplace details `SourceOrderId` from backend SQL parent marketplace order identity.
- Added frontend `requireBackendMarketplaceLineageAuthority`.
- Rendered Marketplace Lineage details only after frontend admission.
- Added frontend and architecture regression freezes.

## Files Modified

- `PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `PointOfSale.Web/src/pages/Orders.jsx`
- `PointOfSale.Tests/Architecture/MarketplaceOrderLineageAdminPresentationAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Certification Answers

Did review only PointOfSale.Web runtime: Yes.
Frontend files modified: `Orders.jsx`, `commerceHubVendorIdentityAuthority.js`, `commerceHubVendorIdentityAuthority.test.ts`.
Backend files modified: `AdminOrderDtos.cs`, `AdminOrderService.cs`.
Owner of Marketplace Identity, Marketplace Order ID, External Order Identity, Vendor Identity, Marketplace Metadata, Marketplace Financial Metadata, Marketplace Fulfillment Metadata, Marketplace Source Type, Canonical Order, canonicalization, persistence, and recovery: backend SQL/services.
Frontend can fabricate Marketplace Lineage, modify Marketplace Identity, modify Vendor Identity, remove Marketplace evidence, or make React/React Query/Zustand/LocalStorage/Redis/Mongo projection Marketplace Authority: No.
Marketplace Lineage can change after recovery: No; recovery converges to backend SQL truth.
Runtime changes required: Yes.
Marketplace Order Lineage is production-certifiable: Yes.
