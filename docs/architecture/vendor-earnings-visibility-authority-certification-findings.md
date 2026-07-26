# Vendor Earnings Visibility Authority Certification Findings

Date: 2026-06-23

PASS M6-D

## Visibility Findings

Backend endpoint authorization and current tenant/actor context issue every vendor earnings, finance dashboard, statement, payout-history, payout-method, and export visibility result. The frontend has no vendor selector, earnings selector, or visibility-grant state; it only renders validated backend evidence.

## Runtime Authority Findings

`deriveVendorEarningsDashboardPresentation`, `deriveVendorFinancialDashboardPresentation`, `deriveVendorFinanceStatementPresentation`, and `deriveVendorPayoutMethodsPresentation` fail closed unless the DTO and every visibility-bearing nested row prove the current vendor tenant scope. Finance statements additionally require canonical transaction shape and coherent pagination metadata.

## Vendor Findings

Vendor A cannot see Vendor B earnings, payout history, statements, or payout methods: dashboard wrappers, earning items, payout rows/items, methods, and statement transactions must match the current authenticated tenant before presentation. Foreign evidence is withheld.

## Admin Findings

Admin finance monitoring uses separately authorized Super Admin paths. It does not feed the vendor runtime cache or vendor presentation boundary. Vendor runtime never accepts an admin-selected vendor, route parameter, or admin DTO as vendor visibility authority.

## Tenant Findings

Vendor query keys contain tenant and actor identity under `commercehub-vendor-session`; actor change clears query state and authority loss removes the scoped subtree. Route, search, browser history, and storage contain no tenant visibility selector for these pages.

## Filter Findings

Date and transaction-type filters are backend-bound statement query intent only. They are not applied to a locally broader corpus and cannot add rows outside backend visibility.

## Search Findings

No vendor earnings/finance search surface or search-parameter visibility selector exists in the inspected runtime. No search term can become a visibility grant.

## Pagination Findings

Statement pagination is backend-bound intent. The adapter now rejects a response whose row window exceeds its backend page size/count or whose `hasMore` flag contradicts page metadata, so cache/DTO tampering cannot make extra rows navigable as current visibility.

## Export Findings

Vendor statement CSV export invokes the backend vendor export endpoint with filter intent only; it submits no vendor, tenant, actor, earnings, or statement-row identifier. The file is a backend-issued blob and is never merged into frontend visibility state. Export cannot exceed server authorization.

## Replay Findings

Refresh, reconnect, focus, visibility, popstate, and BFCache restoration revalidate `/auth/session`, clear scoped authority candidates, and re-read backend data. Replay can trigger reads only; it cannot restore an accepted visibility grant after revocation.

## Cache Findings

React Query is in-memory with tenant/actor-scoped keys and no earnings persistence hydration. Cached DTOs are presentation candidates only; current scope validation and fetch withholding prevent them from becoming authority.

## Failure Findings

401/403 causes scoped authority purge and session revalidation. 404/409/422 remain backend outcomes. Missing, malformed, conflicting, forged, foreign, or pagination-incoherent DTO evidence fails closed.

## Adversarial Findings

React state, query cache, mutation responses, dialogs, tables, selected rows, route/search state, local/session storage, browser history, refresh/reconnect, and BFCache cannot fabricate, widen, transfer, or revive vendor earnings visibility. No frontend-generated visibility becomes authoritative.

## Runtime Hardening Performed

- Added statement pagination-window coherence validation before statement rows can render.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`

## Regression Freezes Added

- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsVisibilityAuthorityCertificationTests.cs`.
- Updated focused frontend financial-participation adapter coverage.

## Architecture Updates

- Added this findings document and recorded PASS M6-D in all five authority context files.

## Explicit Certification Answers

- Who owns Vendor Earnings Visibility Authority? Backend SQL/lineage, tenant and actor authorization, endpoint authorization, and backend DTO/export issuance.
- Can frontend fabricate visibility? No.
- Can visibility emerge, replay, or widen itself? No.
- Can visibility survive refresh or backend revocation? Not as authority; recovery revalidates and stale candidates are withheld or purged.
- Can cached visibility become authoritative? No.
- Can Vendor A see Vendor B earnings or payout history? No.
- Can filters, search, or pagination reveal unauthorized rows? No.
- Can exports exceed authorized visibility? No.
- Can tenant leakage occur? No on inspected runtime paths.
- Were runtime changes required? Yes: pagination metadata coherence validation.
- Is Vendor Earnings Visibility Authority production-certifiable? Yes, for the inspected CommerceHub vendor earnings, finance, payout, export, bootstrap, cache, and recovery runtime paths.
