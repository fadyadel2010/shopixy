# PASS F3 Frontend Affiliate Authority Certification Findings

Date: 2026-06-05

Verdict: Frontend Affiliate Authority is production-certifiable for the inspected affiliate and POS Admin affiliate runtime surfaces after referral-link copy/display hardening. Public launch still inherits the broader affiliate operational debt from PASS A1-A4: deployment-verified alert packs, evidence-role rollout, backup/restore drills, SIEM/WORM retention, and PostgreSQL-backed chaos coverage.

## Attribution Findings

Affiliate dashboard, order, earning, and analytics views read backend affiliate APIs under `AffiliateOnly` authority. The frontend does not carry affiliate id, attribution id, order owner, or referral owner as mutation authority. Affiliate A cannot ask the frontend to reveal Affiliate B attribution because protected reads resolve the affiliate account from the HttpOnly-cookie-backed `AffiliateJwt`, and backend reads filter by the resolved account. DevTools can alter local table rows after render, but this cannot expose backend-hidden attribution or change attribution ownership.

## Referral-Link Findings

Runtime hardening was required. Promotion-link copy/display previously assembled URLs in the browser from DTO fields. Backend attribution still validated referral codes, so this was not commission authority, but it left the frontend acting as link-construction authority. `AffiliatePromotionLinksController` now returns a backend-built `promotionUrl` using current tenant storefront URL, current same-tenant product slug, and the SQL-owned referral code. Affiliate link copy/share surfaces now use that backend-issued URL and fail closed when it is absent.

## Commission Findings

Commission totals, statuses, and order item commissions are display-only backend DTOs. The frontend never calculates earned, approved, paid, reversed, or net commission authority. PASS A3 remains decisive: immutable checkout `OrderItem` snapshots, canonical `AffiliateAttributions`, and append-only `AffiliateCommissionLedger` own commission truth.

## Balance Findings

Balance cards and payout eligibility controls are presentation only. The payout request submit sends only `{ amount }` to `/api/affiliate/payouts/request`; backend `AffiliatePayoutService` recomputes raw SQL ledger availability and active payout reservations. Browser changes to `availableForWithdrawal`, `canWithdraw`, or displayed currency cannot create withdrawable balance.

## Withdrawal Findings

Withdrawal request creation is backend-authoritative. The frontend validates amount for ergonomics, but backend threshold, raw-balance, blank/mixed-currency, reservation, and serializable transaction guards decide persistence. Pending or earned commissions cannot become withdrawable through the frontend; only backend-approved ledger balance contributes to payout eligibility.

## Payment-Method Findings

Affiliate wallet edits are request intent only. `ProfileTab` submits wallet type/number to the backend, where affiliate owner resolution and `PayoutWallet` validation own current payment instructions. Browser state can alter the edit form visually, but it cannot mutate historical payout evidence, transfer allocations, payout requests, withdrawal rows, or paid commission rows.

## POS-Admin Findings

POS Admin affiliate list/detail/order/earning/withdrawal screens call `/api/admin/affiliates` under POS admin authority. Backend tenant scope comes from `ITenantContext.RequiredTenantId`, not frontend tenant state or stale JWT tenant claims. Admin actions route to backend services/orchestrators; the UI does not write ledger rows or decide payout ownership.

## Cache Findings

Affiliate profile metadata in localStorage is a cache only. Protected affiliate routes bootstrap and periodically revalidate `/api/affiliate/profile`, and cross-tab changes to affiliate profile-cache keys force the portal back to login. No React Query affiliate authority store was found. Component state can briefly hold stale DTOs after a successful read, but money and attribution mutations always re-enter backend authority.

## DevTools Findings

DevTools tampering with React state, localStorage, query params, route params, copied link fields, balances, payout form values, wallet fields, or admin selected order ids cannot create backend authority. Tampering can change local visuals or request intent; backend identity, tenant, attribution, commission, balance, payout, wallet, and payment-evidence guards remain decisive.

## Cross-Domain Findings

Affiliate frontend authority stays isolated from POS Admin, Customer, CommerceHub Customer, and Super Admin identity domains. Affiliate protected APIs use `AffiliateJwt`/`AffiliateOnly`; POS Admin affiliate screens use POS admin auth and trusted tenant context. Affiliate profile cache keys are not read as POS, customer, or platform authority.

## Runtime Hardening Performed

- Added backend `PromotionLinkResponse.PromotionUrl` and built it from current SQL tenant/product/link authority in `AffiliatePromotionLinksController`.
- Updated affiliate link list copy to use only backend-issued `promotionUrl` and disable copy when missing.
- Updated product drawer promotion copy/share/visit actions to use only backend-issued `promotionUrl`.
- Hardened the legacy discover promotion modal to create a backend promotion link and copy/share only the returned `promotionUrl`.

## Regression Freezes Added

- `AffiliateFrontendAuthorityCertificationTests` freezes backend-issued promotion URLs, frontend no-synthesis copy/share behavior, backend-only payout request submission, profile-cache-only session behavior, and this certification document structure.

## Architecture Updates

PASS F3 is recorded in this findings document, `.ai-context` authority/risk/rules entries, and the new regression freeze. The frontend affiliate rule is now explicit: browser state may display or request, but it must not construct referral-link, attribution, commission, balance, payout, payment-method, or tenant authority.

## Remaining Debt

- Browser back/forward cache and multi-tab stale-display behavior still need deployment rehearsal with real browser sessions.
- Dedicated frontend telemetry for blocked or missing backend-issued affiliate promotion URLs is not present.
- Versioned payout-method history remains useful for operator forensics, although wallet edits do not mutate historical payout evidence.
- Broader affiliate alert packs, SIEM/WORM retention, backup/restore drills, evidence-role deployment, and PostgreSQL-backed chaos coverage remain inherited operational debt from PASS A1-A4.

## Required Answers

| Question | Answer |
|---|---|
| Can Affiliate A see Affiliate B data? | No on inspected runtime paths. Affiliate APIs resolve the account from `AffiliateJwt`; POS Admin affiliate screens are tenant-scoped by backend trusted tenant context. |
| Can referral links be forged through frontend state? | Not as backend authority. After hardening, copy/share uses backend-issued `promotionUrl`; DevTools-forged text is only local text and checkout attribution still revalidates SQL referral authority. |
| Can frontend influence commissions? | No. Commission amount, ownership, lifecycle, and payoutability remain backend SQL/ledger-owned. |
| Can frontend influence balances? | No. Displayed balances are presentation; payout guards recompute raw SQL ledger availability. |
| Can pending commissions appear withdrawable? | Not authoritatively. DevTools can alter local visuals, but backend payout creation excludes pending/earned commissions. |
| Can payment methods be tampered with visually? | Locally yes, durably no. Wallet form changes are request intent only; backend owner validation and historical evidence immutability are decisive. |
| Can POS Admin see unrelated affiliate data? | No on inspected runtime paths. Admin affiliate data is filtered by trusted tenant context and backend ledger/store relationships. |
| Can localStorage tampering create affiliate authority? | No. localStorage stores profile metadata only and protected routes revalidate backend profile authority. |
| Were runtime code changes required? | Yes. Referral-link copy/display was hardened to backend-issued `promotionUrl`. |
| Is Frontend Affiliate Authority production-certifiable? | Yes for inspected runtime authority surfaces after hardening, subject to inherited operational launch debt. |
