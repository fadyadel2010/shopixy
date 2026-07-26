# Vendor Earnings Identity Authority Certification Findings

Date: 2026-06-23

PASS M6-A

## Verdict

Vendor Earnings Identity Authority is production-certifiable for the inspected CommerceHub POS/Admin vendor earnings and finance runtime paths after frontend payout item and earning item identity-lineage hardening plus scoped cache purge hardening.

## Identity Findings

- Earnings identity originates only in backend DTO evidence from `/api/commercehub/earnings`, including backend `vendorId`, `earningId`, `subOrderId`, optional `originalEarningId`, `refundId`, `payoutId`, payout `payoutId`, payout item `payoutItemId`, and payout item `earningId`/`subOrderId` lineage.
- Frontend runtime does not generate `earningId`, `earningNumber`, `earningEntryId`, payout identity, settlement identity, order-to-earnings identity, payment-to-earnings identity, refund-to-earnings identity, return-to-earnings identity, or lineage identifiers.
- The earnings page renders dashboard totals and payout history only after `deriveVendorEarningsDashboardPresentation` accepts current backend vendor scope and well-formed backend IDs, amounts, currency, statuses, and dates.
- The finance page reuses the same earnings dashboard boundary for payout-history presentation and does not construct earnings identity from statement filters, date filters, table rows, charts, or route state.

## Runtime Authority Findings

- Backend owns Vendor Earnings Identity, Earnings Lineage, Vendor Identity Binding, Order to Earnings Lineage, Payment to Earnings Lineage, Refund to Earnings Lineage, Return to Earnings Lineage, Settlement to Earnings Lineage, payout membership, tenant scope, actor scope, SQL rows, locks, idempotency, and endpoint authorization.
- Frontend owns display, formatting, selection-free table rendering, filtering of finance statements, sorting-free payout display, navigation, presentation withholding, and backend-bound payout-method intent only.
- `CommerceHubEarnings.jsx` and `CommerceHubFinance.jsx` consume earnings identity as backend evidence only; neither screen has a route parameter, search parameter, dialog identifier, selected earning row state, editable earning identity, local storage, session storage, browser history state, optimistic cache write, or mutation response that can create earnings identity.
- Payout method creation invalidates and refetches current backend earnings and payout-method evidence; it does not patch or mint earnings, payout history, payout membership, settlement lineage, or vendor earnings rows.

## Replay Findings

- Refresh, focus, visibility, online, popstate, and BFCache pageshow re-enter current POS vendor session authority through `/auth/session` and withhold route presentation during validation.
- React Query cache entries are tenant/user scoped under `commercehub-vendor-session`; dashboard presentation is withheld while current refetch is active, so stale rows cannot remain current authority during revocation or actor replacement.
- Runtime hardening changed POS vendor authority-loss purge to remove the scoped `commercehub-vendor-session` subtree and scoped earnings/finance/payment domains by predicate, closing the previous non-matching bare-key purge gap.
- Cached DTOs and stale mutation responses cannot become authoritative because accepted earnings presentation requires current backend vendor identity and validated payout/earning lineage at render time.

## Lineage Findings

- Order identity remains order evidence only. The frontend validates earnings item `subOrderId` and order number shape for display, but it cannot convert route, order, table, or statement references into `earningId`.
- Payment identity remains payment evidence only. No inspected frontend path maps `paymentId` or provider references into an earning identity.
- Refund identity remains refund evidence only. Optional earning item `refundId` is accepted only as backend GUID evidence inside the current earnings DTO and cannot create, repair, or substitute an earning.
- Return identity remains return evidence only. No inspected frontend earnings path has return route, dialog, selected row, or mutation state capable of deriving earnings identity.
- Settlement and payout identity remain backend evidence only. Payout history rows require backend `payoutId`; payout items require backend `payoutItemId`, `earningId`, `subOrderId`, order number, amount, and optional exact payout binding.

## Cache Findings

- Initial page load, refresh, reconnect, focus, visibility, online recovery, back/forward navigation, and BFCache restoration depend on backend session revalidation and scoped React Query reads.
- Cache restoration cannot become authoritative because `deriveVendorEarningsDashboardPresentation` fails closed for missing vendor evidence, foreign vendor evidence, malformed payout IDs, malformed payout item IDs, malformed earning IDs, unsupported earning statuses, invalid dates, invalid currency, invalid amounts, and unsupported payout risk flags.
- There is no localStorage or sessionStorage persistence for earnings identity, selected earnings, payout rows, or earning lineage.

## Tenant Findings

- Vendor tenant boundaries hold through current POS vendor session evidence and DTO validation requiring backend `vendorId`/`tenantId` to match the current tenant.
- Admin monitoring paths are outside this POS vendor earnings screen and remain read-only under their own Super Admin payment/payout/order authority certifications.
- Cross-tenant cache reuse is blocked by tenant/user-scoped query keys, actor-transition query clearing, protected endpoint authority-loss purge, and current-vendor DTO validation.
- Vendor identity cannot transfer between vendors because foreign earnings dashboard, payout history, payout method, finance transaction, and statement evidence fails closed before presentation.

## Failure Findings

- 401 and 403 on protected CommerceHub vendor endpoints trigger POS vendor authority loss, scoped query purge, session revalidation, and route-presentation withholding.
- 404, 409, 422, malformed DTOs, missing identifiers, conflicting identifiers, forged identifiers, and replay identifiers remain backend-owned failure outcomes or frontend fail-closed presentation outcomes; they do not create frontend earnings authority.
- Unsupported risk flags, payout statuses, earning statuses, dates, amounts, currency, GUIDs, and vendor scope fail closed in the frontend adapter.

## Adversarial Findings

- React state, query cache, mutation responses, route params, search params, localStorage, sessionStorage, dialog state, selected rows, table state, grid state, browser history, refresh recovery, reconnect recovery, and BFCache restoration cannot fabricate, reconstruct, preserve, transfer, or revive earnings identity without current backend acceptance.
- Stale identity cannot survive backend revocation as current authority because refetch and authority-loss paths purge or withhold presentation, and adapter validation re-runs against current user scope.
- Frontend-generated identity cannot become authoritative because no runtime code generates or submits earnings IDs as authority; payout method mutation is payout-destination intent only and earnings convergence is refetch-only.

## Runtime Hardening Performed

- Added payout item validation for backend `payoutItemId`, `earningId`, `subOrderId`, optional exact `payoutId` binding, order number, and amount before payout history can render.
- Added top-level earning item validation for backend `earningId`, `subOrderId`, optional `originalEarningId`, `refundId`, `payoutId`, order number, amount, currency, canonical earning status, and lifecycle dates.
- Added current vendor scope validation to payout history rows.
- Hardened POS vendor authority-loss query purge to remove scoped earnings, finance, payment, and payout-method caches under the current vendor-session query-key subtree.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`
- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsIdentityAuthorityCertificationTests.cs`

## Architecture Updates

- Added `docs/architecture/vendor-earnings-identity-authority-certification-findings.md`
- Updated `.ai-context/PROJECT_CONTEXT.md`
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`
- Updated `.ai-context/KNOWN_RISKS.md`
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- Updated `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Who owns Vendor Earnings Identity? Backend SQL earnings, payout, settlement, order, payment, refund, return, vendor, tenant, actor, lineage, locks, idempotency, and endpoint authorization own it.
- Can frontend fabricate earnings identity? No.
- Can earnings identity emerge? No.
- Can earnings identity replay? No as authority; replay can only trigger backend reads or fail-closed presentation.
- Can stale identity survive refresh? No as current authority.
- Can cached identity become authoritative? No.
- Can order identity become earnings identity? No.
- Can payment identity become earnings identity? No.
- Can refund identity become earnings identity? No.
- Can return identity become earnings identity? No.
- Can settlement identity become earnings identity? No.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Vendor Earnings Identity Authority production-certifiable? Yes, for inspected CommerceHub POS/Admin vendor earnings and finance runtime paths after the listed hardening and freezes.

## Verification

- `npm test -- --run src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter CommerceHubVendorEarningsIdentityAuthorityCertificationTests`
