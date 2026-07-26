# PASS F6 - Frontend Adversarial Authority Certification Findings

Date: 2026-06-07

## Scope

Adversarial runtime review of deployed frontend code in `PointOfSale.Web/src`, `PointOfSale.Storefront/src`, and `PointOfSale.CommerceHub/src`, using PASS F1-F5 as baseline truth. The review focused on authority, ownership, isolation, cache safety, state safety, DevTools resistance, cross-tab safety, browser persistence, session safety, request tampering, and legacy frontend paths.

## React-State Findings

React and Zustand state remains display or intent only. POS protected shells, affiliate protected shells, Super Admin protected shells, Storefront checkout state, and CommerceHub customer/cart state all depend on backend session/cart/pricing/order endpoints before protected or financial workflows become authoritative. DevTools can change visible state, but inspected backend calls still require current cookies, CSRF, tenant/session authority, cart version, and backend recomputation.

## localStorage Findings

No frontend token authority was found in inspected runtime paths. POS and affiliate localStorage profile keys are metadata only and are revalidated by backend session/profile calls. CommerceHub cart persistence stores cart display/intent state and version, not checkout/payment authority. One runtime gap was found: missing CommerceHub currency in cached or legacy DTOs could be relabeled as EGP by frontend fallbacks.

## Multi-Tab Findings

POS, affiliate, Super Admin, and CommerceHub customer tabs converge after same-browser logout or session replacement through storage signals and periodic/focus/visibility backend revalidation. Cart conflict handling reloads backend cart state on version conflicts or locked-cart responses.

## Route Findings

Route params and direct URLs do not create backend authority. Product, order, checkout recovery, affiliate, POS, and Super Admin screens call backend APIs that re-resolve current identity and ownership. Public route params can choose a read target, but backend visibility/ownership remains decisive.

## Query-Param Findings

Query params are filter/recovery evidence only. Search/category/filter params shape public queries, and recovery/verification tokens are submitted to backend validation endpoints. No inspected query param path grants tenant, affiliate, payout, refund, publication, or session authority.

## Request-Tampering Findings

Frontend request payloads remain intent. Backend owns pricing, stock, checkout, publication, payout, refund, commission, debt, session, and tenant authority. CSRF headers are injected for unsafe requests, cart/checkout requests carry version/idempotency evidence, and backend services recompute or reject stale/tampered values.

## Cache Findings

Runtime caches are derivative. POS runtime cache is tenant/user scoped and invalidated on auth changes. CommerceHub React Query financial caches are removed on session loss. Storefront checkout state is cleared on auth transitions and checkout completion. The F6 hardening closed stale currency cache poisoning where missing currency could be rendered as EGP.

## Browser Lifecycle Findings

Mounted protected shells revalidate on interval/focus/visibility where implemented by prior passes. Storefront checkout completion clears active order context. Browser back/forward cache still needs real-browser deployment rehearsal, but no runtime authority bypass was found in source.

## Cross-Domain Findings

Customer, affiliate, POS Admin, CommerceHub, and Super Admin identity domains remain isolated by backend auth schemes/routes and separate frontend stores/API clients. Frontend metadata cannot cross-authorize another domain.

## Legacy-Path Findings

Legacy public marketplace CMS product/list pages in `PointOfSale.Web/src/marketplace-cms` still used `product.currency || 'EGP'`, allowing stale/malformed DTOs without backend currency to render as EGP. Runtime hardening now renders `Currency unavailable` unless the backend DTO supplies a currency.

## Runtime Hardening Performed

- `PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts` now treats missing currency as unsupported instead of defaulting to EGP.
- `PointOfSale.Web/src/utils/commerceHubCurrencyAuthority.js` now treats missing currency as unsupported instead of defaulting to EGP.
- `PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx` no longer falls back to EGP for selected variant or starting-price labels.
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx` no longer displays missing dashboard currency as EGP.
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx` now uses the CommerceHub currency authority helper and blocks missing/non-EGP display currency.
- `PointOfSale.Web/src/marketplace-cms/MarketplaceProductPublicPage.jsx` and `PointOfSale.Web/src/marketplace-cms/MarketplaceProductsPublicPage.jsx` now fail closed for missing backend product currency.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts`
- `PointOfSale.Web/src/utils/commerceHubCurrencyAuthority.js`
- `PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx`
- `PointOfSale.Web/src/marketplace-cms/MarketplaceProductPublicPage.jsx`
- `PointOfSale.Web/src/marketplace-cms/MarketplaceProductsPublicPage.jsx`

## Regression Freezes Added

`FrontendCurrencyAuthorityCertificationTests.CommerceHubCustomerAndLegacyPublicFrontend_MustNotInjectFallbackCurrencyLabels` freezes the F6 currency/cache/legacy finding by asserting:

- missing CommerceHub currency is not normalized to EGP in shared helpers;
- CommerceHub product detail does not use `product?.currency ?? "EGP"`;
- POS Admin CommerceHub payments/earnings pages do not use dashboard currency fallback;
- legacy public marketplace product pages render `Currency unavailable` instead of `product.currency || 'EGP'`.

## Architecture Updates

`.ai-context` now records PASS F6 as a runtime-hardened certification: frontend state, storage, routes, query params, requests, caches, lifecycle events, and legacy screens remain display/intent only; missing CommerceHub currency must fail closed rather than be inferred by the browser.

## Remaining Debt

- Real-browser bfcache/back-forward and multi-tab rehearsal remains deployment debt.
- Telemetry for blocked missing/non-EGP currency DTOs remains future observability debt.
- Existing broader launch debts from F1-F5 remain inherited: operational drills, alert packs, DB-level EGP constraints, parity scanners, and chaos proof.

## Required Answers

1. Can React state tampering create authority? No. It can change visuals, but backend authority remains decisive.
2. Can localStorage tampering create authority? No. It can poison display/cache metadata only; F6 removed the discovered missing-currency-to-EGP display fallback.
3. Can route manipulation bypass authority? No. Backend visibility/ownership checks remain decisive.
4. Can query parameters bypass ownership? No. Query params are filters or backend-validated recovery evidence.
5. Can request tampering create authority? No. Backend recomputation, CSRF, identity, tenant, and lifecycle services remain decisive.
6. Can cache poisoning create authority? No. F6 closed the inspected stale currency display poisoning path; caches remain derivative.
7. Can multi-tab usage preserve stale authority? No inspected authority path. Visual stale-state convergence is handled by storage signals and backend revalidation.
8. Can browser lifecycle events preserve stale authority? No inspected authority path; real-browser bfcache rehearsal remains debt.
9. Can identities cross domains? No. Customer, affiliate, POS Admin, CommerceHub, and Super Admin authority domains remain backend-isolated.
10. Can legacy paths bypass authority? Previously, legacy marketplace public pages could mislabel missing currency as EGP. Fixed in runtime.
11. Were runtime code changes required? Yes.
12. Is Frontend Adversarial Authority production-certifiable? Yes for inspected runtime authority boundaries after F6 hardening, with the remaining operational/browser rehearsal debt noted above.
