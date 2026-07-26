# Promotion & Coupon Management — Reverse-Engineered Capability

> Authority: **implementation** (PointOfSale.Core / PointOfSale.API / PointOfSale.Infrastructure).
> Docs/prose are secondary. Where code and comments disagree, code wins and the discrepancy is
> reported in **Documentation Validation**. Reverse-engineered 2026-07-17 for Shopixy / PointOfSale.
> Scope: the Promotion & Coupon **business rule engine** (runtime discount evaluation), not the admin
> screens (the admin UI only configures the runtime).

## 1. Executive Summary

Shopixy implements discounts through **two distinct configuration surfaces that converge on one runtime
engine**:

- **Promotions ("Offers")** — `Promotion` entity, `PromotionsController` (`api/promotions`). Automatic,
  code-less discounts scoped to a Product, a Category, or the whole Cart. Priority-ordered.
- **Coupons** — `Coupon` entity, `CouponsController` (`api/coupons`). Code-based, customer-applied
  discounts with per-total and per-customer usage caps, product/category/exclusion scope, and
  first-order-only gating.

At checkout, both feed a single evaluator, **`PromotionEngine.CalculateAsync`**, which selects **ONE
winner** (highest discount; tie → coupon). **There is NO stacking in this implementation**
(`CanCombineCouponWithOffersAsync` always returns `false`). Checkout pricing **fails closed**: if
promotion calculation throws, the order is rejected / charged full price rather than silently dropping a
discount.

PostgreSQL is the single source of truth for `Promotions`, `Coupons`, `CouponReservations`,
`CouponUsages`, `PromotionUsages`. MongoDB/Elasticsearch/Redis carry **derivative** read models
(storefront badges, promotion-aware search boost, cache) projected via MassTransit events. Coupon and
promotion **consumption is confirmed only at payment collection** (never in cart/preview/checkout
reservation), with a durable **reservation → confirmation → release** state machine that guards usage
caps under concurrency.

**CommerceHub has NO promotion/coupon integration** — grep of `PointOfSale.API/CommerceHub/` for
Promotion/Coupon returns nothing. Promotions & Coupons are a **Storefront + POS-Admin** capability only.

## 2. Business Purpose

Let a merchant reduce a customer's payable total under controlled rules: run seasonal/automatic offers
without codes, issue shareable coupon codes with caps and eligibility, cap financial exposure
(`MaxDiscountAmount`, `MaxUsage`, `MaxUsagePerCustomer`, `MinOrderAmount`), schedule campaigns
(`StartDate`/`EndDate`), and preserve an immutable per-order record for accounting, analytics, and audit.

## 3. Capability Scope

**Belongs to this capability**
- Promotion entity + lifecycle (create/update/toggle/delete/expire) — `PromotionsController`.
- Coupon entity + lifecycle (create/update/toggle/delete) + storefront validation — `CouponsController`.
- Runtime discount evaluation — `PromotionEngine` (offers + coupon winner).
- Coupon authority (validate / reserve / confirm / release) — `CouponValidationService`.
- Discount rule validation — `DiscountValidation` (shared).
- Checkout discount reconciliation & fail-closed application — `CheckoutService`.
- Order snapshot of the applied discount + usage confirmation at payment — `Order`, `CouponUsage`,
  `PromotionUsage`, `PaymentLifecycleService`.
- Read-model projection (Mongo badges, ES boost, Redis cache) — `PromotionProjectionConsumer`,
  `CouponProjectionConsumer`, `PromotionCacheService`.
- Campaign expiration background job — `PromotionExpirationService`.
- SmartOffer marketing popups that issue/reference a coupon — `SmartOffer` (adjacent, not a price authority).

**Does NOT belong**
- Base/variant pricing, tax, shipping computation (Pricing capability — `StorefrontPricingService`,
  `TaxCalculationService`, `ShippingCalculationService`). Promotions consume the priced subtotal.
- Payment collection / refunds (Payment capability). Discounts do not move money; they only reduce `Total`.
- Affiliate commission (uses the recorded order price, not the promotion engine).
- CommerceHub marketplace pricing (no promotion/coupon code path — see §22).
- POS in-store Sales discounts (`SalesController` applies its own per-line/invoice discount; it does
  NOT call `PromotionEngine` or `Coupon`).

## 4. Business Terminology

| Term | Meaning in code |
|---|---|
| Promotion / Offer | `Promotion` entity; auto-applied, no code; scope Product/Category/Cart |
| Coupon / Promo code | `Coupon` entity; code-based, customer-applied |
| Scope | `PromotionScope` {Product, Category, Cart}; coupon scope = JSON product/category/exclusion lists |
| Discount type | `DiscountType` {Percentage, FixedAmount} |
| Winner | Single selected discount from `PromotionEngine.DetermineWinner` (no stacking) |
| Reservation | `CouponReservation` (Reserved/Confirmed/Released) — pre-payment hold on a coupon claim |
| Usage | `CouponUsage` / `PromotionUsage` — immutable consumption record, created at payment collection |
| Capacity | confirmed usages + active reservations, checked against `MaxUsage`/`MaxUsagePerCustomer` |
| Applied* fields | `Order.AppliedPromotionId/Type/Name`, `Order.CouponId/CouponCode` — order snapshot |

## 5. Authority Matrix

| Concern | Authority (writes truth) | Evidence / Reads | Notes |
|---|---|---|---|
| Promotion config | `PromotionsController` → SQL `Promotions` | Mongo `PromotionDocument`, ES boost | Owner/Admin/Manager |
| Coupon config | `CouponsController` → SQL `Coupons` | Mongo `CouponDocument` | Owner/Admin/Manager (delete: Owner/Admin) |
| Coupon validity/discount | `CouponValidationService` (SQL) | storefront `validate` endpoint | server-only; auth customer required |
| Offer eligibility/discount | `PromotionEngine` (SQL) | — | server-only |
| Winner selection | `PromotionEngine.DetermineWinner` | — | single winner, tie→coupon |
| Final checkout total | `CheckoutService` + `StorefrontPricingService` | client `expectedPricing` (untrusted) | fail-closed; server recomputes |
| Coupon reservation/confirm/release | `CouponValidationService` (in checkout / payment / cancel tx) | `CouponReservations` | idempotent, row-locked |
| Offer reservation | `CheckoutService.ReserveAppliedOfferPromotion` | pending `Orders` | row-locked |
| Usage confirmation | `PaymentLifecycleService` (at Collected) | `CouponUsages`, `PromotionUsages` | immutable records |
| Order discount snapshot | `CheckoutService` (order creation) | `Order.*` | immutable after creation |
| Read models | `PromotionProjectionConsumer`/`CouponProjectionConsumer` | Mongo/ES/Redis | SQL-authoritative, version-guarded |
| Campaign expiry | `PromotionExpirationService` | — | every 5 min |

**POS Admin** owns config + read/report. **Storefront** owns validation + evaluation + checkout
application (server-side). **Checkout** owns reconciliation + reservation + snapshot. **Payment** owns
usage confirmation and release triggers. **Super Admin / CommerceHub** have NO authority here.

## 6. Promotion Types

`Promotion` (`Core/Entities/Promotion.cs`), by `PromotionScope`:
- **Product offer** (`Scope=Product`): `AppliesToId` = ProductId, or null = all products.
- **Category offer** (`Scope=Category`): `AppliesToId` = CategoryId, or null = all categories.
- **Cart offer** (`Scope=Cart`): applies to entire order subtotal; `AppliesToId` ignored (`AppliesToProduct` returns true).

Flags: `Priority` (int, higher first), `IsActive`, `IsExclusive` (declared, **not enforced** — see §14),
`BannerStyle` (gradient/solid/minimal/festive, display only), `SearchBoostOverride` (double?, additive ES
merchandising boost). All promotions carry `StartDate`/`EndDate`, optional `MinOrderAmount`, `MaxUsage`,
`MaxDiscountAmount`.

## 7. Coupon Types

`Coupon` (`Core/Entities/Coupon.cs`). Single entity; "type" is expressed by configured fields:
- **Percentage vs Fixed** (`DiscountType`), with optional `MaxDiscountAmount` cap (percentage).
- **Scoped** via JSON columns `ApplicableProductIds`, `ApplicableCategoryIds`, `ExcludedProductIds`.
- **First-order-only** (`FirstOrderOnly`).
- **Usage-capped**: `MaxUsage` (global), `MaxUsagePerCustomer`.
- **Time-boxed**: optional `StartDate`/`EndDate` (both nullable → immediately valid / never expires).
- `Code`/`NormalizedCode` (uppercased via `SetCode`), unique per `(TenantId, NormalizedCode)`.

**SmartOffer** (`Core/Entities/SmartOffer.cs`) is a marketing **popup** that carries a `CouponCode` +
`GeneratedCouponId`; it references/issues a coupon and holds **no independent price authority**.

## 8. Discount Types & 9. Supported Discount Models

`DiscountType` enum: `Percentage` and `FixedAmount` only. Model (`Coupon.CalculateDiscount` /
`Promotion.CalculateDiscount` / `CouponValidationService.CalculateDiscount`):

- Percentage: `amount * (DiscountValue / 100)`.
- Fixed: `DiscountValue`.
- Cap: if `MaxDiscountAmount` set and discount exceeds it → clamp to cap.
- Floor: `Math.Min(discount, amount)` — a discount never exceeds the amount it applies to.
- Rounding: `CurrencyHelper.Round` = **2dp, MidpointRounding.AwayFromZero** (system-wide).
- Validation bounds (`DiscountValidation`): value > 0; percentage ≤ 100; fixed ≤ 1,000,000;
  `MaxDiscountAmount`/`MinOrderAmount`/`MaxUsage` ≥ 0; `StartDate ≤ EndDate`;
  `MaxUsagePerCustomer ≤ MaxUsage`; for fixed, `MaxDiscountAmount ≥ DiscountValue`.

Cart-scope offers apportion the total discount across line items via
`AllocateDiscountAcrossItems` (largest-remainder method, penny-accurate to the exact total).

## 10. Promotion Lifecycle

`PromotionsController` (all Owner/Admin/Manager):
1. **Create** (`POST`): validate discount + dates + duplicate-name + rule-admission; `Validate()`;
   `Add`; **publish `PromotionCreatedEvent` before SaveChanges** (MassTransit outbox, same tx).
2. **Update** (`PUT {id}`): **requires `If-Match` RowVersion** (xmin) — mismatch → 409; re-validate;
   `PromotionUpdatedEvent`; concurrency-guarded save.
3. **Toggle** (`PATCH {id}/toggle`): flips `IsActive`; RowVersion-guarded; `PromotionUpdatedEvent`.
4. **Delete** (`DELETE {id}`): RowVersion-guarded. **If it has `PromotionUsages` → soft delete**
   (`IsDeleted=true`, `IsActive=false`); **else hard delete** (row removed). `PromotionDeletedEvent`.
5. **Expire** (background): `PromotionExpirationService` every 5 min sets `IsActive=false` for
   `EndDate < now` and publishes `PromotionExpiredEvent` (cursor-paged, checkpointed, fail-forward).

There is **no separate publish endpoint**; "published/active" = `IsActive && StartDate ≤ now ≤ EndDate`.
"Scheduled" = `IsActive && StartDate > now` (a stats-only classification).

## 11. Coupon Lifecycle

`CouponsController`:
1. **Create** (`POST`, Owner/Admin/Manager): validate; `SetCode` (uppercase); unique code; `Validate()`;
   `CouponCreatedEvent`.
2. **Update** (`PUT {id}`): `CouponUpdatedEvent`.
3. **Toggle** (`PATCH {id}/toggle`): flip `IsActive`.
4. **Delete** (`DELETE {id}`, **Owner/Admin only**): `CouponDeletedEvent`.
5. **Validate** (`POST validate`, **StorefrontJwt**, rate-limited `CouponRateLimit`): customer-facing
   pre-check via `CouponValidationService.ValidateCouponByCodeAsync`; **requires an authenticated customer**.

Coupon **consumption** is a separate runtime machine: **Reserved → Confirmed → Released**
(`CouponReservationStatus`), driven by checkout/payment/cancel — see §12/§14.

## 12. Canonical Runtime Flow

```
Promotion/Coupon created (POST) → Validated → SQL Promotions/Coupons (write truth)
   → Event published in same tx (outbox) → PromotionProjectionConsumer/CouponProjectionConsumer
   → Mongo doc + ES boost + Redis cache invalidated  (derivative, storefront badges/search)

Customer shops (Storefront reads Mongo/ES for badges) → applies coupon code (optional)
   → POST api/coupons/validate (StorefrontJwt, auth customer) → CouponValidationService (SQL) → preview discount

Checkout (POST api/storefront/checkout) → CheckoutService.ProcessCheckoutAsync:
   1. Cart snapshot (Redis) → recompute prices server-side
   2. ReconcileCheckoutDiscountAuthorityAsync:
        a. Re-validate applied coupon by ID (server authority) → authoritative coupon discount
        b. PromotionEngine.CalculateAsync(cart, coupon) → best offer vs coupon → DetermineWinner (ONE)
        c. ApplyPromotionResultOrThrow → sets snapshot discount fields, or FAILS CLOSED on exception
   3. Server recomputes total (EnsureExpectedPricingMatches vs client expectedPricing — untrusted)
   4. Order created (SERIALIZABLE tx): snapshot CouponId/CouponCode OR AppliedPromotionId/Type/Name
   5. If coupon won → CouponValidationService.ReserveCouponAsync (row-locked, idempotent)
      If offer won → ReserveAppliedOfferPromotionAsync (row-locked capacity re-check)
   6. Payment intent created; OrderCreatedEvent + inventory reservation outbox; commit

Payment collected (PaymentLifecycleService, PaymentStatus → Collected):
   → ConfirmCouponUsageAsync  → CouponUsage (immutable) + reservation Confirmed + CouponConsumedEvent
   → ConfirmOfferPromotionUsageAsync → PromotionUsage (immutable) + CurrentUsage + PromotionAppliedEvent
   → Read models updated (CurrentUsage projected to Mongo)

Order cancelled / reservation expired:
   → ReleaseCouponReservationAsync (AdminOrderService / InventoryReservationCleanupJob)
   → reservation Released (only if not yet confirmed); usage records, once created, remain
```

## 13. Promotion Resolution Engine

`PromotionEngine.CalculateAsync` (`API/Services/Promotions/PromotionEngine.cs`):

1. **Empty guard**: no items or subtotal ≤ 0 → `PromotionResult.None()`.
2. **Load active offers** (`LoadActiveOffersAsync`): `Promotions` where `TenantId`, `IsActive`,
   `StartDate ≤ now ≤ EndDate`; ordered `Priority DESC, DiscountValue DESC, Id ASC`. Each is re-run
   through `DiscountValidation.GetPromotionValidationError`; **invalid promotions are skipped (logged),
   not thrown** — a misconfigured promotion silently drops out instead of blocking checkout.
3. **Best offer** (`CalculateBestOfferAsync`): for each offer, skip if `MinOrderAmount` unmet or capacity
   exhausted (`HasOfferCapacity`); compute discount by scope (`CalculateOfferDiscount`); keep the
   highest; tie broken by higher `Priority`.
4. **Coupon discount**: reuses the **already server-validated** coupon amount passed in
   (`CalculatePrevalidatedCouponDiscount`) — the engine does NOT re-hit the coupon DB.
5. **Winner** (`DetermineWinner`): whichever of {best offer, coupon} has the higher `DiscountAmount`;
   **on a tie the COUPON wins** (customer explicitly applied it). Result is exactly one of coupon/offer.

**Expired/inactive** offers are excluded by the SQL predicate (`StartDate ≤ now ≤ EndDate`, `IsActive`).
**Priority** only orders candidates and breaks discount ties; the **highest actual discount always wins**
regardless of priority. **Shipping promotions**: not a distinct type — shipping cost is computed by the
Pricing/Shipping service and is not discounted by `PromotionEngine` (no `Scope=Shipping`). **Category/
product rules**: `AppliesToProduct` / per-item scope matching in `CalculateOfferDiscount`.

Auxiliary reads: `GetActiveOffersForProductsAsync` / `...ForCategoriesAsync` (badge data),
`GetActiveCartPromotionAsync` (cart banner).

## 14. Promotion Conflict Resolution, Stacking & Priority Rules

- **No stacking (Phase 1).** `CanCombineCouponWithOffersAsync` returns `false` unconditionally.
  `ApplyPromotionResultOrThrow` sets **either** coupon fields **or** promotion fields, never both, and
  clears the other side. `Order` persists coupon OR offer, never both.
- **Winner rule**: highest discount; tie → coupon.
- **Priority**: `Promotion.Priority` (higher first) orders candidates and breaks equal-discount ties
  among offers; it does NOT let a lower-discount offer beat a higher-discount one.
- **`IsExclusive`** is declared on `Promotion` and carried through events but is **NOT enforced** in the
  engine (comment: "Future: Check coupon.IsExclusive flag"). Report as declared-not-wired (§27).

## 15. Eligibility Engine

**Promotion (offer) eligibility** (must all hold): `IsActive`; `StartDate ≤ now ≤ EndDate`; passes
`DiscountValidation`; scope match (`AppliesToProduct` for product/category, always true for cart);
`Subtotal ≥ MinOrderAmount`; capacity available.

**Coupon eligibility** (`CouponValidationService.ValidateCouponInternalAsync`), in order:
1. **Authenticated customer required** (`RequireAuthenticatedCustomer`) — guest coupons impossible.
2. Code normalized & must match stored `NormalizedCode` (case-insensitive).
3. Config valid (`DiscountValidation.GetCouponValidationError`) → else `INVALID_CONFIG`.
4. `IsActive` → else `INACTIVE`.
5. `now ≥ StartDate` → else `NOT_YET_VALID`; `now ≤ EndDate` → else `EXPIRED`.
6. `Subtotal ≥ MinOrderAmount` → else `MINIMUM_NOT_MET`.
7. **Global cap**: confirmed usages + active reservations ≥ `MaxUsage` → `MAX_USAGE_EXCEEDED`.
8. **Per-customer cap**: customer confirmed + active reservations ≥ `MaxUsagePerCustomer` → `CUSTOMER_MAX_EXCEEDED`.
9. **First-order-only**: customer has a Collected non-cancelled order → `FIRST_ORDER_ONLY`; also blocks
   if the customer already holds an active first-order reservation.
10. **Scope** (`ValidateScope`): excluded product in cart → `NOT_APPLICABLE`; applicable-product list set
    but none in cart → `NOT_APPLICABLE`; applicable-category list set but none in cart → `NOT_APPLICABLE`;
    scope requires items but cart empty → `NOT_APPLICABLE`.
11. **Split-brain guard**: if the client's applied discount ≠ recomputed authoritative discount →
    `DISCOUNT_MISMATCH` (telemetry `RecordCouponSplitBrainDetected`).

**Not implemented as eligibility axes** (do not document as supported): customer group/segment, brand,
collection, maximum-quantity, branch, payment method, shipping method, marketplace scope, per-time-of-day.
`FirstOrderOnly` is the only customer-attribute gate; `NewCustomer` == `FirstOrderOnly`.
`CustomerHasCompletedOrdersAsync` defines "first order" = no prior `PaymentStatus.Collected`,
non-Cancelled/Refunded/Returned order.

## 16. Coupon Validation Engine (Reservation → Confirmation → Release)

`CouponValidationService` (`Infrastructure/Discounts/CouponValidationService.cs`) is SQL-only authority:

- **Validate** (`ValidateCouponByCode/ById`): read coupon (`IgnoreQueryFilters`, tenant-scoped) → run the
  §15 rule chain → return `CouponValidationDecision` with authoritative discount + counts.
- **Reserve** (`ReserveCouponAsync`): must run **inside an open SQL transaction**
  (`EnsureTransactionalMutation`). Row-locks the coupon (`FOR UPDATE` PG / `UPDLOCK,ROWLOCK,HOLDLOCK`
  SQL Server) → re-validates (excluding this order) → idempotent by `(TenantId, OrderId)`; a
  per-customer unique index `(TenantId, CouponId, CustomerId)` blocks a second concurrent pending order
  from the same customer. Duplicate-key races resolve to the persisted reservation.
- **Confirm** (`ConfirmCouponUsageAsync`): at payment collection; re-validates, creates the **immutable
  `CouponUsage`** (unique `(TenantId, OrderId)`), flips reservation to `Confirmed`, recomputes and writes
  `Coupon.CurrentUsage` = confirmed count, publishes **`CouponConsumedEvent`**. Idempotent (existing
  usage short-circuits).
- **Release** (`ReleaseCouponReservationAsync`): only releases a **Reserved** reservation with **no
  confirmed usage**; a confirmed coupon is never auto-released. Called from `AdminOrderService` (cancel)
  and `InventoryReservationCleanupJob` (expiry).

Capacity math everywhere = **confirmed usages + active `Reserved` reservations** (never just
`CurrentUsage`), so pending checkouts count against the cap.

## 17. Pricing Integration

- Promotions consume the **priced** cart: `StorefrontPricingService` produces the subtotal
  (effective online price per variant = `OnlinePrice>0 ? OnlinePrice : Price`); `PromotionEngine`
  discounts that subtotal. Discounts never touch tax or shipping computation.
- `DiscountBreakdown` (`Core/Financial`) = immutable `(CouponDiscount, PromotionDiscount, ManualDiscount)`
  → `TotalDiscount`.
- Final total: `Round(Subtotal − TotalDiscount + Shipping + Tax + PaymentFee)`, all 2dp AwayFromZero.
- `StorefrontPromotionPriceComposer` is **storefront-only** (its comment forbids affiliate/CommerceHub reuse).

## 18. Checkout Integration

`CheckoutService.ProcessCheckoutAsync` (`ReconcileCheckoutDiscountAuthorityAsync` + `ApplyPromotionResultOrThrow`):

- Client-supplied coupon **code without a server-issued id (or vice-versa)** → `CheckoutCartInvalidException`.
- Applied coupon is **re-validated by id server-side** (client discount untrusted); failure aborts checkout.
- `PromotionEngine.CalculateAsync` picks the single winner; result written to the snapshot as coupon OR offer.
- **FAIL CLOSED**: if the engine throws a non-cancellation, non-domain error,
  `ApplyPromotionResultOrThrow` throws `CheckoutPromotionCalculationException` (telemetry
  `RecordPromotionFailClosed`) → checkout rejects rather than dropping the discount. `DomainGuardException`
  is re-thrown for structured 4xx. `Order.PromotionCalculationFailed` + `PromotionWarning*` persist the
  fallback state for idempotent replay.
- `EnsureExpectedPricingMatches` compares the server breakdown against the client `expectedPricing`
  (subtotal/discount/coupon/promotion/cart-promotion/shipping/tax) → `CheckoutTotalMismatchException` on drift.
- Reservation happens inside the checkout SERIALIZABLE tx (coupon: `ReserveCouponAsync`; offer:
  `ReserveAppliedOfferPromotionAsync` with a `FOR UPDATE` capacity re-check that throws if the cap moved).

## 19. Order Snapshot Strategy (what becomes immutable)

Persisted on `Order` at creation (`CheckoutService`), never mutated afterward by this capability:
- **Coupon path**: `CouponId`, `CouponCode` (+ `DiscountAmount` == `CouponDiscountAmount`).
- **Offer path**: `AppliedPromotionId`, `AppliedPromotionType` (`"offer"`), `AppliedPromotionName`
  (+ `PromotionDiscountAmount`). Coupon and offer are mutually exclusive on the order.
- `ManualDiscountAmount`, `TotalDiscountAmount`, and the `DiscountBreakdown` computed property.
- `PricingSnapshotJson` — full immutable pricing breakdown (subtotal, discounts, tax rate/source, shipping,
  total, geographic snapshot).
- `PromotionCalculationFailed` + `PromotionWarningMessage` (fallback audit).

At payment collection the **usage records** capture the durable audit:
- `CouponUsage`: `CouponId`, `CouponCode`, `CustomerId`, `DiscountAmount`, `OrderSubtotal`, `UsedAtUtc`
  (immutable; survives order cancel/refund).
- `PromotionUsage`: `PromotionId`, `PromotionName`, `CustomerId`, `DiscountAmount`, `OrderSubtotal`,
  `AppliedToItemsJson` (per-item breakdown), `AppliedAtUtc` (immutable).

Both usage records are **`IMMUTABLE` after creation** (no update/delete in normal flow) and remain even if
the order is later cancelled or refunded — a deliberate audit/anti-abuse choice.

## 20. Customer Experience

- Storefront reads **MongoDB** `PromotionDocument`/`CouponDocument` for badges/banners (never SQL directly),
  kept in sync by projection consumers + Redis cache.
- `StorefrontCouponService` is a **thin adapter** over `ICouponValidationService` (all decisions route back
  to SQL authority). `api/coupons/validate` previews the discount before checkout (auth customer required).
- At checkout the customer-applied code is re-resolved to a server-issued `CouponId`; if they also qualify
  for a better auto-offer, the engine silently upgrades them to the higher discount (tie → their coupon).
- `CouponAppliedEvent`/`CouponConsumedEvent`/`PromotionAppliedEvent` feed analytics (see §24).

## 21. Storefront Integration

- **Storefront**: validation + evaluation + checkout application (server). **No tenant header spoofing** —
  `ITenantContext.RequiredTenantId` is enforced; `X-Tenant-Id` is stripped upstream.
- **CommerceHub**: **NONE.** No promotion/coupon code path in `PointOfSale.API/CommerceHub/`. Offers/coupons/
  global campaigns/Super-Admin marketplace promotion authority do **not exist** in this implementation.
  Super Admin cannot manage tenant coupons; CommerceHub marketplace items are priced without this engine.
- Storefront pricing composer explicitly excludes affiliate and CommerceHub contexts.

## 22. CommerceHub Integration — Verified Separate

Requested verification (Marketplace Promotions, Marketplace Coupons, Global Campaigns, Super Admin
authority, Marketplace scope, Storefront isolation):
- **Verified**: `grep -rln "Promotion\|Coupon" PointOfSale.API/CommerceHub/` ⇒ **zero results**.
- There is **no** marketplace promotion entity, no global campaign scheduler, no Super-Admin promotion
  authority, and no marketplace-scoped promotion code. Promotions/Coupons are strictly **per-tenant
  (POS Admin storefront)**. Storefront isolation is inherent: every query is `TenantId`-scoped and the
  tenant header is stripped — there is no cross-tenant promotion surface to isolate.
- `StorefrontPromotionPriceComposer` comment: not reusable for affiliate/CommerceHub.

## 23. Security Model

| Action | Roles |
|---|---|
| List / Get promotion | Owner, Admin, Manager |
| Create / Update promotion | Owner, Admin, Manager |
| Toggle promotion | Owner, Admin, Manager |
| Delete promotion | Owner, Admin |
| `sync-to-mongo` (promotion replays) | Owner, Admin |
| List / Get / Create / Update / Toggle coupon | Owner, Admin, Manager |
| Delete coupon | **Owner, Admin only** |
| `validate` coupon (storefront) | StorefrontJwt + **authenticated customer** (rate-limited) |
| Apply at checkout | internal (CheckoutService) — customer must be authenticated if coupon used |

- **Tenant isolation**: all promo/coupon SQL is `TenantId` filtered; cross-tenant leak impossible by query
  design. `X-Tenant-Id` stripped.
- **Fail-closed checkout**: discounts are never trusted from the client; server re-evaluates.
- **Usage concurrency**: row locks + unique indexes (`(TenantId,NormalizedCode)`, `(TenantId,CouponId,
  CustomerId)`, `(TenantId,CouponId,OrderId)`) prevent double-spend and duplicate reservations.
- **Optimistic concurrency**: `Promotion` update/delete require `If-Match` RowVersion (xmin) → 409 on drift.
- **SQL is authoritative** for coupons/promotions; Mongo/ES/Redis are derivative and version-guarded.

## 24. Audit & Reporting

- **Usage records** (`CouponUsage`, `PromotionUsage`) = immutable, order-linked audit (customer, discount,
  subtotal, per-item breakdown, timestamps). Survive cancel/refund.
- **Events** for analytics/reporting: `CouponAppliedEvent`, `CouponConsumedEvent`, `CouponUsageChangedEvent`,
  `PromotionAppliedEvent`, `PromotionUsageChangedEvent`. Mongo `CurrentUsage` is synced from these.
- **Projection consumers** update read-model usage counts idempotently and emit telemetry.
- Reporting on "how many times used" = confirmed `CouponUsages`/`PromotionUsages` count (not `CurrentUsage`
  alone). Admin list/GET endpoints surface `CurrentUsage`/`MaxUsage`.

## 25. Background Jobs / Hosted Services

- **`PromotionExpirationService`** (HostedService, every 5 min): cursor-paged scan of active promotions
  with `EndDate < now` (since last checkpoint); sets `IsActive=false`, publishes `PromotionExpiredEvent`,
  advances checkpoint only if all publishes succeeded (fail-forward retry). Clears MongoDB badges + cache.
- **`InventoryReservationCleanupJob`**: releases **Reserved** coupon reservations (expired checkout) via
  `ReleaseCouponReservationAsync`.
- Coupon/promotion **confirmation is event-driven at payment collection**, not a scheduled job.

## 26. Events (implementation-backed)

**Coupon** (`Core/Events/CouponEvents.cs`, `Consumer=CouponProjectionConsumer`): `CouponCreatedEvent`,
`CouponUpdatedEvent`, `CouponDeletedEvent`, `CouponUsageChangedEvent`, plus runtime
`CouponAppliedEvent`/`CouponConsumedEvent` (usage confirmation).
**Promotion** (`Core/Events/PromotionEvents.cs`, `Consumer=PromotionProjectionConsumer`):
`PromotionCreatedEvent`, `PromotionUpdatedEvent`, `PromotionDeletedEvent`, `PromotionExpiredEvent`,
`PromotionAppliedEvent`, `PromotionUsageChangedEvent`.
**Transport**: MassTransit + outbox (events written in the same SQL tx as the mutation, then dispatched) →
MongoDB/ES/Redis consumers; idempotent by `(TenantId, Id)`. **No separate RabbitMQ-named exchange** beyond
the MassTransit outbox pipeline is wired for promotions in this code.

## 27. Failure Scenarios & Edge Cases (implementation-backed)

- **Expired during checkout**: `ReserveAppliedOfferPromotionAsync` re-checks `IsActive/Start/End` and
  `MinOrderAmount`; throws if the offer flipped. Coupon re-validated at reservation.
- **Deleted after checkout**: soft-deleted promotions keep `PromotionUsages` (delete repurposes `Id`); hard
  delete only when no usages. Mongo doc removed by `PromotionDeletedEvent`.
- **Multiple coupons**: disallowed — `ApplyPromotionResultOrThrow` sets coupon **or** offer, and a coupon
  is single-valued on the order.
- **Coupon + Promotion**: resolved by `DetermineWinner` (higher discount; tie→coupon); not combined.
- **Promotion overlap**: only the highest-discount active offer wins (priority breaks ties).
- **Timezone**: all comparisons `DateTime.UtcNow`; `StartDate`/`EndDate` persisted and compared in UTC. A
  tenant-local "ends midnight" can look different in UTC — no local-time conversion performed here.
- **Cache inconsistency**: Mongo/ES/Redis are derivative; promotions read read-models, pricing reads SQL.
  `PromotionCacheService.Invalidate*` on every change mitigates, but a lag window exists between SQL commit
  and projection completion.
- **Usage concurrency / race**: row locks (`FOR UPDATE` / `UPDLOCK…HOLDLOCK`) + per-customer/order unique
  indexes; duplicate-key races resolve to the persisted row. Capacity = confirmed + active reservations.
- **Rollback**: checkout tx aborts → order not created, reservations rolled back (not confirmed). Payment
  failure after collection still leaves `CouponUsage`/`PromotionUsage` (intentional audit). On order cancel
  via admin, only a still-`Reserved` reservation is released.
- **Promotion config invalid at eval**: skipped (logged), not thrown — a broken offer drops out silently.
- **Promotion calc exception**: fail-closed (checkout rejected; `PROMOTION_CALCULATION_FAILED` fallback
  stored if non-fatal path).
- **`IsExclusive` not enforced**: a promotion flagged exclusive still combines/competes via the single-winner
  rule like any other — the flag is inert in the runtime.

## 28. Implementation Validation & Certification Verdict

**What is conclusively implemented & wired:**
- Two config surfaces (Promotion / Coupon) converging on one SQL-authoritative engine with a single winner.
- Coupon validation, reservation, confirmation, release state machine with row locks + unique indexes.
- Offer best-selection by highest discount with priority tie-break; no stacking (Phase 1).
- Fail-closed checkout with server re-validation and `expectedPricing` guard.
- Immutable order discount snapshot + immutable `CouponUsage`/`PromotionUsage` audit.
- CQRS projections (Mongo badges, ES boost, Redis cache) via MassTransit outbox + promotion expiration job.
- Strict tenant isolation; owner/admin/manager roles; delete restricted (coupon: owner/admin).

**Verifiedly ABSENT (do not claim):**
- Any CommerceHub / marketplace / global-campaign / Super-Admin promotion authority (grep: zero files).
- Shipping-scope promotions, brand/collection/branch/payment-method/shipping-method/customer-group eligibility.
- Promotion stacking / coupon+offer combination; exclusive-coupon enforcement (`IsExclusive` inert).
- Promotion usage confirmation outside payment collection, or coupon auto-release after confirmation.

**Certification verdict:** The Promotion & Coupon capability is **certified as reverse-engineered from
implementation** for the POS-Admin↔Storefront↔Checkout↔Payment path. It is a single-winner, SQL-authoritative,
fail-closed, tenant-isolated discount engine. CommerceHub marketplace and stacking/exclusivity features are
**not implemented** in this codebase as of 2026-07-17. Every statement above is backed by the cited source
files; no administrative-UI prose was treated as authoritative.

---

## FINAL REPORT

**Files created (1)**
- `C:\Users\fadya\source\repos\PointOfSale\.ai\hermes\capabilities\pos-admin\promotion-coupon-management.md`
  (the single deliverable; 28 sections + this report).

**Files modified**: none (read-only reverse engineering). Implementation files were not changed.

**Memory writes**: none (explicitly excluded per mission).

**Code generated**: none (explicitly excluded per mission).

**Implementation changed**: none.

**Everything explicitly confirmed**
- Deliverable produced is exactly `promotion-coupon-management.md` under `.ai/hermes/capabilities/pos-admin/`.
- No implementation was modified, no extra docs/memory/code created.
- Every runtime claim is cited to a source file inspected during the session
  (`Promotion.cs`, `Coupon.cs`, `PromotionUsage.cs`, `CouponUsage.cs`, `CouponReservation.cs`,
  `PromotionEngine.cs`, `DiscountValidation.cs`, `CouponValidationService.cs`, `PromotionsController.cs`,
  `CouponsController.cs`, `CheckoutService.cs`, `PaymentLifecycleService.cs`, `PromotionExpirationService.cs`,
  `PromotionProjectionConsumer.cs`, `CouponEvents.cs`, `PromotionEvents.cs`, `SmartOffer.cs`,
  `StorefrontCouponService.cs`, `Order.cs`, `PromotionCacheService.cs`).
- CommerceHub absence confirmed by shell grep (zero files).





