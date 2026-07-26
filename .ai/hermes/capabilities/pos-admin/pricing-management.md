# Pricing Management — Shopixy POS Admin Capability (Implementation Reverse-Engineered)

> Source of truth: the running implementation in this repository (`PointOfSale.Core`,
> `PointOfSale.API`, `PointOfSale.Infrastructure`, `PointOfSale.CommerceHub`,
> `PointOfSale.Infrastructure` projection writers/consumers). Documentation that
> disagrees with the code below is flagged in "Documentation Validation".

---

## Purpose

Pricing Management owns the **authoritative price truth** for sellable goods and the
**deterministic, server-side calculation** of the amounts a customer is charged.

Business problem it solves:
- A product/variant must have one SQL-authoritative price that every channel derives from
  (no per-channel price drift).
- Discounts (coupons, automatic promotions / "offers", smart-offer popups) must be
  validated and computed where the truth lives (PostgreSQL), never on the client.
- The final checkout total must be reproducible and fail-closed: if discount authority
  cannot be trusted, the order is rejected (charged full price or aborted), never silently
  under-charged.

What it owns:
- Base prices stored on `Product` (legacy) and `ProductVariant` (authoritative sellable unit).
- Online/e-commerce price derivation (`OnlinePrice` with `SalePrice`/`Price` fallback).
- Cost/purchase price (informational; feeds margin/accounting, NOT customer pricing).
- The storefront promotion engine, coupon validation/authority, and the composed
  checkout pricing breakdown (subtotal, discounts, shipping, tax, payment fee, total).
- CommerceHub per-listing price overrides (a secondary, tenant-gated override on top of
  the variant effective price).
- Tax-rate resolution and shipping-cost calculation that combine with pricing at checkout.

---

## Business Scope

Reverse-engineered from entities, services, and schema.

Owned by Pricing:
- **Base Price (POS):** `ProductVariant.Price` — the POS sale price for the variant
  (`ProductVariant.cs:61`).
- **Base Price (legacy product):** `Product.SalePrice` (`Product.cs:61`) — retained for
  backward compatibility; simple products now route through the single default variant.
- **Online Price:** `ProductVariant.OnlinePrice` (nullable) and `Product.OnlinePrice`
  (nullable). If null or 0, falls back to the POS price (`Product.cs:67`,
  `ProductVariant.cs:67`).
- **Cost / Purchase Price:** `ProductVariant.CostPrice` (`ProductVariant.cs:72`) and
  `Product.PurchasePrice` (`Product.cs:55`). Informational — margin/accounting only.
  NOT used to compute customer-facing price.
- **Tax-inclusive flag:** `Product.PriceIncludesTax` (`Product.cs:72`). Product-level.
- **Tax rate:** `Product.TaxRate` (product-level percentage, e.g. 15 = 15%)
  (`Product.cs:77`). Used only as a fallback in `TaxCalculationService` tenant-setting path.
- **Effective Online Price (storefront):** computed, not stored on the SQL entity.
  `ProductVariant.EffectiveOnlinePrice => OnlinePrice.HasValue && >0 ? OnlinePrice : Price`
  (`ProductVariant.cs:191`). Mirrored in the Mongo `ProductVariantDocument.EffectivePrice`
  (`ProductVariantDocument.cs:102`).
- **Promotions ("Offers"):** `Promotion` entity (`Promotion.cs`) — automatic, code-free,
  scoped to Product / Category / Cart, priority + date + usage-gated.
- **Coupons:** `Coupon`, `CouponReservation`, `CouponUsage`, `PromoCode`, `PromoCodeUsage`
  (`Entities/`). Code-based, normalized case-insensitive, tenant-scoped.
- **Smart Offers:** `SmartOffer` entity (`SmartOffer.cs`) — marketing popup configured with
  an embedded `CouponCode` + `GeneratedCouponId`. A presentation/trigger wrapper around a
  coupon; SmartOffer itself carries no price — it issues/references a coupon.
- **CommerceHub Price Override:** `CommerceHubListing.CommerceHubPrice` — a per-listing
  override on top of the variant effective online price, gated by
  `CommerceHubPriceOverrideRuntimePolicy` (tenant allowlist/cohorts)
  (`CommerceHubPriceAuthority.cs:137`).
- **Checkout pricing breakdown:** `StorefrontPricingService` + `DiscountBreakdown`
  (`StorefrontPricingService.cs`, `Financial/DiscountBreakdown.cs`) — subtotal, coupon
  discount, promotion discount, manual discount, shipping, tax, payment fee, final total.
- **Tax resolution:** `TaxCalculationService` (`Pricing/TaxCalculationService.cs`).
- **Shipping cost:** `ShippingCalculationService` (`Pricing/ShippingCalculationService.cs`).

NOT owned by Pricing (authority boundaries, see below):
- Inventory / stock levels (owned by Inventory; pricing reads `TrackStock` only).
- Orders lifecycle & payment capture (Pricing feeds the amounts; Order/Payment own
  persistence and money movement).
- Accounting / ledger (reads final amounts; does not own journal entries).
- Affiliate commission calculation (derives from the order's recorded price; Pricing does
  not compute commission).
- Search ranking/visibility (Pricing writes price into projections; search owns ranking).
- CommerceHub listing lifecycle outside the price override field (CommerceHub module owns
  the listing; Pricing owns only the override value + resolution).

---

## AUTHORITY BOUNDARIES

Authoritative (PostgreSQL is the system of record):

| Concept | Authority | Notes |
|---|---|---|
| Base price / variant price | `ProductVariant` (PostgreSQL) | `Product.Price`/`SalePrice` legacy only |
| Online price | `ProductVariant`/`Product` (PostgreSQL) | Fallback logic is deterministic in code |
| Effective online price | Derived in code from SQL columns | `CommerceHubPriceAuthority.ResolveBaseVariantPrice` |
| Coupon validity / discount | `CouponValidationService` (PostgreSQL read) | Server-only; `StorefrontPromotionPriceComposer` is storefront-only helper |
| Promotion ("offer") eligibility | `PromotionEngine` reading `Promotions` (PostgreSQL) | Server-only |
| Checkout final total | `CheckoutService` + `StorefrontPricingService` | Fail-closed |
| Tax rate | PostgreSQL `SystemSettings` (`Tax.Enable`, `Tax.Rate`) → tenant country → config `CountryTaxRates` → `DefaultTaxRate` (`TaxCalculationService.cs:52`) | Resolution order is authoritative |
| Shipping cost | PostgreSQL `ShippingMethod` (`ShippingCalculationService`) | Free-shipping threshold honored |
| CommerceHub override | `CommerceHubPriceAuthority` + `CommerceHubPriceOverrideRuntimePolicy` | Only if enabled for tenant |
| Affiliate commission base | Order's recorded price (set at checkout) | Pricing does not compute commission |

Projection (derived; must never be trusted as source of truth):
- Mongo `ProductDocument` / `ProductVariantDocument` (price mirrored via
  `ProductSqlAuthorityProjectionWriter` from SQL).
- Elasticsearch product documents (price copied from Mongo/SQL for search/filter).
- CommerceHub read models (`CommerceHubProductDocument.Price`).

Evidence (what is actually validated server-side):
- Coupon: `CouponValidationService.ValidateCouponByIdAsync` / `ByCodeAsync` — tenant match,
  not deleted, active, date window, min order, usage caps (global + per-customer),
  category scope (`CategoryId != Guid.Empty` enforced so empty categories cannot bypass
  category-scoped coupons — `Product.cs:46`).
- Promotion: `PromotionEngine` re-validates every candidate via
  `DiscountValidation.GetPromotionValidationError` and respects `MaxUsage` capacity using
  confirmed usages + active reservation count from `Orders` (`PromotionEngine.cs:587`).

Presentation (UI only; must never be trusted):
- Cart/checkout client-side totals are recomputed server-side in `ReconcileCheckoutDiscountAuthorityAsync`.
- `StorefrontPromotionPriceComposer` is explicitly **storefront-only** and must NOT be used
  by affiliate or CommerceHub pricing (`StorefrontPromotionPriceComposer.cs:7`).

---

## OPERATIONAL LIFECYCLE

1. Operator edits a price in POS Admin (Product/Variant form →
   `ProductsController`/`ProductVariantsController`).
2. Validation: entity-level rules (weight non-negative, etc.) and
   `DiscountValidation` for any attached promotion/coupon.
3. Persistence: `ProductVariant` (and `Product` for legacy) rows committed to PostgreSQL
   with optimistic-concurrency `RowVersion` (xmin) and monotonic
   `ProjectionMutationVersion`.
4. Business rules: `Product.HasVariants` semantics — when true, product-level
   `Price`/`OnlinePrice`/`Barcode` must be NULL (commerce moves to variants)
   (`Product.cs:108`).
5. Outbox / events: product mutation raises `ProductUpdatedEvent`, and a price change can
   raise `ProductPriceChangedEvent { OldPrice, NewPrice }` (`ProductEvents.cs:211`).
6. Projection writers (consumers):
   - `ProductSqlAuthorityProjectionWriter` recomposes the full Mongo `ProductDocument`
     from SQL authority (rejects stale versions via `ProjectionMutationVersion`).
   - `ProductStockPriceSearchIndexConsumer` syncs price into Elasticsearch.
   - `ProductCacheInvalidationConsumer` invalidates Redis read caches.
   - `ProductVisibilityLifecycleProjectionConsumer` updates visibility/purchasability.
7. Downstream consumers read the projection:
   - Storefront (`StorefrontProductService`, `StorefrontProductsController`) computes
     `effectivePrice` from the **default variant's** `EffectivePrice`
     (`StorefrontProductService.cs:312`, `StorefrontProductsController.cs:519`).
   - CommerceHub listing price resolved via `CommerceHubPriceAuthority` (base variant price
     or override if enabled for tenant).
   - Affiliate views read published product price (no separate price authority).
   - Search filters by price from the synced index.
8. At checkout, `CheckoutService`:
   - Validates the coupon server-side (`CouponValidationService`) and writes
     authoritative discount onto the cart snapshot.
   - Runs `PromotionEngine.CalculateAsync` to pick the best single discount (offer or
     coupon — no stacking in Phase 1).
   - Calls `StorefrontPricingService.CalculateCheckoutAsync` to compose
     subtotal − discounts + shipping + tax + payment fee = `FinalTotal`.
   - Fails closed if promotion calculation cannot be trusted
     (`ApplyPromotionResultOrThrow` throws on exception; customer is charged full price or
     the order is rejected — never silently under-charged)
     (`CheckoutService.cs:4603`, comment at :4592).
9. Customer sees the authoritative total on the storefront/checkout.

---

## IMPLEMENTATION DISCOVERY

Frontend / Admin
- Product/Variant authoring forms (`PointOfSale.Web`) post to
  `ProductsController` / `ProductVariantsController`.
- NOTE: legacy `PRICING-SECTION-REMOVAL.md` describes a "ProductUnits" table holding prices.
  The implementation does NOT use a dedicated price table — prices live on `ProductVariant`
  (and legacy `Product`). "ProductUnit" exists but is a **multi-unit conversion** entity
  with its own `PurchasePrice`/`SellingPrice` for unit conversion, not the storefront price
  source. See Documentation Validation.

Controllers / Endpoints (pricing-relevant)
- `ProductsController`, `ProductVariantsController` — CRUD price fields.
- `PromotionsController` (`api/promotions`) — CRUD offers; `GET stats`,
  `POST sync-to-mongo`.
- `CouponsController` (`api/coupons`) — CRUD coupons; `POST validate`, `PATCH {id}/toggle`,
  `POST sync-to-mongo`.
- `AdminSmartOffersController`, `StorefrontSmartOffersController` — smart-offer popups.
- `AffiliatePromotionLinksController` — affiliate referral links (references products, not
  pricing logic).
- `StorefrontProductsController`, `StorefrontSearchController`, `StorefrontLandingPageController`
  — read effective price for display.

Application services
- `StorefrontPricingService` (`API/Services/Pricing`) — composes the checkout/cart pricing
  breakdown. Depends on `IShippingCalculationService` + `ITaxCalculationService`.
- `TaxCalculationService` — resolves tax rate with documented precedence.
- `ShippingCalculationService` — base + per-kg rate, free-shipping threshold → 0.
- `PromotionEngine` (`API/Services/Promotions`) — single unified engine for offers + coupon
  winner. Loads active offers, filters by scope/date/usage, picks highest discount (priority
  tiebreak), then `DetermineWinner` vs the pre-validated coupon (no stacking).
- `CouponValidationService` (`Infrastructure/Discounts`) — SQL-authoritative coupon
  validation, reservation (idempotent per order/customer), confirmation (emits
  `CouponConsumedEvent`), release. Capacity counted via `PromotionUsages` +
  `Orders` with active reservations.

Domain layer (`PointOfSale.Core`)
- `Product`, `ProductVariant` entities (price fields + computed `EffectiveOnlinePrice`).
- `Promotion` (offers; `CalculateDiscount`, `IsValid`, `AppliesToProduct`).
- `Coupon`, `CouponReservation`, `CouponUsage`, `PromoCode`, `PromoCodeUsage`.
- `SmartOffer` (popup config; validates embedded coupon).
- `Pricing/CommerceHubPriceAuthority` — override resolution + runtime policy.
- `Pricing/StorefrontPromotionPriceComposer` — storefront-only offer price helper.
- `Financial/DiscountBreakdown` — immutable coupon+promotion+manual discount aggregation.
- `Financial/FinancialCurrencyGuard`, `Financial/Iso4217CurrencyProvider` — currency safety.
- `Validation/DiscountValidation` — shared coupon/promotion rules.
- `Helpers/CurrencyHelper.Round` — 2-dp, `MidpointRounding.AwayFromZero` (system-wide
  rounding standard).

Repositories / DB tables (PostgreSQL)
- `Products` (`SalePrice numeric(18,2)`, `OnlinePrice numeric`, `PurchasePrice`,
  `PriceIncludesTax boolean`, `TaxRate numeric(5,2)`, `CategoryId` non-empty).
- `ProductVariants` (`Price numeric(18,2)`, `OnlinePrice numeric(18,2)`,
  `CostPrice numeric(18,2)`, plus `ProjectionMutationVersion`, `RowVersion`).
- `Promotions`, `Coupon`, `CouponReservations`, `CouponUsages`, `PromoCodes`,
  `PromoCodeUsages`, `SmartOffers`, `CommerceHubListings` (`CommerceHubPrice`).

Events / Outbox
- `ProductUpdatedEvent`, `ProductPriceChangedEvent`, `ProductCreatedEvent`,
  `CommerceHubPriceOverrideSaved/Updated/RemovedEvent`, `CouponConsumedEvent`.

Background jobs / Projection writers
- `ProductSqlAuthorityProjectionWriter` — recomposes Mongo `ProductDocument` from SQL,
  enforcing monotonic projection version (stale replay rejected).
- `ProductStockPriceSearchIndexConsumer`, `ProductCacheInvalidationConsumer`,
  `ProductVisibilityLifecycleProjectionConsumer`.

Mongo / Redis / Elasticsearch
- Mongo `ProductDocument` (`StorefrontEffectivePrice` resolved via
  `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice`) and
  `ProductVariantDocument.EffectivePrice`.
- Redis caches invalidated on product mutation.
- Elasticsearch price filter (`MinPrice`/`MaxPrice`) sourced from synced product docs.
- CommerceHub read model `CommerceHubProductDocument.Price`.

CommerceHub / Affiliate / Checkout integration
- CommerceHub: `CommerceHubCommandHandlers` resolve listing price via
  `CommerceHubPriceAuthority`; override published through price-override events.
- Affiliate: reads published product price; commission derived from recorded order price,
  not from the pricing service.
- Checkout: `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync` is the single
  authority that reconciles coupon + promotion and fails closed.

---

## BUSINESS RULES (implementation-backed)

Price storage:
- Variant price `Price` is the POS sale price; `OnlinePrice` overrides it for e-commerce
  when > 0; otherwise falls back (`ProductVariant.cs:191`).
- Legacy product `SalePrice` is POS; `OnlinePrice` overrides when > 0
  (`Product.cs:67`).
- Cost price is never a customer-facing input.
- When `Product.HasVariants == true`, product-level `Price`/`OnlinePrice`/`Barcode` MUST be
  NULL — enforced in entity/DTO/DB (`Product.cs:108`).
- Price columns are `NOT NULL` (numeric(18,2)) at DB level for variants.

Validation (shared, `DiscountValidation`):
- Discount value must be > 0.
- Percentage discount cannot exceed 100.
- Fixed discount capped at `MaxFixedDiscountValue = 1_000_000`.
- Min order amount, max discount amount, max usage must be non-negative.
- Start date cannot be after end date.
- For fixed amount: max-discount-amount cannot be less than the discount value.
- Coupon-only: max-usage-per-customer cannot exceed max-usage.
- Product `CategoryId` must not be `Guid.Empty` (prevents category-scoped coupon bypass)
  (`Product.cs:46`).

Coupon rules:
- Codes normalized uppercase; unique per `(TenantId, NormalizedCode)`.
- Server-only validation (tenant, active, date window, min order, global + per-customer
  usage caps, category scope).
- Reservation is idempotent per `(TenantId, OrderId)` and per
  `(TenantId, CouponId, CustomerId)`.
- Confirmation emits `CouponConsumedEvent` and updates `CurrentUsage`.

Promotion ("offer") rules:
- Auto-applied, code-free. Scope: Product / Category / Cart.
- Eligible only if `IsActive`, now within `[StartDate, EndDate]`, passes
  `DiscountValidation`, meets `MinOrderAmount`, and has `MaxUsage` capacity.
- Capacity = confirmed usages (`PromotionUsages`) + active reservations (`Orders` in
  non-terminal states) < `MaxUsage`.
- Among eligible, highest discount wins; tie broken by higher `Priority`
  (`PromotionEngine.cs:422`, `:461`).
- `MaxDiscountAmount` caps percentage discounts.

No stacking (Phase 1):
- Coupon and offer do NOT combine. `DetermineWinner` picks the single highest discount;
  on tie, the **coupon wins** (user explicitly applied it)
  (`PromotionEngine.cs:696`, `:710`).
- `CanCombineCouponWithOffersAsync` always returns `false` (`PromotionEngine.cs:231`).

Effective price / final total:
- `Promotion.CalculateDiscount` and `StorefrontPromotionPriceComposer.Compose` apply
  discount, cap at `MaxDiscountAmount`, clamp to `[0, basePrice]`, and round.
- `StorefrontPricingService.BuildBreakdownAsync`:
  `FinalTotal = Round(Subtotal − TotalDiscount + Shipping + Tax + PaymentFee)`
  (`StorefrontPricingService.cs:142`).
- `TotalDiscount = CouponDiscount + PromotionDiscount + ManualDiscount`
  (`DiscountBreakdown.cs:11`).
- All currency rounding is 2 dp, `MidpointRounding.AwayFromZero`
  (`CurrencyHelper.Round`, used everywhere).

Tax rules (`TaxCalculationService`):
- If subtotal ≤ 0 → tax 0, source `empty-subtotal`.
- Tenant `Tax.Enable=false` → tax 0, source `tenant-disabled`.
- Tenant `Tax.Rate` setting wins next (source `tenant-setting`); invalid values are
  logged and skipped.
- Else country rate from `StorefrontPricingOptions.CountryTaxRates` (source `country`).
- Else `DefaultTaxRate` (source `default`).
- Rates ≥ 1 are normalized to fractional (15 → 0.15) (`TaxCalculationService.cs:106`).

Shipping rules (`ShippingCalculationService`):
- If `subtotal >= FreeShippingThreshold` → 0.
- Else `max(0, BaseRate + PerKgRate * max(0, totalWeight))`.

CommerceHub override rules (`CommerceHubPriceAuthority`):
- Override accepted only if `CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant`
  (enabled globally AND tenant in allowlist; cohort filter if configured) AND override > 0.
- Accepted override replaces base variant effective online price
  (source `CommerceHubOverride`); otherwise falls back
  (source `ProductVariantEffectiveOnlinePrice`).
- If tenant not enabled, override is ignored and base price used.

Fail-closed:
- If promotion calculation throws (non-cancellation), checkout pricing fails closed:
  the exception is surfaced / order rejected; the customer is never charged a silently
  discounted price (`CheckoutService.cs:4592` + `:4620`).
- If coupon invalid at checkout, `CheckoutCartInvalidException` is thrown and a
  `CheckoutFailed` event emitted (`CheckoutService.cs:4462`).

---

## PRICE PRECEDENCE (actual implementation)

Storefront / effective price:
1. Variant `OnlinePrice` if set and > 0
2. else Variant `Price`
   (computed as `ProductVariant.EffectiveOnlinePrice`; same fallback for legacy `Product`).

CommerceHub listing price (`CommerceHubPriceAuthority.ResolveAsync`):
1. If `CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant(tenant)` AND override > 0
   → use override (`CommerceHubOverride`)
2. else base variant effective online price (`ProductVariantEffectiveOnlinePrice`)

Checkout discount (Phase 1, single winner — NO stacking):
1. Coupon validated server-side (`CouponValidationService`) → authoritative coupon discount
2. Promotion engine evaluates all eligible offers → best single offer discount
3. `DetermineWinner`: higher discount wins; tie → coupon wins
4. Result applied as either `CouponDiscount` or `PromotionDiscount` (not both summed)

Final checkout total:
1. Subtotal = Σ(item.CurrentPrice × qty)
2. − TotalDiscount (coupon XOR promotion + manual discount)
3. + Shipping (`ShippingCalculationService`)
4. + Tax (`TaxCalculationService`)
5. + Payment fee (`StorefrontPricingService.CalculatePaymentFee`)
6. = FinalTotal (rounded 2dp, AwayFromZero)

---

## CROSS-SYSTEM EFFECTS

Products:
- Price lives on `ProductVariant`; product price fields legacy. Changing a price updates
  SQL, raises events, and rebuilds the Mongo document. No change to inventory.

Inventory:
- Pricing reads `TrackStock` only. Price changes do NOT move stock. Stock changes do NOT
  change price.

Orders:
- Orders store the composed amounts (`Subtotal`, `DiscountAmount`, `PromotionDiscountAmount`,
  `ManualDiscountAmount`, `Shipping`, `Tax`, `FinalTotal`). Pricing sets these; Order owns
  the row.

Checkout:
- `CheckoutService` is the consumer of pricing authority. Recomputes everything server-side;
  fails closed on discount uncertainty.

Payments:
- Payment amount is derived from the order's `FinalTotal` (set by pricing). Pricing does not
  move money.

Storefront:
- Reads effective price from the default variant's `EffectivePrice`
  (`StorefrontProductService.cs:312`). Price changes reflected after projection + cache
  invalidation.

CommerceHub:
- Listing price = base variant price or override (if enabled). Override changes go through
  `CommerceHubCommandHandlers` + price-override events; reads come from
  `CommerceHubQueryService` / `CommerceHubProductDocument.Price`.

Affiliate:
- Affiliate views published product price; commission base is the recorded order price, not
  the pricing service. A price change can affect future commission amounts but pricing does
  not compute commission.

Search (Elasticsearch):
- Price synced from SQL→Mongo→ES via `ProductStockPriceSearchIndexConsumer`. Search filters
  by `MinPrice`/`MaxPrice` on the synced field. Stale ES price = projection lag, not a
  pricing bug.

Analytics:
- Reads recorded order amounts. No live pricing logic.

Accounting / Ledger:
- Reads final order amounts. Does not own pricing.

Notifications / Cache:
- Redis caches invalidated on product mutation (`ProductCacheInvalidationConsumer`).
- `CouponConsumedEvent` drives usage/notification side-effects.

---

## VALIDATION CHECKLIST

After changing a base (variant) price:
- SQL `ProductVariants.Price` updated; `ProjectionMutationVersion` advanced.
- `ProductUpdatedEvent` → Mongo `ProductDocument` recomposed; ES price synced; Redis cache
  invalidated.
- Storefront shows new effective price (after cache TTL / invalidation).
- CommerceHub listing price updates unless an override is active for that tenant.
- Open carts/checkouts recompute at next checkout (no stale lock-in).

After enabling a promotion:
- `Promotions` row `IsActive=true`, valid dates.
- Eligible carts get the offer via `PromotionEngine` at checkout (no code needed).
- Search may show a boost if `SearchBoostOverride` set (synced to ES).

After disabling a promotion:
- `IsActive=false` → engine filters it out; no discount applied.

After changing variant `OnlinePrice`:
- Effective online price flips to `OnlinePrice` if > 0, else falls back to `Price`.
- Storefront/CommerceHub effective price follows.

After changing `PurchasePrice`/`CostPrice`:
- Margin/accounting only; NO customer-facing price change.

After creating/updating a coupon:
- Normalized code unique per tenant; validation passes.
- Applied at checkout via `CouponValidationService`; reservation then confirmation.

After setting a CommerceHub price override:
- Only affects CommerceHub IF `CommerceHubPriceOverrideRuntimePolicy` enables that tenant.
- Otherwise ignored; base variant price used. Emit price-override event.

---

## COMMON FAILURE SCENARIOS

Wrong storefront price:
- Causes: stale Redis cache (not invalidated), stale Mongo projection (version regression),
  client showing product-level `SalePrice` instead of default variant `EffectivePrice`.
- Where to look first: `ProductCacheInvalidationConsumer`, `ProductSqlAuthorityProjectionWriter`
  version checks, `StorefrontProductService` effective-price selection.

Wrong checkout total:
- Causes: promotion calculation exception (fail-closed → full price), invalid coupon
  (exception), stale cart snapshot not reconciled.
- Where to look first: `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync`,
  `ApplyPromotionResultOrThrow`, `StorefrontPricingService.BuildBreakdownAsync`.

Promotion ignored:
- Causes: `IsActive=false`, date window, `MinOrderAmount` not met, `MaxUsage` exhausted
  (confirmed + active reservations), invalid `DiscountValidation`, lower discount than the
  chosen coupon.
- Where to look first: `PromotionEngine.CalculateBestOfferAsync`,
  `PrecomputeOfferUsageCountsAsync` capacity.

Coupon ignored / rejected:
- Causes: wrong tenant, deleted, inactive, date window, min order, usage caps, empty
  `CategoryId` bypass guard, missing server identity at checkout.
- Where to look first: `CouponValidationService.ValidateCouponByIdAsync`,
  `CouponErrorCodes`.

CommerceHub price mismatch:
- Causes: override enabled for tenant but not applied, or applied but tenant not in allowlist
  (base price used), or projection lag.
- Where to look first: `CommerceHubPriceAuthority.ResolveAsync`,
  `CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant`,
  `CommerceHubCommandHandlers` publish path.

Affiliate commission mismatch:
- Causes: commission derived from recorded order price, not live price; price changed after
  order placed. NOT a pricing-service bug.
- Where to look first: order row amounts, affiliate commission pipeline.

Search showing stale price:
- Causes: ES sync lag after SQL commit.
- Where to look first: `ProductStockPriceSearchIndexConsumer`, ES index, Mongo source doc.

Projection / cache stale:
- Causes: `ProjectionMutationVersion` regression rejected by writer, Redis not invalidated.
- Where to look first: `ProductSqlAuthorityProjectionWriter.ApplyAsync`,
  `ProductCacheInvalidationConsumer`.

Variant price mismatch:
- Causes: `HasVariants=true` but product-level price shown; or default-variant selection
  wrong in storefront service.
- Where to look first: `StorefrontProductService` default-variant resolution,
  `Product.HasVariants` invariant.

Currency mismatch:
- Causes: rounding differs from `MidpointRounding.AwayFromZero`; or tax rate not normalized
  (15 vs 0.15).
- Where to look first: `CurrencyHelper.Round`, `TaxCalculationService.CreateResult` (`:106`).

---

## DEBUGGING GUIDE

If a pricing bug arrives:
1. Start at SQL: confirm the authoritative row (`ProductVariants.Price`/`OnlinePrice`,
   `Products`, `Promotions`, `Coupons`). SQL is the truth.
2. Check pricing services: `StorefrontPricingService.BuildBreakdownAsync` for the total
   math; `CommerceHubPriceAuthority` for CommerceHub price.
3. Check promotions: `PromotionEngine.CalculateAsync` / `CalculateBestOfferAsync` — eligibility,
   capacity, winner selection.
4. Check coupons: `CouponValidationService` + `CouponErrorCodes` — validation, reservation,
   confirmation.
5. Check checkout: `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync` and
   `ApplyPromotionResultOrThrow` — fail-closed behavior, snapshot reconciliation.
6. Check projections: `ProductSqlAuthorityProjectionWriter` (version monotonicity),
   `ProductStockPriceSearchIndexConsumer` (ES), `ProductCacheInvalidationConsumer` (Redis).
7. Check cache: Redis entries for the product; confirm invalidation fired.
8. Check CommerceHub: only if the mismatch is on a CommerceHub listing and the tenant is in
   the override allowlist — `CommerceHubPriceOverrideRuntimePolicy`.
9. Check affiliate only if the complaint is about commission; remember commission uses the
   recorded order price, not the live pricing service.

Rule of thumb: any client-computed total is untrusted. The server recomputes at checkout.
If SQL, services, and projections agree, the UI is wrong (stale cache or wrong field).

---

## RELATED KNOWLEDGE

Core Guides:
- `UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md`
- `ECOMMERCE-ARCHITECTURE.md`
- `PHASE-11.1-PRODUCT-VARIANTS.md`, `PHASE-11.2-PRODUCT-VARIANTS-UI.md`

System Guides:
- `POS_ADMIN_SYSTEM_DEEP_REVIEW.md`
- `STOREFRONT-ADMIN-DEEP-AUDIT-REPORT.md`
- `CHECKOUT_DEBUGGING_SUMMARY.md`
- `COUPON_NORMALIZED_CODE_IMPLEMENTATION.md`

Related Capabilities:
- Product Management (owns catalog + variant authoring)
- Inventory Management (stock; pricing reads TrackStock only)
- Checkout & Payments (consumes pricing authority; fails closed)
- CommerceHub (listing price override consumer)
- Affiliate (commission derived from recorded order price)
- Search / Elasticsearch (price projection consumer)
- Tax & Shipping (combine with pricing at checkout)

Related Domains:
- `PointOfSale.Core/Pricing`
- `PointOfSale.Core/Discounts`
- `PointOfSale.Core/Financial`
- `PointOfSale.Core/Validation` (`DiscountValidation`)
- `PointOfSale.API/Services/Pricing`, `PointOfSale.API/Services/Promotions`,
  `PointOfSale.API/Services/Coupons`, `PointOfSale.API/Services/Checkout`

---

## DOCUMENTATION VALIDATION

Implementation-backed findings vs existing docs (implementation wins):

1. **MISSING / INCORRECT business rule — "ProductUnits" price table does not exist.**
   `PRICING-SECTION-REMOVAL.md` states "Actual prices are retrieved from the ProductUnits
   table" and that prices were moved "per unit" into a `ProductUnits` table. The
   implementation stores sellable prices on `ProductVariant` (`Price`, `OnlinePrice`,
   `CostPrice`). `ProductUnit` is a separate **multi-unit conversion** entity
   (`PurchasePrice`/`SellingPrice` for unit conversion) and is NOT the storefront price
   source. Documentation is wrong/stale.

2. **INCORRECT authority boundary — coupons/promotions are server-authoritative, not UI.**
   The removed "Pricing" section implied prices were edited per "unit" in the UI as the
   source of truth. Implementation: prices are SQL-authoritative; coupons/promotions are
   validated server-side in `CouponValidationService`/`PromotionEngine`; client totals are
   recomputed and fail closed at checkout.

3. **HIDDEN implementation behavior — CommerceHub price override is tenant-gated at runtime.**
   Not documented in product/pricing docs: `CommerceHubPriceOverrideRuntimePolicy`
   (global enable + tenant allowlist + cohort filter) decides whether an override is even
   applied. If disabled for the tenant, the override is silently ignored and the base
   variant price is used.

4. **HIDDEN implementation behavior — no coupon+offer stacking (Phase 1).**
   `DetermineWinner` picks a single highest discount; tie → coupon wins.
   `CanCombineCouponWithOffersAsync` always returns false. No documentation states this
   explicitly for operators.

5. **HIDDEN implementation behavior — fail-closed checkout pricing.**
   If `PromotionEngine` throws (non-cancellation), the order is rejected / charged full
   price; discounts are never silently dropped. Not surfaced in product/pricing docs.

6. **HIDDEN implementation behavior — tax rate normalization.**
   `TaxCalculationService` normalizes rates ≥ 1 to fractional (15 → 0.15) and falls back in
   a strict order (tenant setting → country → default). Not documented for operators.

7. **AUTHORITY inconsistency — `StorefrontPromotionPriceComposer` is storefront-only and
   must not be used by affiliate/CommerceHub** (`StorefrontPromotionPriceComposer.cs:7`).
   Any reuse elsewhere would violate pricing authority. No doc warns about this.

8. **OUTDATED — legacy `Product.SalePrice`/`PurchasePrice` retained "for backward
   compatibility"** per entity comments, but the removal doc claims new products set them to
   0 and they are "unused." They remain valid fallback fields for simple (non-variant)
   products; the "unused" statement is inaccurate for the legacy/simple path.

---

## OUTPUT CONFIRMATIONS

- Summary of discoveries:
  Pricing authority for Shopixy lives in PostgreSQL on `ProductVariant` (with legacy
  `Product` fields). The storefront effective price is `OnlinePrice ?? Price`; CommerceHub
  may apply a runtime-gated override. Discounts (coupons + automatic promotions) are
  validated and chosen server-side by a single `PromotionEngine` with NO stacking in Phase 1
  (single highest discount wins; tie → coupon). Checkout recomputes the full breakdown
  (subtotal − discounts + shipping + tax + payment fee) and fails closed on uncertainty.
  Price changes propagate SQL → events → Mongo/ES/Redis projections.

- Business rules discovered:
  OnlinePrice overrides Price when > 0; cost price is non-customer; HasVariants forces
  product-level price NULL; CategoryId must be non-empty (coupon bypass guard); discount
  value > 0, percentage ≤ 100, fixed ≤ 1,000,000; coupon codes normalized uppercase unique
  per tenant; promotion eligibility = active + date + min-order + usage capacity; all
  rounding 2dp AwayFromZero; tax resolution order tenant→country→default with ≥1
  normalization; shipping free-threshold → 0; CommerceHub override tenant-gated.

- Pricing precedence discovered:
  Effective price = OnlinePrice(>0) ?? Price. CommerceHub = override(if enabled & >0) ??
  base. Checkout discount = best of (coupon, offer), no stack, tie→coupon. FinalTotal =
  subtotal − totalDiscount + shipping + tax + paymentFee.

- Cross-system dependencies discovered:
  Products (SQL truth), Inventory (reads TrackStock only), Orders (stores amounts), Checkout
  (consumes authority, fails closed), Payments (derive from FinalTotal), Storefront (default
  variant EffectivePrice), CommerceHub (override consumer), Affiliate (commission from
  recorded order price), Search (ES price sync), Analytics/Accounting (read recorded amounts),
  Redis (cache invalidation), Mongo/ES (projections).

- Documentation inconsistencies found:
  "ProductUnits" price table claim is false (prices on ProductVariant; ProductUnit is
  multi-unit conversion only); "prices unused for new products" inaccurate for simple
  products; missing docs on CommerceHub runtime-gated override, no-stacking, fail-closed
  checkout, tax normalization, and StorefrontPromotionPriceComposer authority restriction.

- Hidden implementation behavior found:
  CommerceHub override runtime policy (tenant allowlist/cohorts), no coupon+offer stacking,
  fail-closed promotion calculation, tax rate ≥1 normalization, PromotionEngine capacity via
  confirmed usages + active order reservations, idempotent coupon reservation per
  order/customer, `StorefrontPromotionPriceComposer` storefront-only restriction.

- Confirmation that only this file was created:
  Only `C:\Users\fadya\source\repos\PointOfSale\.ai\hermes\capabilities\pos-admin\pricing-management.md`
  was created. No other file was added.

- Confirmation that no other files were modified:
  No existing files were edited, renamed, or deleted. The working tree is unchanged except
  for the new capability document above.

- Confirmation that no memory writes occurred:
  No memory tool calls were made; no durable memory was written during this task.
