# PASS AF-PA4.4 Promotion Link Resolution, Storefront Entry & Attribution Admission Certification

Promotion Link resolution and Storefront Attribution Admission are production-certifiable after backend storefront-admission hardening. Backend `AffiliateReferralMiddleware`, SQL `AffiliatePromotionLink`, current tenant resolution, active affiliate account authority, current product lifecycle/category/price/stock authority, protected attribution evidence cookies, and checkout final SQL revalidation own Referral Resolution, Storefront Admission, and Attribution Admission. Frontend route/query/session/header state is referral intent only.

## Promotion Resolution Findings

Promotion URL generation remains backend-owned by `AffiliatePromotionLinksController` and `StorefrontUrlResolver`. At storefront entry, `AffiliateReferralMiddleware` now resolves `ReferralCode` only against an active, non-deleted `AffiliatePromotionLink` whose `TenantId` matches the current backend-resolved storefront tenant. A forged code, route edit, header edit, tenant URL edit, or replayed code cannot become valid unless current backend SQL finds that exact active store-owned link.

## Storefront Entry Findings

Storefront entry passes backend validation before affiliate mode is activated. The admission boundary is `AffiliateReferralMiddleware` after `TenantResolutionMiddleware` establishes current store scope. The middleware validates the promotion link, product, affiliate account, protected first-click cookie evidence, and current lifecycle before it sets `AffiliateModeResolver` request context or writes `AFF_ATTR`.

## Attribution Findings

Attribution begins only after backend Storefront Admission sets request-scoped affiliate mode and protected attribution evidence. The frontend `affiliateRefTracker`, `AffiliateLink`, browser URL, session storage, and `X-Affiliate-Ref` header nominate a referral candidate only. Checkout later revalidates the admitted affiliate/promotion-link authority against current SQL before stamping the order snapshot and creating `AffiliateAttributions`.

## Lifecycle Findings

Inactive links, deleted links, wrong-store links, disabled/deleted/inactive/unpublished/hidden products, inactive categories, missing positive affiliate price, unavailable inventory without backorder, and suspended/closed/deleted affiliate accounts fail closed before Storefront Attribution Admission. Existing protected attribution cookies are tenant-bound, expiring, tamper-protected evidence and stale invalid promotion-link evidence is discarded.

## Multiple Referral Findings

Multiple referral attempts are backend-governed. A protected `AFF_ATTR` cookie preserves the first admitted referral for the tenant while valid; later query/header/route candidates cannot replace it in the browser. If the protected evidence no longer matches current SQL authority, backend admission fails closed instead of letting the frontend choose a winner.

## Cache Findings

Browser history, BFCache, React state, session storage, and query/cache replay cannot create attribution. They can only resend a referral candidate through URL/header state. Every API request that carries a candidate re-enters backend middleware admission, and checkout performs final SQL revalidation before durable attribution.

## Failure Findings

Invalid referral, stale lifecycle, 400/401/403/404/410/422-style denial, 500, timeout, cancellation, or offline failure does not create attribution. Middleware skips or withholds affiliate mode on failure and never writes an attribution cookie unless backend admission succeeds. Checkout rejects stale promotion-link authority before order attribution.

## Adversarial Findings

Modifying `ReferralCode`, `ProductId`, tenant URL, store host, route path, session storage, `X-Affiliate-Ref`, old promotion links, deleted links, or repeated clicks cannot fabricate attribution. Promotion-link admission requires current SQL link/store/product/category/price/stock/account authority; checkout revalidates current SQL again.

## Storefront Admission Review

Runtime pipeline:

Promotion URL
-> backend-resolved tenant
-> `AffiliateReferralMiddleware`
-> active store-owned `AffiliatePromotionLink`
-> current product lifecycle/category/price/stock validation
-> active affiliate account validation
-> Storefront Admission via `AffiliateModeResolver.SetAffiliateModeV2`
-> protected `AFF_ATTR` attribution evidence
-> Storefront rendering
-> checkout final SQL revalidation before durable `AffiliateAttribution`

Storefront Admission occurs in `AffiliateReferralMiddleware` immediately before `AffiliateModeResolver.SetAffiliateModeV2` and `EnsureAttributionCookie`.

## Runtime Hardening Performed

- Required promotion-link referral resolution to match the current backend-resolved tenant.
- Added current product lifecycle, active affiliate category, positive price, and inventory/backorder admission before v2 affiliate mode activation.
- Cleared stale protected attribution evidence when current promotion-link lifecycle admission rejects it.
- Added regression coverage for backend tenant/lifecycle-owned Storefront Admission and frontend candidate-only referral forwarding.

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

- Who owns Referral Resolution? Backend SQL and `AffiliateReferralMiddleware`.
- Can frontend resolve referrals? No. It can only forward candidate referral evidence.
- Can forged ReferralCodes become valid? No. Only current backend SQL active referral authority can admit a code.
- Can Storefront fabricate attribution? No.
- Can inactive Promotion Links enter Storefront? No.
- Can suspended Affiliates create attribution? No.
- Can multiple referrals compete? They can be submitted as candidates, but backend first-click protected evidence and current SQL validation decide.
- Where does Storefront Admission occur? In `AffiliateReferralMiddleware` before `AffiliateModeResolver.SetAffiliateModeV2` and `EnsureAttributionCookie`.
- Where does Attribution begin? At backend Storefront Admission as protected request/cookie evidence, then durably at checkout SQL attribution creation.
- Were runtime changes required? Yes.
- Is Promotion Link Resolution & Storefront Entry production-certifiable? Yes.
