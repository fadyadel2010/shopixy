# PASS AF-PA5.1 Customer Journey Attribution Persistence Certification

Customer Journey Attribution Persistence is production-certifiable after protected-evidence lifecycle cleanup hardening. Backend `AffiliateReferralMiddleware`, current tenant resolution, protected `AFF_ATTR` evidence, SQL `AffiliatePromotionLink`, current affiliate account authority, current product/category/price/stock admission, request-scoped `AffiliateModeResolver`, and checkout final SQL revalidation own Journey Attribution. Frontend navigation, Home, category, search, product details, filters, sort, browser history, BFCache, refresh, tabs, React state, and `affiliateRefTracker` may only preserve navigation or submit referral candidates.

## Journey Findings

After Storefront Admission, attribution can survive normal customer movement from Home to category to search to product to Home to another product. The preserving artifact is not a frontend route, query string, React state, or session storage value. It is backend-issued protected `AFF_ATTR` evidence plus re-entry through `AffiliateReferralMiddleware` on subsequent API requests. Home sections, category navigation, product discovery, product details, search, sort, and filters all use `httpClient`; `httpClient` may attach `X-Affiliate-Ref` from the tracker, but that header is only a candidate. Dropping `?ref=` during filter rewrites or navigation does not by itself terminate a valid admitted journey because the HttpOnly protected cookie can re-enter backend admission.

## Persistence Findings

Attribution persistence is preserved at `AffiliateReferralMiddleware` when it accepts a tenant-bound, unexpired, DataProtection-protected `AFF_ATTR` payload and then revalidates current SQL promotion-link/account/product authority before setting request-scoped affiliate mode. `AffiliateModeResolver` remains request-scoped only. `AFF_ATTR` is evidence only: HttpOnly, Secure, SameSite=Lax, path-wide, and capped at 30 days. Checkout still performs final SQL revalidation before durable order attribution.

Attribution disappears when protected evidence is absent, tampered, foreign-tenant, expired, no longer backed by a current active promotion link/account, or rejected by current product/category/price/stock admission. Runtime now deletes stale protected evidence when current SQL link/account authority no longer supports it.

## Lifecycle Findings

Affiliate suspension, closure, deletion, inactive/missing affiliate account authority, disabled/deleted promotion links, wrong-store links, deleted/unpublished/hidden/inactive products, inactive categories, missing positive affiliate price, unavailable stock without backorder, and expired cookies terminate attribution admission. They do not let the frontend keep attribution alive. Stale protected evidence is cleared on current-backend rejection paths added in this pass.

## Cache Findings

Browser history, BFCache, refresh, React state, query strings, session storage, in-memory context, and product/homepage caches remain derivative. Cache can replay a route or resend a candidate header, but every accepted affiliate request must pass backend protected-evidence validation and current SQL revalidation. Local storage is not used for affiliate attribution persistence.

## Failure Findings

Timeout, cancellation, offline, invalid cookie, restore failure, failed product/category/search reads, and backend 4xx/5xx responses do not preserve attribution by themselves. Middleware swallows non-critical referral-processing errors to avoid blocking storefront rendering, but it withholds affiliate mode unless backend admission succeeds. If backend evidence cannot be validated, the journey continues as non-attributed.

## Adversarial Findings

Modifying cookies, headers, route params, query params, session storage, React state, product IDs, referral codes, browser history, or replaying old journey URLs cannot preserve attribution without backend evidence. A modified `AFF_ATTR` fails DataProtection validation; a valid-looking header or session value is only a candidate; a stale protected cookie must still match current SQL authority.

## Journey Attribution Pipeline

Affiliate Promotion Link
-> Storefront Admission in `AffiliateReferralMiddleware`
-> protected `AFF_ATTR` evidence issued
-> customer navigation through Home/category/search/product/sort/filter/history/refresh/tabs
-> each API request re-enters middleware via protected cookie or candidate header
-> current backend SQL revalidation restores request-scoped `AffiliateModeResolver`
-> Cart Admission and checkout final SQL revalidation
-> durable order attribution only after backend acceptance

Journey Attribution is preserved exactly at the protected-evidence plus current-SQL revalidation boundary in `AffiliateReferralMiddleware`; it is not preserved by frontend navigation.

## Runtime Hardening Performed

- Cleared protected promotion-link evidence when the current active SQL promotion link no longer exists.
- Cleared protected evidence when promotion-link evidence no longer matches current SQL affiliate/link authority.
- Cleared protected evidence when the resolved affiliate account is inactive or missing.
- Cleared protected v1 evidence when current affiliate-account authority is missing or mismatched.
- Added a regression freeze for Customer Journey Attribution persistence, stale protected-evidence cleanup, frontend candidate-only referral forwarding, and URL-driven navigation/filter separation.

## Exact Runtime Files Modified

- `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliatePromotionLinkResolutionStorefrontEntryCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Required Answers

- Who owns Journey Attribution? Backend `AffiliateReferralMiddleware`, protected evidence validation, current SQL authority, and checkout final revalidation.
- Can frontend preserve attribution? No. Frontend can only preserve navigation and submit candidate referral hints.
- Can navigation lose attribution? Normal navigation should not lose valid backend-governed attribution while protected evidence remains valid; navigation can lose candidate URL/header state, but that is not authority.
- Can browser history become authority? No. It can replay locations only.
- Can cookies become authority? No. `AFF_ATTR` is protected backend evidence, and current SQL revalidation is still required.
- When does attribution terminate? On missing/tampered/foreign/expired evidence, stale or disabled promotion link, inactive/deleted/suspended affiliate account, lifecycle-invalid product/category/price/stock authority, or checkout final revalidation rejection.
- Where is Attribution Persistence enforced? In `AffiliateReferralMiddleware` before request-scoped `AffiliateModeResolver` is set, then again at checkout final SQL revalidation.
- Were runtime changes required? Yes. Stale protected-evidence deletion was hardened.
- Is Customer Journey Attribution Persistence production-certifiable? Yes.
