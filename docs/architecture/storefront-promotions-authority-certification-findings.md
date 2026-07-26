# PASS P2-C Storefront Promotions Authority Certification Findings

Date: 2026-06-08

Storefront promotions authority is production-certifiable for inspected runtime paths after recovery
snapshot hardening. PostgreSQL owns coupon, coupon usage, promotion, smart-offer generated coupon,
eligibility, scheduling, usage-limit, and discount-calculation truth. Storefront cart state,
recovery snapshots, frontend triggers, Redis, MongoDB, route values, headers, and DTO discount
amounts are evidence or presentation only.

## Coupon Findings

Coupon truth is current tenant-owned SQL `Coupons`. Storefront coupon application resolves the
authenticated customer and tenant, blocks guest coupon use, rejects raw smart-offer generated coupon
codes, and calls the SQL-authoritative `ICouponValidationService`. Deleted, disabled, expired,
future, missing, tenant-foreign, malformed, or scope-invalid coupons fail closed.

## Coupon Usage Findings

Usage limits are SQL authoritative. Coupon reservation and confirmation require an open SQL
transaction, lock the coupon row on relational providers, count confirmed `CouponUsages` plus active
`CouponReservations`, enforce customer limits and first-order reservations, and collapse replay
through tenant/order uniqueness. Early coupon consumption remains removed.

## Promotion Findings

Automatic promotion eligibility is backend authoritative through `PromotionEngine`. Active
promotions are loaded by tenant, active flag, schedule window, validated discount config, scope,
minimum order, and SQL usage capacity. Product, category, and cart promotions are calculated from
server cart snapshots, not client totals.

## Conflict Resolution Findings

Stacking is disabled for the inspected runtime. The engine computes the best automatic offer and
the prevalidated coupon result, then chooses one winner by discount amount with coupon as the tie
break. Checkout clears the losing coupon/promotion fields before persisting the order.

## Smart Offer Findings

Smart offers are presentation plus coupon intent only. Storefront smart-offer redeem requires an
authenticated same-tenant customer and an active generated SQL coupon, then issues a protected
five-minute token bound to tenant, smart offer, coupon, and customer. Applying the generated coupon
still rechecks the token, the active offer/coupon join, and the normal coupon authority service.

## Customer Eligibility Findings

Coupon and smart-offer redemption require current authenticated customer identity. Coupon authority
uses `OnlineCustomerId`/customer ownership, customer usage counts, first-order checks, and active
customer cart ownership. Anonymous users cannot apply or redeem coupons.

## Scheduling Findings

Coupon and promotion start/end dates are checked during apply, checkout recomposition, and paid
promotion usage confirmation. Expired or future promotions cannot be confirmed as paid usage.

## Pricing Findings

Discounts are bounded by validated discount configuration, max-discount caps, minimum order checks,
currency rounding, and `Math.Min(discount, subtotal/line amount)`. Checkout recomputes subtotal,
discounts, shipping, payment fee, tax, and expected pricing server-side before order creation.

## Checkout Integration Findings

Checkout revalidates coupon identity/code pairing, validates coupons by current SQL coupon id,
passes only the authoritative coupon discount into promotion conflict resolution, recomputes the
winning discount, clears stale discount fields, and compares backend totals with expected pricing.

## Order Integration Findings

Orders persist only the server-selected winner: coupon id/code or applied promotion id/type/name.
Paid promotion usage is confirmed only in `PaymentLifecycleService`, after current promotion
authority is row-locked and revalidated against active window, usage capacity, minimum order, and
current discount recomputation.

## Recovery Findings

Runtime hardening was required. Stored abandoned-cart snapshots can no longer restore coupon,
cart-promotion, or item-promotion discount authority. The recovery controller now strips stored
discount fields and recomputes a non-discounted grand total unless a live cart can be recomposed
through the cart service.

## Tenant Isolation Findings

Coupon, promotion, smart-offer, reservation, usage, checkout, and recovery paths carry explicit
tenant filters. Authenticated smart-offer and cart coupon paths validate that customer tenant claims
match the resolved tenant. Tenant-foreign coupons and promotions are not accepted.

## Concurrency Findings

Coupon reservation/confirmation and paid promotion usage use SQL transactions, row locks where
supported, active-reservation counts, confirmed-usage counts, and tenant/order uniqueness. Replay
collapses to existing usage/reservation records rather than duplicating discount benefits.

## Historical Consistency Findings

Coupon and promotion usages are durable SQL lineage for applied discounts. Projection updates and
Mongo current-usage fields are derivative. Historical orders retain coupon/promotion identity and
discount snapshots, but payment-time promotion usage confirmation revalidates current promotion
authority before creating usage lineage.

## Adversarial Findings

Coupon forgery, coupon replay, promotion spoofing, discount inflation, customer impersonation,
eligibility bypass, recovery abuse, DTO tampering, route tampering, tenant guessing, and stacking
abuse were reviewed against inspected runtime paths. The remaining accepted debt is operational:
production chaos drills, dashboards, parity scanners, and deployment proof are not closed by this
certification.

## Runtime Hardening Performed

- `PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs`: stored recovery snapshots now
  strip coupon, cart-promotion, item-promotion, total-discount, and discounted-line fields before
  response; `GrandTotal` is recomputed without stored discounts.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/AbandonedCart/AbandonedCartRecoverySecurityTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontPromotionsAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Required Answers

- Who owns promotion authority? Current tenant-owned PostgreSQL coupon, promotion, reservation,
  usage, smart-offer generated coupon, checkout, order, and payment lifecycle state owns it.
- Can coupons be forged? No on inspected paths; code/id/token evidence must resolve to current SQL
  coupon authority.
- Can coupon usage limits be bypassed? No inspected bypass remains; limits count confirmed usage
  plus active reservations under SQL transaction and row lock.
- Can discounts stack improperly? No; runtime chooses one winner and clears losing fields.
- Can smart offers create authority? No; they create protected coupon intent only.
- Can customers bypass eligibility? No inspected path allows anonymous or tenant-foreign customer
  coupon eligibility.
- Can expired promotions apply? No inspected checkout/payment confirmation path accepts expired
  coupons or promotions.
- Can discounts exceed allowed bounds? No; discount validation, caps, rounding, and subtotal/line
  bounds apply.
- Can checkout consume invalid promotions? No; checkout recomputes and payment confirmation
  revalidates current authority.
- Can abandoned checkout resurrect expired promotions? No after this hardening; stored recovery
  snapshots no longer restore discount authority.
- Can tenant leakage occur? No inspected tenant-foreign coupon, promotion, smart-offer, or recovery
  authority path remains.
- Can replay inflate discounts? No inspected replay path duplicates usage or benefit; SQL
  uniqueness/idempotency collapses duplicate execution.
- Were runtime code changes required? Yes, recovery snapshot discount stripping.
- Is Storefront Promotions Authority production-certifiable? Yes, for inspected runtime paths.
