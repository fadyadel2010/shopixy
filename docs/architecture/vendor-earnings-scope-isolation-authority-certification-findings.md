# Vendor Earnings Scope Isolation Authority Certification Findings

Date: 2026-06-23

PASS M6-G

## Verdict

Vendor Earnings Scope Isolation Authority is production-certifiable for the inspected CommerceHub vendor earnings, finance, payout-method, statement/export, bootstrap, cache, recovery, and adapter runtime paths after actor-bound earnings endpoint enforcement and nested finance evidence scope validation.

## Scope Findings

Backend SQL and endpoint authorization own vendor, tenant, actor, earnings, payout, settlement, statement, and financial scope. The browser carries only backend-bound statement filter/pagination and payout-method destination intent. It has no vendor, tenant, actor, earnings, payout, settlement, or statement scope selector.

## Runtime Authority Findings

Earnings reads derive the vendor exclusively from trusted `ITenantContext`; finance reads/exports derive the same scope through `RequiredTenantId`. The earnings endpoint now independently enforces the backend Owner/Admin financial actor cohort. Frontend adapters accept dashboard, item, payout, payout-item, statement transaction, refund-impact, and settlement rows only if their backend vendor evidence matches the current authenticated tenant.

## Vendor Findings

Vendor A cannot become Vendor B. Vendor-scoped query keys include backend-hydrated tenant and actor identities; each accepted wrapper and nested evidence row must match the current vendor. No route, dialog, selected row, cache key, mutation response, or export input specifies a vendor.

## Actor Findings

Actor scope is backend-authenticated and endpoint-authorized. Login, logout, role, tenant, or actor replacement clears the query client and POS runtime cache. Bootstrap revalidates `/auth/session` before protected rendering after focus, visibility, reconnect, navigation, BFCache, and periodic recovery.

## Tenant Findings

Tenant scope is derived on the server from `ITenantContext`, not browser state. Client query keys partition cache by backend tenant/actor; authority loss removes the CommerceHub vendor subtree. Foreign wrapper or nested DTO scope fails closed at presentation.

## Payout Findings

Payouts and payout items remain backend-issued lineage evidence. They require current tenant scope, exact parent-payout binding, and a matching current earning/sub-order. A payout cannot create an earning or widen earnings scope.

## Settlement Findings

Settlement remains backend financial evidence. Recent settlements now require current vendor scope, canonical settlement ID/type/direction/currency/date evidence before the finance dashboard can render. Settlement references cannot become earnings scope.

## Async Findings

Concurrent, late, stale, refetch, focus, and reconnect responses remain candidates only. Query keys bind requests to tenant/actor scope; route presentation is withheld while recovery fetches run, and every response is scope-validated at render. A response for a previous principal cannot overwrite accepted current scope.

## Replay Findings

Refresh, history, BFCache restoration, and reconnect only re-run backend session/data reads. There is no local/session-storage earnings scope, React Query persistence/hydration, dialog scope, selected-row scope, or mutation-response scope that can revive a revoked grant.

## Cache Findings

React Query is in-memory. Actor transition clears all query state; authority loss removes vendor-session earnings, finance, payment, and payout-method subtrees. Cached DTOs are presentation candidates and cannot be authoritative.

## Failure Findings

401/403 trigger authority loss, scoped purge, and session revalidation. 404/409/422 remain backend outcomes. Missing, malformed, conflicting, forged, foreign, stale, or scope-incoherent DTOs fail closed.

## Adversarial Findings

React state, query cache, mutation responses, route/search params, local/session storage, tables, dialogs, selected rows, browser history, refresh, reconnect, and BFCache cannot fabricate, emerge, replay, widen, or transfer vendor earnings scope. Backend reauthorizes every endpoint and frontend never submits scope authority.

## Runtime Hardening Performed

- Restricted the earnings endpoint to backend `Owner,Admin` actor authority.
- Added backend vendor identity to refund-impact DTOs and projection.
- Required nested refund-impact and settlement rows to match the current vendor before finance dashboard presentation.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubFinancialDashboardDtos.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`

## Regression Freezes Added

- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsScopeIsolationAuthorityCertificationTests.cs`.
- Extended frontend authority adapter coverage for foreign nested refund-impact and settlement rows.

## Architecture Updates

- Added this certification finding and recorded PASS M6-G in the authority context files.

## Explicit Certification Answers

- Who owns Vendor Earnings Scope Isolation Authority? Backend SQL lineage, trusted tenant context, authenticated actor authorization, endpoint authorization, and backend DTO issuance.
- Can frontend fabricate scope? No.
- Can scope emerge, replay, or widen itself? No.
- Can Vendor A become Vendor B scope? No.
- Can actor scope survive replacement? No as authority.
- Can tenant scope survive replacement? No as authority.
- Can stale responses re-enter scope? No; they are withheld/purged and revalidated.
- Can payout scope become earnings scope? No.
- Can settlement scope become earnings scope? No.
- Can cached scope become authoritative? No.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Vendor Earnings Scope Isolation Authority production-certifiable? Yes, for inspected runtime paths after the listed hardening and regression freezes.
