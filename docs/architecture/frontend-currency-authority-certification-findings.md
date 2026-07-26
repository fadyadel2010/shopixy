# PASS F5 Frontend Currency Authority Certification

Date: 2026-06-07
Verdict: production-certifiable for inspected frontend currency authority after runtime hardening.

## CommerceHub Findings

CommerceHub customer display remains EGP-only through the CommerceHub currency authority helper. Cart state normalizes to EGP, checkout refreshes backend cart/pricing authority before order creation, and order/refund/customer financial displays use backend DTO currencies that are rejected visually when non-EGP.

## Publication Findings

Publication UI now fails closed unless `isCommerceHubCurrencyEligible` is explicitly `true`. Legacy DTOs or stale cached rows that omit eligibility cannot expose publish/update as available. Product price display no longer falls back to EGP when product currency is missing.

## Affiliate Findings

Affiliate portal product discovery, product drawer, public affiliate landing, and storefront affiliate landing no longer inject EGP or USD when backend product/tenant currency is missing. Missing or invalid product currency renders `Currency unavailable` instead of relabeling the amount.

## Financial Findings

PASS F4 financial hardening remains authoritative: CommerceHub financial actions are EGP-only, and affiliate financial surfaces render backend row/bucket currencies without collapsing mixed currencies.

## Cache Findings

React Query, Zustand, localStorage, sessionStorage, and cached DTOs are presentation/request-intent only. Stale CommerceHub publication eligibility now fails closed, and stale affiliate/storefront product currency cannot be rewritten to EGP or USD by frontend fallback logic.

## DevTools Findings

DevTools tampering can alter local state or request intent, but inspected backend APIs still own publication, checkout, refund, settlement, payout, ledger, affiliate, and tenant currency authority. Frontend currency fields cannot create backend authority.

## Legacy DTO Findings

Legacy/missing currency DTOs were found on frontend product-display surfaces. Runtime hardening removed the active fallbacks so missing currency blocks display instead of becoming EGP or USD.

## Runtime Hardening Performed

- POS wishlist analytics DTOs now include backend tenant currency.
- POS wishlist analytics UI renders backend currency or `Currency unavailable`, not hardcoded USD.
- CommerceHub publication UI blocks missing currency eligibility and does not display missing product currency as EGP.
- Affiliate product card, product drawer, public affiliate landing, and storefront affiliate landing no longer inject EGP/USD fallback currency labels.

## Exact Files Modified

- `PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs`
- `PointOfSale.Storefront/src/pages/AffiliateLandingPage.tsx`
- `PointOfSale.Tests/Architecture/CommerceHubFrontendAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/FrontendCurrencyAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/components/WishlistAnalyticsCard.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliateLanding.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProductCard.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`

## Regression Freezes Added

- `FrontendCurrencyAuthorityCertificationTests.CommerceHubPublicationFrontend_MustFailClosedWhenCurrencyEligibilityIsMissing`
- `FrontendCurrencyAuthorityCertificationTests.AffiliateProductFrontend_MustNotInjectFallbackCurrencyLabels`
- `FrontendCurrencyAuthorityCertificationTests.PosWishlistAnalytics_MustUseBackendTenantCurrency`
- `FrontendCurrencyAuthorityCertificationTests.CertificationDocumentation_MustRecordPassF5FrontendCurrencyAuthority`

## Architecture Updates

PASS F5 establishes that frontend currency labels must be backend-owned or fail closed. CommerceHub remains EGP-only. POS/storefront may use tenant currency. Affiliate product and financial displays must not inject EGP/USD when backend currency is missing.

## Remaining Debt

No PASS F5 runtime blocker remains. Broader operational monitoring, browser rehearsal, and PostgreSQL-backed mixed-currency chaos proof remain inherited launch-readiness debt.

## Required Answers

1. Can non-EGP CommerceHub values appear? No on certified CommerceHub authority displays; non-EGP CommerceHub currency is blocked visually and backend authority remains EGP-only.
2. Can publication restrictions drift? No after hardening; missing/stale eligibility fails closed.
3. Can affiliate currencies collapse? No on certified financial surfaces; product displays no longer inject fallback labels.
4. Can frontend convert authority currencies? No conversion authority was found or added.
5. Can cached DTOs override currency authority? No; cached missing/non-EGP CommerceHub data fails closed, and affiliate/storefront missing currency is not rewritten.
6. Can localStorage tampering create currency authority? No.
7. Can legacy DTOs bypass currency rules? No after hardening; missing eligibility/currency blocks instead of relabeling.
8. Were runtime code changes required? Yes.
9. Is Frontend Currency Authority production-certifiable? Yes for inspected runtime surfaces after hardening.
