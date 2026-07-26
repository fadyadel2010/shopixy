# PASS F2 Frontend CommerceHub Authority Certification Findings

Date: 2026-06-05

Verdict: CommerceHub frontend runtime is production-certifiable for the inspected customer and POS Admin CommerceHub authority surfaces after the EGP-only frontend currency display/request hardening in this pass. Public launch still inherits broader PASS 15.6/16.1B deployment, monitoring, evidence-retention, backup/restore, SMTP/session, and PostgreSQL-backed chaos blockers.

## Discovery Findings

Customer discovery, search, homepage, autocomplete, category, filter, product-card, and product-detail surfaces call backend CommerceHub discovery APIs. Backend PASS 16.2 authority remains decisive: SQL-overlaid listing/product/variant/category/media/price/stock/publication visibility filters returned DTOs. Frontend route/query manipulation can request candidate pages or product ids, but direct product/detail reads still fail closed at the API when SQL authority no longer proves visibility.

## Cart Findings

Cart browser state is intent and display only. Add/remove/quantity mutations shape local state, but backend cart save/pricing sanitizes listing, product, variant, store, publication, stock, and price authority. Checkout is blocked unless pricing is refreshed and cart validation passes. This pass additionally pinned customer frontend cart currency state to CommerceHub EGP so localStorage-tampered cart currency cannot render mixed CommerceHub authority before backend sanitation.

## Checkout Findings

Checkout refreshes the saved cart, re-prices through `POST /api/commercehub/cart/price`, checks backend payment-method availability, then submits checkout with cart version, item identity, quantity, expected price, shipping selections, and idempotency evidence. Backend PASS 16.4 remains the authority for prices, shipping, totals, commission, settlement, payment, inventory reservation, and order creation. Frontend totals are stale-client evidence only.

## Order Findings

Customer orders and order details are backend DTO reads. The frontend displays backend status/action fields and exposes payment retry only when backend says `canRetryPayment`. There is no customer order-status mutation UI in the inspected customer app. POS Admin order lifecycle authority remains backend SQL lifecycle services from PASS 16.5A.

## Refund Findings

Customer refund initiation is item-level request intent. The modal can be exposed by DevTools, but submit payloads carry order item id, quantity, reason, payout evidence, media URLs, and idempotency only; backend PASS 16.5E validates ownership, refundable quantity, lifecycle state, payout snapshot, return policy, and financial reversal authority. UI `canRefund` gates are not treated as backend authorization.

## Publication Findings

POS Admin CommerceHub publication UI already disables publish/update for rows whose backend DTO marks `isCommerceHubCurrencyEligible === false`, shows the EGP-only rule, and keeps unpublish available for visibility reduction. Bulk operations inspected on this surface are unpublish and remove-override, both backend-routed. Backend PASS 16.5F-B remains decisive for publish, republish, update, bulk, replay, recovery, and reindex restrictions.

## Financial Findings

POS Admin CommerceHub balances, payment requests, finance summaries, statements, and histories read backend SQL/ledger-derived DTOs. Frontend date filters and CSV requests are report parameters only. This pass hardened displayed finance/payment currency so a stale or malicious dashboard/statement payload with non-EGP currency renders as blocked instead of appearing as CommerceHub money authority.

## Currency Findings

CommerceHub frontend currency is EGP-only. Customer `PointOfSale.CommerceHub` formatting now refuses non-EGP CommerceHub currency labels and cart state normalizes CommerceHub cart currency to EGP. POS Admin finance/payment display helpers also refuse non-EGP CommerceHub currency labels, and vendor repayment request creation always appends `EGP` instead of reusing a dashboard-provided currency.

## Cache Findings

React Query and Zustand caches are presentation and request-intent stores only. Query caches can briefly show stale backend DTOs, but final cart pricing, checkout, refund creation, payment initiation, publication, and finance mutations call backend authority. The new currency guard prevents cached non-EGP CommerceHub values from rendering as valid CommerceHub currency authority.

## DevTools Findings

DevTools tampering with React state, route ids, query params, localStorage, sessionStorage, checkout request bodies, refund modal state, publication controls, or finance dashboard data cannot create backend authority on inspected paths. Tampering may create local intent or blocked visuals, but backend APIs revalidate identity, ownership, publication, price, currency, inventory, order, refund, and financial authority.

## Runtime Hardening Performed

- Added `PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts` and routed customer CommerceHub `formatCurrency` through it so non-EGP cached/API-shaped currency displays fail closed.
- Normalized CommerceHub customer cart display currency to EGP in `types/cart.ts` and `store/cart-store.ts`, including server-cart hydration, local add-to-cart, cart merge, and item sync paths.
- Added `PointOfSale.Web/src/utils/commerceHubCurrencyAuthority.js` and routed POS Admin CommerceHub finance/payment formatters through it.
- Changed POS Admin vendor repayment submission to append the CommerceHub authority currency `EGP`, not dashboard-provided currency.

## Regression Freezes Added

- `CommerceHubFrontendAuthorityCertificationTests` freezes customer frontend EGP fail-closed display, cart currency sanitation, checkout backend refresh-before-submit behavior, POS Admin EGP-only finance/payment display and request shaping, publication currency UI blocking, and this certification document structure.

## Architecture Updates

PASS F2 is recorded in this findings document and in the new regression freeze. `.ai-context` entries were updated to define CommerceHub frontend state as presentation/intent only and frontend CommerceHub currency as EGP-only display/request shaping.

## Remaining Debt

- Customer CommerceHub app has no local Vitest test runner; current frontend freeze is a repository source certification test.
- Browser back/forward cache and multi-tab stale display behavior should be manually rehearsed in deployment with real browser sessions.
- Dedicated frontend telemetry for blocked non-EGP CommerceHub cached payloads is not yet present.
- Public launch remains blocked by broader deployment, monitoring, alert, WORM/SIEM retention, backup/restore, SMTP/session, and PostgreSQL chaos evidence.

## Required Answers

| Question | Answer |
|---|---|
| Can frontend influence CommerceHub prices? | No. Frontend can submit expected price snapshots as stale-client evidence, but backend price authority recomputes through CommerceHub price services. |
| Can frontend influence checkout totals? | No. Checkout totals, shipping, promo, commission, settlement, and order snapshots are backend recomputed. |
| Can unpublished products appear? | Not from certified backend discovery/detail APIs. A tampered local cart line cannot survive backend sanitation/pricing/checkout. |
| Can hidden products remain in cart? | Only transiently in malicious/local browser state; backend cart save/pricing/checkout removes or rejects them. |
| Can invalid refunds be initiated? | UI can be tampered, but backend refund creation validates ownership, quantity, lifecycle, and payout evidence before persistence. |
| Can publication restrictions be bypassed? | No on certified runtime paths. UI blocks non-eligible rows and backend enforces publication policy. |
| Can non-EGP CommerceHub products appear publishable? | No for inspected POS Admin UI rows with currency eligibility metadata; backend remains decisive. |
| Can localStorage tampering create CommerceHub authority? | No. LocalStorage can alter local display/intent only; backend authority is revalidated, and non-EGP display is now blocked. |
| Were runtime code changes required? | Yes. Frontend EGP-only currency display/request hardening was required. |
| Is Frontend CommerceHub Authority production-certifiable? | Yes for inspected runtime authority surfaces after hardening, subject to broader launch-readiness blockers. |
