# PASS SF-SOF.FINAL Complete Smart Offer End-to-End Authority Certification

Date: 2026-07-02

## Verdict

Complete Smart Offer Domain is production-certifiable. No runtime behavior change was required.

Backend owns Smart Offer Identity, Lifecycle, Status, Priority, Rules, Eligibility, Redeem, Redeem Tokens, Generated Coupon, Coupon Validation, Winning Resolution, Pricing, Discount, Currency, Tax, Shipping, Payment Fee, Grand Total, Expected Pricing, Reservation, Usage, Usage Confirmation, Recovery, Replay Protection, Audit, Outbox, Projection, Persistence, and SQL Truth. PointOfSale.Web and PointOfSale.Storefront own candidate state, presentation, navigation, popup, selection, refresh, retry, rendering, and loading/error state only.

Accepted Smart Offer Authority exists only after backend admission, validation, canonicalization, redeem, generated-coupon validation, pricing, checkout, payment, recovery, persistence, and audit.

## Overall findings

Complete Smart Offer runtime remains backend-owned from administrator creation/edit through activation, storefront qualification, popup, redeem, generated coupon application, pricing preview, checkout, payment success/failure, order creation, recovery, restart/reconnect, Redis flush, Mongo/projection rebuild, audit review, and accepted DTO presentation.

The final review composes prior certified Smart Offer slices without weakening adjacent authority boundaries:

- SF-SMO.READ: backend-owned admin read and DTO admission.
- SF-SMO.LIFECYCLE: backend-owned lifecycle mutation, RowVersion, and If-Match authority.
- SF-SMO.RULE: backend-owned rule and eligibility admission.
- SF-SOF.FIN: backend-owned generated coupon, pricing, financial validation, checkout, and SQL order snapshot.
- SF-SOF.RECOVERY-AUDIT: backend-owned recovery, usage confirmation, audit, projection, and replay protection.

## Read findings

Smart Offer Read begins in SQL `SmartOffers` under trusted tenant/session/role authority. `AdminSmartOffersController.GetSmartOffers`, `GetSmartOffer`, and `GetSmartOfferStats` read tenant-owned SQL rows, generated SQL coupon evidence where present, and tenant currency, execute search/filter/sort/pagination on the backend, emit no-store DTOs, and require frontend admission through `adminSmartOfferReadAuthority` before presentation.

Storefront Smart Offer reads are display-only. `StorefrontSmartOffersController.GetSmartOffers` reads active tenant SQL Smart Offers that have active generated SQL coupons and emits only popup/trigger fields. Storefront DTOs do not expose accepted coupon, discount, currency, tax, shipping, payment fee, total, winner, or pricing authority.

## Lifecycle findings

Smart Offer Lifecycle is backend-owned. Create, edit, activate, deactivate, and archive/delete enter `AdminSmartOffersController` after trusted tenant/session authority and Owner/Admin/Manager authorization. Existing-offer mutations require accepted backend RowVersion evidence through mandatory `If-Match`; stale, missing, malformed, replayed, or modified evidence fails closed before SQL persistence.

Admin frontend lifecycle state is candidate intent only. Accepted lifecycle begins only after backend validation, SQL persistence, generated-coupon synchronization, outbox eventing, and accepted DTO emission.

## Rule findings

Smart Offer Rules and Eligibility are backend-owned. Backend validation and canonicalization accept trigger, page scope, display/frequency, priority, and generated-coupon all-order eligibility with minimum-order/max-discount constraints. Generated coupons are synchronized by backend code and clear product/category/exclusion eligibility.

Product, category, customer, customer group, store, channel, exclusion, arbitrary rule JSON, minimum quantity, maximum quantity, and explicit target-scope eligibility fields remain unsupported Smart Offer persistence fields in the inspected runtime.

## Financial findings

Generated Coupon, Coupon Validation, Winning Resolution, Pricing, Discount, Currency, Tax, Shipping, Payment Fee, Expected Pricing, and Grand Total are backend-owned. Storefront submits popup selection and redeem-token candidates only.

Financial authority begins when `StorefrontSmartOffersController.RedeemSmartOffer` validates tenant/customer/active SQL Smart Offer/active generated SQL coupon and issues a short-lived protected token. `StorefrontCartController.ResolveSmartOfferRedemptionAsync` validates that token against the current tenant/customer, reloads current active SQL Smart Offer/generated coupon evidence, resolves the coupon code server-side, blocks raw generated-coupon use without a token, and then applies normal SQL-backed coupon validation.

Pricing begins in backend cart/checkout pricing. `CheckoutService.PreviewPricingAsync` and final checkout transaction revalidation reload cart and persisted coupon state, validate generated coupon by SQL identity, recalculate pricing through backend services, compare expected pricing as stale-detection only, and persist SQL order pricing snapshots after backend financial admission.

Frontend cannot influence accepted pricing by modifying Smart Offer DTOs, redeem tokens, generated coupon codes, cart DTOs, pricing DTOs, expected pricing, selected winner, tax, shipping, payment fee, currency, or totals.

## Recovery findings

Recovery is backend-owned. Checkout-session recovery validates signed recovery tokens, resolves current SQL checkout-session evidence, verifies recovery nonce, rejects converted sessions, and requires live backend cart revalidation. Payment-intent recovery and payment lifecycle re-enter backend order/payment/coupon authority. Stored recovery snapshots, browser storage, Redis, Mongo, stale DTOs, and route tokens are not recovery source.

Payment failure releases reservations through backend payment/order failure paths. Payment success confirms coupon usage only after current SQL payment/order authority succeeds. Duplicate payment, duplicate checkout, timeout, replay, restart, reconnect, Redis flush, Mongo rebuild, and projection rebuild converge to SQL truth or fail closed.

## Audit findings

Audit begins only in backend append-only SQL `AuditLogs`, backend events, and outbox authority. Audit reads/exports are tenant-scoped, Owner/Admin authorized, and no-store. Frontend audit pages request and render backend evidence only; they do not mutate audit rows or hydrate audit evidence from browser storage, Redis, Mongo, projections, or stale DTOs.

## Projection findings

Projection begins only when backend consumers or repair services reload SQL authority and write derivative Mongo/cache documents. `SmartOfferProjectionConsumer` reads SQL `SmartOffers` and projects storefront config visibility. Coupon projection and discount read-model repair reload SQL coupon/usage truth, fence event replay, apply freshness filters, and delete stale derivative documents.

Redis and Mongo cannot become Smart Offer Authority. Redis is cart/cache evidence only. Mongo storefront config and coupon/promotion read models are derivative visibility evidence only and rebuild from SQL.

## Cross-system findings

Coupons receive backend-generated Smart Offer coupon evidence and own coupon validation/reservation/usage semantics after backend Smart Offer redeem admission. Promotions own promotion winner/stacking rules and cannot overwrite Smart Offer authority. Checkout owns pricing and final transaction revalidation. Orders persist backend-admitted pricing/order snapshots. Payments own payment success/failure and usage confirmation/release. Affiliate, Marketplace, Inventory, Builder, CMS, and Analytics remain adjacent authorities and cannot overwrite Smart Offer Identity, Lifecycle, Rules, Generated Coupon, Pricing, Recovery, Audit, Projection, Persistence, or SQL Truth.

Smart Offer cannot overwrite another domain. Cross-system handoffs carry backend-admitted candidates, snapshots, events, or presentation evidence only.

## End-to-End pipeline

Smart Offer Candidate -> Backend Admin Admission -> Rule Validation -> Lifecycle Persistence -> Generated Coupon -> Storefront Read DTO -> Popup Presentation -> Redeem -> Protected Token -> Cart Coupon Bridge -> Generated Coupon Validation -> Coupon Validation -> Pricing -> Winner Resolution -> Checkout Preview -> Expected Pricing Stale Detection -> Final Checkout Transaction -> Reservation -> Payment -> Usage Confirmation or Release -> Order Snapshot -> Audit -> Outbox -> Projection -> Recovery -> Accepted DTO -> Presentation.

- Smart Offer begins: backend SQL `SmartOffers` and admin/storefront candidates before backend admission.
- Authority begins: backend trusted tenant/session/customer/role admission against current SQL Smart Offer/generated-coupon/coupon/cart/checkout/payment truth.
- Validation begins: `AdminSmartOffersController` request/entity validation, redeem-token validation, SQL Smart Offer/generated-coupon reload, coupon validation, and checkout transaction revalidation.
- Pricing begins: backend cart/coupon validation plus `CheckoutService.PreviewPricingAsync` and final transaction revalidation before accepted pricing DTO/order snapshot.
- Recovery begins: backend recovery-token/session/payment/cart re-entry with live backend cart and SQL order/payment/coupon authority.
- Audit begins: backend append-only SQL audit/event/outbox authority.
- Projection begins: SQL-derived backend consumer or repair.
- SQL persistence begins: backend Smart Offer/generated-coupon write transactions, SQL cart coupon state, checkout order transaction, coupon reservation/usage, payment lifecycle, audit, and outbox writes.
- Presentation begins: PointOfSale.Web or PointOfSale.Storefront only after accepted backend DTO admission.

## Boundary map

Identity Boundary: backend SQL `SmartOffers`. Recovery source: SQL. Failure behavior: 404/403/reject or no presentation.

Lifecycle Boundary: backend `AdminSmartOffersController` plus SQL RowVersion/If-Match. Recovery source: no-store SQL refetch. Failure behavior: 400/404/409/428.

Rule Boundary: backend request/entity validation and canonicalization. Recovery source: SQL Smart Offer and generated coupon truth. Failure behavior: reject mutation.

Financial Boundary: backend redeem, generated coupon, coupon validation, checkout/pricing services, currency/tax/shipping/payment validation, expected-pricing comparison, and SQL order snapshot. Recovery source: live backend cart plus SQL coupon/order/payment truth. Failure behavior: fail closed pricing/checkout.

Recovery Boundary: backend checkout-session/payment-intent/payment lifecycle/cart revalidation. Recovery source: SQL plus live backend cart. Failure behavior: fail closed recovery or current backend DTO only.

Audit Boundary: backend append-only SQL `AuditLogs`. Recovery source: SQL audit rows. Failure behavior: no-store read/export failure.

Projection Boundary: backend event consumer/repair. Recovery source: SQL truth. Failure behavior: fenced replay, stale projection, rebuild, or no side effect.

Presentation Boundary: frontend admitted DTO rendering. Recovery source: backend refetch/admitted DTO. Failure behavior: loading/error/empty state only.

## Runtime hardening performed

No new runtime behavior changes were required for SF-SOF.FINAL. The complete review relies on the already hardened runtime boundaries from SF-SMO.READ, SF-SMO.LIFECYCLE, SF-SMO.RULE, SF-SOF.FIN, and SF-SOF.RECOVERY-AUDIT.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/SmartOfferEndToEndAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/complete-smart-offer-end-to-end-authority-certification-findings.md`

## Explicit certification answers

- Did you review ONLY the complete Smart Offer runtime? Yes.
- Who owns Smart Offer Identity? Backend SQL `SmartOffers`.
- Who owns Lifecycle? Backend.
- Who owns Rule Authority? Backend.
- Who owns Eligibility? Backend.
- Who owns Redeem? Backend `StorefrontSmartOffersController` and protected token service.
- Who owns Generated Coupon? Backend SQL coupon authority synchronized from Smart Offer admission.
- Who owns Pricing? Backend cart/checkout/pricing services.
- Who owns Winning Resolution? Backend coupon/promotion/checkout pricing authority.
- Who owns Financial Authority? Backend.
- Who owns Reservation? Backend checkout SQL transaction and coupon validation service.
- Who owns Usage? Backend payment lifecycle and SQL coupon usage authority.
- Who owns Recovery? Backend SQL/cart/checkout/payment/session authority.
- Who owns Audit? Backend append-only SQL audit/outbox authority.
- Who owns Projection? Backend SQL-derived consumers/repair; projection is derivative.
- Who owns SQL Truth? Backend PostgreSQL.
- Can frontend fabricate Smart Offer? No.
- Can frontend fabricate Coupon? No.
- Can frontend fabricate Pricing? No.
- Can replay fabricate Usage? No.
- Can cache become Authority? No.
- Can Redis become Authority? No.
- Can Mongo become Authority? No.
- Can Recovery diverge from SQL? No inspected path allows divergence; recovery re-enters SQL/live backend authority or fails closed.
- Can another domain overwrite Smart Offer? No.
- Where does Smart Offer begin? SQL `SmartOffers` plus candidate submission before backend admission.
- Where does Authority begin? Backend trusted tenant/session/customer/role admission against SQL truth.
- Where does Validation begin? Backend admin validation, redeem-token validation, generated-coupon validation, coupon validation, and checkout revalidation.
- Where does Pricing begin? Backend cart/coupon validation and checkout pricing preview/final transaction revalidation.
- Where does Recovery begin? Backend checkout-session/payment-intent/payment/cart re-entry.
- Where does SQL persistence begin? Backend Smart Offer/generated-coupon/cart-coupon/checkout/payment/audit/outbox write transactions.
- Where does Presentation begin? Frontend after accepted backend DTO admission.
- Were runtime changes required? No.
- Is the complete Smart Offer Domain production-certifiable? Yes.
