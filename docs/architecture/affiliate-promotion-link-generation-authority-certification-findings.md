# PASS AF-PA4.3 Affiliate Promotion Link Generation Authority Certification

## Promotion Link Findings

Promotion Link generation is backend-owned. Authenticated affiliate UI can only request generation through `POST /api/affiliate/promotion-links` with `tenantId` and `productId`; it cannot submit a referral code, promotion URL, link id, affiliate id, lifecycle state, storefront base URL, slug override, or persistence instruction. Backend `AffiliatePromotionLinksController.Create` resolves the current affiliate user from the affiliate JWT, validates current product and affiliate selection admission, creates or reactivates an `AffiliatePromotionLink`, and returns backend-built `PromotionUrl` only after current read admission.

## Identity Findings

Authoritative identity is backend SQL `AffiliatePromotionLink.Id` plus the unique relationship `(AffiliateUserId, TenantId, ProductId)` and URL-facing `ReferralCode`. `AffiliateUserId` comes from current backend affiliate auth claims, `TenantId` and `ProductId` are validated against current SQL product/store authority, and `ReferralCode` is generated server-side. Regeneration of the same active or inactive relationship is idempotent and preserves the existing identity; it does not let the frontend replace the referral code or create a second active relationship.

## Relationship Findings

The relationship is `AffiliatePromotionLink`, backed by `UQ_AffiliatePromotionLinks_User_Store_Product`. Multiple non-deleted promotion links for the same affiliate/store/product relationship are database-impossible. Repeated generation re-enters backend selection admission, then returns or reactivates the existing row. Deactivation only changes `IsActive`; it does not grant the browser authority to create a replacement row.

## URL Findings

The backend builds `PromotionUrl` from `StorefrontUrlResolver.Resolve(tenant)`, current SQL product slug/product id, and backend `ReferralCode`. Frontend `LinksTab`, `DiscoverTab`, and `ProductDrawer` copy/share/display only backend-issued `promotionUrl`; they do not synthesize `?ref=` URLs from referral code, product slug, base URL, or route state. Forged URLs remain only URL strings unless backend referral resolution finds a current active promotion-link row under the correct store authority.

## Lifecycle Findings

Promotion Link Admission requires current backend affiliate session authority, current product existence, non-deleted active published storefront-visible product state, enabled non-deleted affiliate policy, active non-deleted affiliate category, positive affiliate price, available inventory or backorder allowance, active tenant URL resolution, non-deleted link state, and active link state for copy/display. Product archive/delete/disable, category disable, affiliate policy disablement, tenant URL loss, and affiliate suspension/closure force backend rejection, omission, or session loss on the next backend re-entry.

## Cache Findings

React state, copied state, clipboard, browser history, BFCache, local/session storage, and stale network responses are presentation only. Runtime hardening adds no-store/no-cache request hints for promotion-link create/list/deactivate calls and `[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` on the promotion-link controller. Cached URLs may remain text in clipboard/history, but they are not authority and must re-enter backend referral/link/lifecycle validation before they can affect commerce.

## Failure Findings

Generation failure cannot appear successful because frontend state promotes a copy/share URL only from a backend response containing a non-empty `promotionUrl`. `400`, `401`, `403`, `404`, `409`, `422`, `500`, timeout, and offline failures leave `promotionUrl` empty or remove list presentation. Failed reads set empty/unavailable presentation and do not synthesize fallback links.

## Adversarial Findings

Modified DTOs cannot submit authoritative affiliate id, tenant scope beyond the requested store candidate, referral code, link id, slug, storefront base URL, URL, lifecycle, or persistence fields. Modified ProductId/TenantId must pass current backend product/store selection admission. Replay generation returns the existing backend row or fails current admission. Replay of stale backend responses cannot become durable authority because copy/share uses backend-issued URL text only, and every actual use must re-enter backend referral resolution.

## Promotion Link Admission Review

Runtime pipeline:

Affiliate/Product Relationship
-> Backend Validation
-> Promotion Link Identity
-> Promotion URL Generation
-> Backend Persistence
-> Current Backend Read
-> Promotion Link Admission
-> Frontend Display

Promotion Link Admission occurs in `AffiliatePromotionLinksController.MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct)`. This boundary reloads current product and tenant authority, re-runs selection admission when required, resolves the backend storefront URL, builds `PromotionUrl`, and only then returns a display/copy/share DTO. Frontend admission is presentation-only: it accepts or withholds that DTO but never creates authority.

## Runtime Hardening Performed

- Added no-store response cache metadata to `AffiliatePromotionLinksController`.
- Routed promotion-link list/deactivate frontend calls through `affiliateApi`.
- Added no-store/no-cache request hints to frontend promotion-link create/list/deactivate calls.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs`
- `PointOfSale.Web/src/services/affiliateApi.js`
- `PointOfSale.Web/src/modules/affiliate/pages/portal/LinksTab.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/services/affiliateApi.test.ts`
- `PointOfSale.Tests/Architecture/AffiliateFrontendAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Required Answers

- Who owns Promotion Link generation? Backend SQL and `AffiliatePromotionLinksController`.
- Can frontend generate Promotion URLs? No. It can only request generation and display/copy/share backend-issued `promotionUrl`.
- Can duplicate Promotion Links exist? No duplicate non-deleted relationship can exist for the same affiliate/store/product due to database uniqueness.
- Can forged URLs become valid? No. A forged URL is just text unless backend referral resolution admits a current active link.
- Can stale Promotion Links survive lifecycle changes? They may remain as stale text, but cannot remain authoritative after backend lifecycle re-entry.
- Can clipboard/history become authority? No.
- Where does Promotion Link Admission occur? `AffiliatePromotionLinksController.MapToResponseAsync(... requireCurrentSelectionAdmission: true ...)`.
- Were runtime changes required? Yes, no-store hardening for promotion-link responses and frontend calls.
- Is Promotion Link Generation production-certifiable? Yes.
