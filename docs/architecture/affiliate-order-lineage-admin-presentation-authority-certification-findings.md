# PASS AF-ORD1 Affiliate Order Lineage Admin Presentation Authority Certification

Certification date: 2026-07-01

Scope reviewed: PointOfSale.Web Orders runtime and directly consumed backend admin order read APIs/services for Affiliate-origin order read and attribution presentation only. Storefront lineage already certified in SF-ORD5.1, CommerceHub marketplace runtime, affiliate registration, payout processing, commission calculation, checkout implementation beyond affiliate lineage creation boundaries, payment, shipping, builder, CMS, analytics, and prior certified domains were not re-reviewed.

## Findings

Affiliate Order Lineage is backend-owned. Affiliate lineage begins when `AffiliateReferralMiddleware` admits backend-protected referral evidence and checkout revalidates it against current SQL affiliate and promotion-link authority. Attribution begins only when checkout stamps the canonical SQL `Order` affiliate snapshot and creates the canonical `AffiliateAttributions` row in the same transaction. Canonicalization begins in backend checkout/SQL and continues in `AdminOrderService`, which now admits admin presentation evidence only from SQL `Orders` plus matching `AffiliateAttributions` rows. Presentation begins only after `AdminOrderDto` emission and frontend `requireBackendAffiliateAttributionAuthority` admission.

Admin Orders list/details now surface nullable `AffiliateAttribution` evidence from backend SQL only. Standard order rows get evidence after `AdminOrderService.AttachAffiliateAttributionEvidenceAsync` reloads canonical attribution rows whose tenant, order id, affiliate id, referral code, and promotion link match the immutable order snapshot. Order details use `LoadAffiliateAttributionEvidenceAsync` with the same parity check. CommerceHub marketplace rows remain null and are not expanded by this pass.

Frontend Orders cannot fabricate Affiliate Attribution. `adminOrdersApi` already uses no-store admin order reads and current tenant/order/source admission. `commerceHubVendorIdentityAuthority` now rejects malformed, cross-tenant, cross-order, incomplete, unknown-source, or replayed affiliate attribution evidence before presentation. React renders an affiliate badge and detail section only for admitted backend DTO evidence; absence renders nothing and is not replaced by fallback identity, referral code, or attribution metadata.

Recovery converges to SQL truth. Refresh, reconnect, application restart, Redis flush, Mongo projection rebuild, stale DTO replay, and browser cache restoration re-enter backend admin order SQL reads. Affiliate recovery can repair a missing canonical row only from the immutable order snapshot; Mongo affiliate projections remain derivative and are not consumed as admin Orders authority.

## Boundary Summary

- Affiliate Boundary: owner backend SQL `AffiliateAccount`/`AffiliatePromotionLink` plus protected middleware evidence; recovery source current SQL; failure behavior no affiliate mode or no displayed attribution.
- Referral Boundary: owner backend referral evidence token plus SQL referral-code ownership; recovery source SQL order snapshot/canonical attribution; failure behavior fail closed/null evidence.
- Attribution Boundary: owner SQL `Orders` affiliate snapshot plus `AffiliateAttributions`; recovery source immutable order snapshot; failure behavior no admin affiliate presentation.
- Canonical Boundary: owner checkout transaction and `AdminOrderService` SQL parity mapping; recovery source SQL; failure behavior null evidence or 404/500.
- Recovery Boundary: owner backend services and SQL repair; Redis/Mongo/browser caches are derivative; failure behavior refetch/fail closed.
- Presentation Boundary: owner PointOfSale.Web rendering after DTO admission only; failure behavior withheld affiliate badge/details.

## Runtime Hardening Performed

- Added `AdminAffiliateAttributionDto` to admin order list/details DTOs.
- `AdminOrderService` now maps affiliate attribution evidence from SQL `AffiliateAttributions` only when it matches the canonical order snapshot.
- `commerceHubVendorIdentityAuthority` now admits or rejects affiliate attribution DTO evidence before Orders presentation.
- `Orders.jsx` now renders affiliate badges/details only from admitted backend evidence.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/pages/Orders.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliateOrderLineageAdminPresentationAuthorityCertificationTests.cs`
- Updated `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`

## Required Answers

- Who owns Affiliate Identity? Backend SQL affiliate authority.
- Who owns Referral Identity and Referral Code? Backend protected referral evidence plus SQL affiliate/promotion-link ownership; historical order snapshot/canonical attribution after conversion.
- Who owns Affiliate Attribution and Commission Attribution? Backend SQL `Orders`, `AffiliateAttributions`, and commission ledger/services.
- Who performs canonicalization? Backend checkout and `AdminOrderService` SQL mapping.
- Can frontend fabricate, modify, or remove Affiliate Attribution? No; it can only render admitted DTO evidence or withhold presentation.
- Can React, React Query, Zustand, LocalStorage, SessionStorage, Redis, or Mongo become Affiliate Authority? No.
- Can Affiliate Attribution change after recovery? No; recovery converges to immutable SQL snapshot/canonical attribution.
- Is Affiliate Order Lineage production-certifiable? Yes, after this admin presentation hardening.
