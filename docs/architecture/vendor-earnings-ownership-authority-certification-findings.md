# Vendor Earnings Ownership Authority Certification Findings

Date: 2026-06-23

PASS M6-B

## Verdict

Vendor Earnings Ownership Authority is production-certifiable on the inspected CommerceHub POS vendor earnings, finance, payout-history, payout-method, statement-export, bootstrap, cache, and recovery runtime paths. Backend evidence and authorization own vendor, tenant, actor, earnings, settlement, and payout bindings. Frontend only presents current backend evidence and fails closed.

## Ownership Findings

- Backend SQL and endpoint authorization own vendor, tenant, actor, earning, payout, and settlement ownership.
- Each earning item now carries `tenantId`; each payout item carries `tenantId` and its exact `payoutId`.
- The client accepts nested rows only when their vendor/tenant evidence matches the current authenticated vendor scope.

## Runtime Authority Findings

- `/api/commercehub/earnings`, finance dashboard/statement/export, payout methods, and payment dashboard derive vendor scope from backend tenant context and authenticated actor claims; the browser submits no vendor, tenant, actor, earning, payout, or settlement ownership selector.
- Frontend state is limited to filters, pagination, payout-method destination intent, and display state. It creates no ownership-bearing identifier or optimistic ownership result.

## Vendor Findings

- Vendor A cannot become Vendor B: dashboards, payout history, payout methods, and statement rows are withheld unless DTO tenant/vendor evidence equals the current session tenant.
- Foreign earnings cannot appear as local earnings: individual earning rows require current tenant evidence, and payout history items must resolve to a same-tenant earning with matching sub-order and payout binding.

## Actor Findings

- Actor transitions clear the query client and POS runtime cache. Session bootstrap validates `/auth/session` on initial load, focus, visibility recovery, reconnect, navigation history, BFCache restoration, and a periodic interval.
- Previous actor evidence cannot be replayed as current authority; rendering waits for current session validation and DTO scope validation.

## Tenant Findings

- Tenant scope comes from backend tenant context, not route/search/local/session storage or request DTO ownership fields.
- Query keys include tenant and actor identity; authority loss removes CommerceHub vendor query subtrees and any actor transition clears all query state.

## Payout Findings

- Payout ownership is backend-owned. A payout row must prove current tenant ownership; every payout item must prove the same tenant and exact parent payout ID.
- Payout ownership cannot become earnings ownership: presentation requires the referenced earning to already be a current-tenant backend earning with matching sub-order and exact payout binding.

## Settlement Findings

- Settlement references remain backend financial evidence only. No inspected frontend earnings route, state, cache, mutation, or selection maps a settlement ID into an earning or vendor ownership grant.
- Statement transactions require current vendor entity evidence; settlement transaction type is display/filter-only.

## Replay Findings

- Refresh, reconnect, browser history, and BFCache can only re-run backend session and data reads. They cannot replay ownership into an accepted presentation without current scope and lineage validation.

## Cache Findings

- React Query is in-memory only; no earnings ownership hydration is persisted in localStorage or sessionStorage.
- Cached DTOs are candidates, never authority. Scoped cache keys, authority-loss purge, actor-transition clearing, refetch withholding, and render-time validation prevent cached ownership from becoming authoritative or surviving revocation.

## Failure Findings

- 401/403 causes authority-loss purge and revalidation. 404/409/422 remain backend failure outcomes. Missing, malformed, conflicting, forged, or stale ownership DTO fields fail closed before display.

## Adversarial Findings

- Route/search parameters, selected rows, dialogs, table state, browser history, local/session storage, mutation responses, cache tampering, and fabricated nested IDs cannot create, transfer, widen, or revive ownership.
- A forged wrapper with a foreign nested earning/payout item is rejected because row-level tenant, payout, and earning lineage must agree.

## Runtime Hardening Performed

- Added backend tenant ownership evidence to vendor earning items.
- Added backend tenant and exact payout ownership evidence to payout items on both earnings-history and payout-service DTO projections.
- Strengthened frontend validation to require row-level current vendor ownership and to bind each payout item to its parent payout and a matching current backend earning.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubFinanceDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`
- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsOwnershipAuthorityCertificationTests.cs`

## Architecture Updates

- Added this findings document and recorded PASS M6-B in the required authority context files.

## Explicit Certification Answers

- Who owns Vendor Earnings Ownership Authority? Backend SQL lineage, tenant and actor authorization, endpoint authorization, payout/settlement bindings, and backend DTO issuance.
- Can frontend fabricate ownership? No.
- Can ownership emerge? No.
- Can ownership replay? No as authority.
- Can ownership survive refresh? No as authority; refresh revalidates backend session and evidence.
- Can cached ownership become authoritative? No.
- Can Vendor A become Vendor B? No.
- Can foreign earnings appear as local earnings? No.
- Can payout ownership become earnings ownership? No.
- Can settlement ownership become earnings ownership? No.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Vendor Earnings Ownership Authority production-certifiable? Yes, for the inspected runtime paths after this hardening.
