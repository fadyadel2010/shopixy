# CommerceHub Vendor Scope Isolation Authority Certification Findings

Date: 2026-06-16

Status: PASS M1-H production-certifiable for inspected frontend runtime paths.

## Scope Findings

- Backend POS session, vendor, tenant, capability, lifecycle, governance, catalog, order, financial, analytics, storefront, and CMS authorities own vendor scope truth.
- Frontend scope is derivative only. DTOs missing explicit backend tenant/vendor evidence now fail closed at the shared current-vendor boundary.
- Actor and tenant replacement purge scoped query caches and re-enter `/api/auth/session`; storage and cross-tab events are terminal/revalidation signals only.

## Catalog Findings

- CommerceHub listings/products/KPIs/analytics/notifications are current-vendor validated before presentation.
- Catalog mutations remain backend-authorized intent and are capability/eligibility gated; mutation responses do not become ownership authority.

## Order Findings

- POS order list/detail DTOs now carry backend `TenantId` evidence.
- Order list and route/history-selected order detail presentation are withheld unless current-vendor validation accepts the DTOs.

## Financial Findings

- Finance, earnings, payments, statements, payout methods, proof access, settlements, and selected payment methods remain routed through `commerceHubVendorFinancialParticipationAuthority`.
- Financial rows are withheld during current refetch and require current backend vendor identity evidence.

## Analytics Findings

- Analytics routes are capability-gated and protected endpoint authority-loss detection now includes `/admin/analytics` and `/admin/pixel-tracking`.
- Analytics DTOs must carry current tenant/vendor evidence before vendor-scoped presentation.

## Storefront Findings

- Storefront builder/config/preview and theme paths remain current-session scoped; protected endpoint authority-loss detection now includes store-config and preview endpoints.
- Storefront preview no longer relies on storage-derived tenant scope and DTOs missing tenant evidence fail closed.

## Cache Findings

- Vendor query keys are scoped by current backend tenant/user where React Query is used, and actor/tenant transitions clear query caches.
- Cache cannot restore foreign scope because presentation boundaries require current backend session scope plus DTO tenant evidence.

## Realtime Findings

- POS vendor cross-tab storage events are terminal logout/revalidation signals only.
- Focus, visibility, online, popstate, and BFCache restoration re-enter backend session authority; they do not publish vendor scope.

## Failure Findings

- 401/403 on catalog, orders, finance, earnings, payments, payout methods, storefront builder/theme/config/preview, CMS/footer/pages, analytics, pixel/config, abandoned checkout, and manual-payment vendor paths dispatch vendor authority-loss revalidation.
- 404/malformed/missing-scope DTOs fail closed for inspected presentation paths.

## Adversarial Findings

- Frontend cannot fabricate ownership, appear as another vendor, restore foreign scope from cache/storage/history/URL state, or bypass backend ownership authority on inspected paths.
- Tenant leakage is blocked by backend endpoint authority plus frontend current-vendor DTO evidence validation.

## Runtime Hardening Performed

- Required explicit backend tenant/vendor evidence in `deriveCurrentVendorScopedDto`.
- Added backend tenant evidence to POS admin order list/detail DTOs.
- Routed POS order list/detail presentation through current-vendor validation.
- Expanded POS vendor protected endpoint authority-loss detection across orders, storefront, analytics, pixel/config, abandoned checkout, and manual-payment paths.

## Modified Runtime Files

- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/pages/Orders.jsx`
- `PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`

## Certification Answers

- Vendor scope authority is owned by the backend.
- Frontend cannot fabricate vendor scope on inspected paths.
- Vendor A cannot access Vendor B catalog, orders, finances, analytics, or storefront through inspected frontend runtime authority.
- Cache cannot restore foreign scope because accepted presentation requires current backend scope and DTO tenant/vendor evidence.
- Tenant leakage is not available on inspected paths after hardening.
- Runtime changes were required and performed.
- CommerceHub Vendor Scope Isolation Authority is production-certifiable for inspected frontend runtime paths.
