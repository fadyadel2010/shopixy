# CERT-1 B2 Checkout Validation Certification

Certification: CERT-1  
Phase: B  
Control: B2 - Checkout Validation Certification  
Scope: Storefront Checkout Validation  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

Checkout validation is implemented as a server-side admission and authority verification workflow. The implemented checkout path validates request context, customer or guest ownership, cart authority, storefront status, product and variant salability, coupon authority, shipping method authority, payment method authority, pricing totals, payment amount limits, currency, inventory availability, idempotency, concurrency, and final order admission.

The certification is issued with observations because checkout-time shipping validation proves tenant ownership, active status, carrier configuration, and recalculates cost, but the inspected final checkout validator does not enforce `ShippingMethodDocument.AvailableCountries` or a pickup-specific validation branch. No architectural violations were identified.

## Executive Summary

Checkout Validation owns storefront checkout admission. It begins at `PointOfSale.API/Controllers/CheckoutController.cs` when `POST api/storefront/checkout` receives a request and validates tenant, customer or session headers, request shape, address data, payment method input, and expected pricing syntax before calling `ICheckoutService.ProcessCheckoutAsync`.

Checkout Validation validates:

- Tenant and caller ownership for authenticated customer checkout and guest session checkout.
- Cart existence, cart ownership, cart emptiness, product and variant authority, salability, and price freshness.
- Storefront order availability from SQL store configuration.
- Coupon validity, customer eligibility, product or category scope, usage capacity, discount math, and transactional reservation.
- Shipping method authority, active state, non-deleted state, enabled carrier configuration where applicable, and server-side shipping recalculation.
- Payment method authority, enabled state, configuration validity, server-side payment fee, and min/max amount limits.
- Server-side pricing totals, discounts, tax, shipping, payment fee, and expected total match.
- Inventory availability and reservation under transactional and row-locking controls.
- Idempotency, duplicate active-order protection, checkout locking, and optimistic price/cart concurrency boundaries.

Checkout Validation ends when `CheckoutService.CreateOrderAsync` admits the order by persisting the order, order items, inventory reservations, coupon/offer reservations, idempotency result, and transactional payment intent authority inside the database transaction. Payment provider execution and provider API calls are outside this certification scope.

Final authority is server-side implementation authority: `CheckoutService`, SQL reads and writes through `AppDbContext`, `IProductVariantStockRepository`, coupon authority services, shipping/payment authority queries, and pricing services. Client totals, client cart contents, and client-selected authority data are not final authority.

## Implemented Validation Pipeline

The implemented validation pipeline is:

1. Checkout request is received by `CheckoutController.Checkout`.
2. Tenant/customer/session headers are validated by `CheckoutController.ValidateHeadersAsync` and `ValidateRequestContextAsync`.
3. Request DTO is validated by `CheckoutRequestDto.Validate`.
4. Caller ownership is validated by `CheckoutService.ValidateCheckoutCallerOwnershipAsync`.
5. Pre-cart idempotency and request-hash checks are performed by the checkout idempotency coordinator.
6. Storefront order acceptance is validated by `CheckoutService.EnsureStorefrontAcceptingOrdersAsync`.
7. A checkout execution lock is acquired through `ICheckoutExecutionLockManager`.
8. The cart snapshot is loaded from the tenant/owner Redis cart key by `CheckoutService.GetCartSnapshotAsync`.
9. Cart tenant, owner, cart id, and non-empty item state are validated.
10. Persisted coupon state is reconciled from `StorefrontCartCouponStates`.
11. Duplicate active-order and snapshot-hash idempotency controls are applied.
12. Phone risk and optional geographic address admission are validated.
13. Product, variant, category lineage, salability, and price authority are reloaded from SQL by `CheckoutService.RecalculatePricesAsync`.
14. Coupon and promotion authority are reconciled by `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync`, `ICouponValidationService`, and the promotion engine.
15. Shipping method authority is validated by `CheckoutService.ValidateShippingMethodAsync`.
16. Payment method authority is validated by `CheckoutService.ValidatePaymentMethodAsync`.
17. Manual wallet proof fields are validated when the selected payment method requires them.
18. Checkout currency is validated by `CheckoutService.EnsureCheckoutCurrencyAsync`.
19. Pricing is recalculated by `StorefrontPricingService.CalculateCheckoutAsync`.
20. Payment method min/max amount limits are validated by `CheckoutService.ValidatePaymentMethodOrderAmountLimits`.
21. Conditional phone OTP verification is enforced when required.
22. `CheckoutService.CreateOrderAsync` repeats authority-sensitive validation inside a serializable transaction.
23. Inventory is validated and reserved by `CheckoutService.ValidateAndReserveInventoryAsync` and `ProductVariantStockRepository.ReserveAsync`.
24. Expected client pricing is compared to authoritative pricing by `CheckoutService.EnsureExpectedPricingMatches`.
25. Order admission, reservations, payment intent authority, idempotency result, and outbox records are persisted.
26. Provider payment session creation occurs only after order admission and is outside Checkout Validation.

Evidence: `CheckoutController`, `CheckoutRequestDto`, `CheckoutService.ProcessCheckoutAsync`, `CheckoutService.CreateOrderAsync`, `StorefrontPricingService`, `CouponValidationService`, `ProductVariantStockRepository`, `PaymentMethodConfigurationValidator`.

## Customer Validation

Guest checkout is implemented. If no authenticated customer is resolved, `CheckoutController.ValidateRequestContextAsync` requires `X-Session-Id`, and `CheckoutService.ValidateCheckoutCallerOwnershipAsync` requires the cart owner type to be `session` and the owner id to match the session id.

Authenticated checkout is implemented. The controller resolves authenticated customers from JWT first. `X-Customer-Id` is accepted only when it matches the authenticated customer or comes from a trusted internal service path. `CheckoutService.ValidateCheckoutCallerOwnershipAsync` reloads the customer from `OnlineCustomers`, requires tenant match, requires `IsActive`, and requires the cart owner type/id to match the customer.

Tenant validation is implemented at the controller and service boundary. `CheckoutController` requires tenant context, and `CheckoutService` applies tenant-scoped guards and tenant-scoped SQL queries for customer, cart, product, shipping, payment, coupon, storefront, and order admission.

Customer ownership is implemented for both customer carts and guest session carts. A mismatch raises tenant/ownership errors before pricing or order admission.

Session validation is implemented for guest checkout through `X-Session-Id` and cart owner matching. The checkout request does not accept cart owner fields from the client.

Recovery validation is implemented as request and snapshot idempotency, not as a separate customer recovery model. The checkout path uses deterministic request hashes, cart snapshot hashes, persistent idempotency keys, duplicate active-order checks, and idempotency result replay controls.

Evidence: `CheckoutController.ValidateRequestContextAsync`, `CheckoutController.ResolveAuthorizedCustomerHeader`, `CheckoutService.ValidateCheckoutCallerOwnershipAsync`, `CheckoutService.ProcessCheckoutAsync`, `CheckoutRequestIdempotencyCoordinatorTests`, `CheckoutControllerIdempotencyHeaderTests`.

## Cart Validation

Empty cart validation is implemented. `CheckoutService.GetCartSnapshotAsync` rejects missing carts and rejects carts with no items.

Cart ownership is implemented. The Redis cart snapshot must match the request cart id, tenant id, owner type, and owner id resolved by the controller and service.

Deleted, disabled, hidden, unpublished, inactive, and stale product/variant states are validated against SQL authority during checkout recalculation. `CheckoutService.RecalculatePricesInternalAsync` loads variants and products from SQL and rejects:

- Missing variants.
- Product/variant mismatches.
- Inactive products.
- Products not visible online.
- Products not published to the online storefront.
- Dead category lineage.
- Inactive variants.

Quantity validation is primarily owned by cart mutation and cart revalidation services. `CartService` caps item quantity, cart line count, and total quantity, and `CartDto` add/update request models constrain quantity ranges. Checkout does not accept line items or quantities from the checkout DTO; it consumes the server cart snapshot and inventory validation later enforces available quantity.

Price freshness is enforced at checkout. If authoritative SQL product/variant prices differ from the cart snapshot beyond the implemented tolerance, `CheckoutService.RecalculatePricesAsync` raises `CheckoutPriceChangedException` and blocks admission.

Evidence: `CheckoutService.GetCartSnapshotAsync`, `CheckoutService.RecalculatePricesAsync`, `CheckoutService.RecalculatePricesInternalAsync`, `CartService`, `CartDtos`, `StorefrontCartController`, `StorefrontCartCouponRevalidationTests`, `StorefrontCheckoutAuthorityCertificationTests`.

## Inventory Validation

Available stock validation is implemented. `CheckoutService.ValidateAndReserveInventoryAsync` collects variant ids from the server cart snapshot, reads authoritative available quantities through `IProductVariantStockRepository.GetAvailableQuantitiesAsync`, reloads product/variant authority from SQL, and rejects insufficient available inventory unless the product is not stock-tracked or backorder is allowed.

Reservation validation is implemented. `ProductVariantStockRepository.ReserveAsync` requires positive quantity, verifies current inventory lineage, locks stock rows, verifies total available quantity, increments reserved quantity, writes an `InventoryReservation`, and verifies reserved quantity does not exceed physical quantity.

Overselling prevention is implemented by row-locking stock rows and checking `Quantity - ReservedQuantity` before reservation. The repository rejects reservations that would result in negative availability or reserved quantity greater than quantity.

Inventory authority is SQL inventory stock ownership. `ProductVariantStockRepository.GetAvailableQuantitiesAsync` groups current inventory stock owner rows and returns zero for variants that are not current for inventory.

Inventory snapshot is not trusted from the browser. Checkout inventory validation reads stock authority at order admission time and reserves inside the order transaction.

Concurrent validation is implemented by deterministic checkout locking, serializable order admission, stock row locks, inventory reservations, and reservation binding to order items.

Evidence: `CheckoutService.ValidateAndReserveInventoryAsync`, `ProductVariantStockRepository.GetAvailableQuantitiesAsync`, `ProductVariantStockRepository.ReserveAsync`, `ProductVariantStock`, `InventoryReservationLifecycleIntegrationTests`, `CheckoutConcurrencyHardeningSourceTests`.

## Shipping Validation

Shipping method validation is implemented. `CheckoutService.ValidateShippingMethodAsync` loads the selected shipping method from SQL by tenant and id, excludes deleted rows, and rejects missing or inactive methods.

Shipping availability is partially implemented at final checkout authority. For non-manual carrier methods, checkout validates an enabled tenant carrier configuration. Storefront selection services also use `ShippingMethodService` and Mongo read models for available method reads, with SQL fallback.

Shipping zone/country validation was not identified in the final checkout validator. `ShippingMethodDocument` contains `AvailableCountries`, but the inspected `CheckoutService.ValidateShippingMethodAsync` path does not enforce that field against the checkout shipping address country.

Shipping recalculation is implemented. `StorefrontPricingService.CalculateCheckoutAsync` calls `ShippingCalculationService.Calculate`, which derives shipping from authoritative method rates and free-shipping threshold, not from a client-supplied shipping amount.

Pickup handling was not identified as a distinct checkout validation branch. Fulfillment mode is derived from carrier code classification, while shipment orchestration is deferred after order creation.

Shipping authority is the server-side shipping method authority loaded by checkout from SQL, mapped to `ShippingMethodDocument`, and recalculated by pricing services. The Mongo shipping read model supports storefront read/selection flows but is not the final admission authority in checkout.

Evidence: `CheckoutService.ValidateShippingMethodAsync`, `CheckoutService.LoadShippingMethodAuthorityAsync`, `StorefrontPricingService.CalculateCheckoutAsync`, `ShippingCalculationService`, `ShippingMethodService`, `ShippingMethodReadRepository`, `ShippingMethodDocument`, `ShippingMethodFinalAuthorityCertificationTests`, `ShippingMethodSelectionAuthorityCertificationTests`.

## Coupon Validation

Coupon ownership and authentication are implemented. `CouponValidationService` requires an authenticated customer before coupon validation.

Expiration validation is implemented. Coupon validation checks start and end dates through `DiscountValidation.GetCouponValidationError` and coupon authority fields.

Usage limits are implemented. `CouponValidationService` evaluates confirmed coupon usages plus active reservations against global usage limits and per-customer limits.

Minimum order validation is implemented. Coupon validation checks `MinOrderAmount` against authoritative subtotal.

Maximum order validation was not identified as a coupon-specific rule in the inspected coupon authority path. Discount caps are implemented through maximum discount amount logic.

Product and category restrictions are implemented. Coupon scope JSON is evaluated against cart product ids and category ids, including excluded product ids.

Customer restrictions are implemented through authenticated customer requirement, per-customer usage limits, and first-order-only validation.

Duplicate usage prevention is implemented through confirmed usage checks, active reservation checks, and transactional coupon reservation with unique constraint handling.

Coupon discount tampering prevention is implemented. `CouponValidationService` recalculates the discount and rejects a mismatch against the applied discount amount.

Evidence: `CouponValidationService.ValidateCouponByCodeAsync`, `CouponValidationService.ValidateCouponByIdAsync`, `CouponValidationService.ValidateCouponInternalAsync`, `CouponValidationService.ReserveCouponAsync`, `CouponAuthorityModels`, `Coupon`, `CouponCustomerValidationRules`, `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync`, `CheckoutCouponCqrsConsistencyTests`, `CouponUsageConcurrencyIntegrationTests`.

## Payment Validation

Payment method authority is implemented. `CheckoutService.ValidatePaymentMethodAsync` normalizes the requested code, loads the tenant payment method from SQL by tenant/code, excludes deleted rows, and rejects missing or disabled methods.

Configuration validity is implemented. `CheckoutService.EvaluatePaymentMethodConfiguration` decrypts encrypted payment method configuration and validates it with `PaymentMethodConfigurationValidator`. Invalid or undecryptable configuration blocks checkout payment method authority.

Amount limits are implemented. `CheckoutService.ValidatePaymentMethodOrderAmountLimits` validates authoritative final total against `TenantPaymentMethod.MinOrderAmount` and `MaxOrderAmount`.

Payment fee validation is implemented as server-side calculation. `StorefrontPricingService.CalculateCheckoutAsync` calculates payment fee from the selected payment method's fee configuration and includes it in final total authority.

Manual wallet validation is implemented at the DTO and service layers. `CheckoutRequestDto.Validate` validates wallet type, wallet phone, and proof key format for manual wallet methods, and `CheckoutService` performs manual wallet proof validation before order admission.

Runtime boundary is preserved. Checkout validation does not directly call Stripe, Paymob, or Fawry provider APIs in the validation path. Provider session creation is delegated after order admission to payment services/orchestration and is outside this certification scope.

Evidence: `CheckoutService.ValidatePaymentMethodAsync`, `CheckoutService.LoadPaymentMethodAuthorityAsync`, `CheckoutService.EvaluatePaymentMethodConfiguration`, `CheckoutService.ValidatePaymentMethodOrderAmountLimits`, `PaymentMethodConfigurationValidator`, `TenantPaymentMethod`, `StorefrontPricingService.CalculateCheckoutAsync`, `StorefrontPaymentMethodSelectionAuthorityCertificationTests`, `PaymentMethodAuthorityCertificationTests`.

## Pricing Validation

Price authority is SQL product and variant pricing. `CheckoutService.RecalculatePricesInternalAsync` reloads product and variant price authority and updates cart item current price/cost/category from server data.

Discount recalculation is implemented. Coupon discount, promotion item discount, cart-level promotion discount, and manual discount components are produced by server-side coupon and promotion authority paths.

Shipping recalculation is implemented by `ShippingCalculationService` from the authoritative selected shipping method.

Tax calculation is implemented by `TaxCalculationService`, which uses tenant tax settings and country/default fallback rules.

Payment fee calculation is implemented by `StorefrontPricingService.CalculateCheckoutAsync` from the authoritative selected payment method.

Coupon recalculation is implemented by `CouponValidationService` and `CheckoutService.ReconcileCheckoutDiscountAuthorityAsync`.

Final total calculation is implemented by `StorefrontPricingService.CalculateCheckoutAsync` as:

`subtotal - totalDiscount + shipping + tax + paymentFee`

Price tampering prevention is implemented. `CheckoutExpectedPricingDto` accepts client expected totals, but `CheckoutService.EnsureExpectedPricingMatches` compares those values to authoritative server-calculated subtotal, coupon discount, item promotion discount, cart promotion discount, manual discount, shipping, tax, payment fee, and total. Any mismatch blocks admission.

Evidence: `CheckoutService.RecalculatePricesAsync`, `CheckoutService.RecalculatePricesInternalAsync`, `CheckoutService.EnsureExpectedPricingMatches`, `StorefrontPricingService.CalculateCheckoutAsync`, `ShippingCalculationService`, `TaxCalculationService`, `CheckoutExpectedPricingDto`, `StorefrontPricingAuthorityTests`, `StorefrontCheckoutPricingTotalsAuthorityCertificationTests`.

## Business Rule Validation

The following implemented business rules were identified:

- Cannot checkout without a valid tenant context.
- Cannot checkout without a valid cart id.
- Cannot checkout without a valid payment method.
- Cannot checkout without a valid shipping method id.
- Cannot checkout without a syntactically valid shipping address.
- Cannot checkout without expected pricing.
- Cannot checkout as a guest without a session id.
- Cannot checkout as an authenticated customer if the customer does not exist, belongs to another tenant, is inactive, or does not own the cart.
- Cannot checkout a cart that is missing, belongs to a different tenant/owner, or is empty.
- Cannot checkout while the storefront is not accepting orders.
- Cannot bypass checkout execution locking and idempotency controls for duplicate admission.
- Cannot checkout products or variants that are missing, mismatched, inactive, hidden from online storefront, unpublished, or in dead category lineage.
- Cannot checkout stale cart prices; price changes block admission.
- Cannot apply a coupon without authenticated customer authority.
- Cannot apply a deleted, inactive, not-yet-started, expired, overused, out-of-scope, first-order-ineligible, or discount-mismatched coupon.
- Cannot bypass promotion calculation failures; promotion authority fails closed.
- Cannot use a missing, deleted, inactive, or carrier-unconfigured shipping method.
- Cannot use a missing, deleted, disabled, undecryptable, or configuration-invalid payment method.
- Cannot place an order below or above the selected payment method amount limits.
- Cannot bypass server-side shipping, tax, discount, payment fee, and final total calculation.
- Cannot submit client expected totals that differ from server-calculated totals.
- Cannot exceed authoritative inventory unless stock tracking/backorder policy permits it.
- Cannot reserve inventory outside the reservation repository's row-lock and availability controls.
- Cannot complete order admission without transactional persistence of order, items, inventory reservation binding, coupon/offer reservation, idempotency result, and outbox records.

Evidence: `CheckoutController`, `CheckoutRequestDto`, `CheckoutService`, `CouponValidationService`, `StorefrontPricingService`, `ProductVariantStockRepository`, `PaymentMethodConfigurationValidator`.

## DTO Certification

Client-supplied checkout values include `CartId`, `ShippingMethodId`, `ShippingAddress`, optional `BillingAddress`, `PaymentMethod`, optional `CouponCode`, manual wallet proof fields, `CustomerNotes`, required `ExpectedPricing`, and optional affiliate referral code.

Server-recalculated values include cart items from Redis, product and variant prices from SQL, coupon discount, promotion discounts, shipping amount, tax amount, payment fee, final total, currency authority, inventory availability, and reservation state.

Ignored or non-authoritative client values include all expected pricing amounts for purposes of calculation authority. They are used only as a client acknowledgement/mismatch detector by `EnsureExpectedPricingMatches`.

Trusted values are not taken from the browser for final authority. Final authority comes from resolved tenant/customer/session context, server cart snapshot, SQL product/variant data, SQL shipping method data, SQL payment method data, SQL coupon authority, pricing services, and inventory repository reservation results.

Evidence: `CheckoutRequestDto`, `CheckoutExpectedPricingDto`, `CheckoutController.ValidateHeadersAsync`, `CheckoutService.GetCartSnapshotAsync`, `CheckoutService.RecalculatePricesAsync`, `CheckoutService.EnsureExpectedPricingMatches`.

## Runtime Boundary

Checkout Validation does not own payment provider execution. It does not directly execute Stripe, Paymob, or Fawry provider calls in the validation and order admission path.

Checkout Validation owns:

- Request/context validation.
- Customer/session/cart ownership validation.
- Storefront order admission checks.
- Cart/product/variant authority checks.
- Coupon and promotion authority checks.
- Shipping and payment method authority checks.
- Pricing verification.
- Inventory validation and reservation.
- Order admission and transactional authority persistence.

Checkout Validation does not own:

- Provider API execution.
- Provider credentials lifecycle.
- Payment settlement.
- Refunds.
- Payment runtime adapter behavior.
- CommerceHub marketplace flow certification.
- Post-admission shipment orchestration.

After order admission, provider session creation is delegated to payment services/orchestration. That downstream runtime is outside B2 Checkout Validation.

Evidence: `CheckoutService.ProcessCheckoutAsync`, `CheckoutService.CreateOrderAsync`, `CheckoutService.TryCreateTransactionalPaymentIntentAuthorityAsync`, `CheckoutService.CreateCheckoutPaymentSessionAsync`, `IPaymentService`, `IPaymentOrchestrationService`.

## Critical Invariants

- Server recalculates totals; browser totals are never calculation authority.
- Checkout DTO expected pricing is an assertion checked against server authority, not trusted input.
- Cart contents come from the server cart snapshot, not checkout request line items.
- Product and variant salability is revalidated from SQL during checkout.
- Price changes block checkout admission.
- Inventory is authoritative at reservation time.
- Inventory reservation is performed with repository-level locking and availability checks.
- Payment method is revalidated from tenant SQL authority.
- Payment method configuration validity is required before admission.
- Shipping method is revalidated from tenant SQL authority.
- Coupon authority is SQL-backed and transactionally reserved.
- Storefront live status is checked before and inside order admission.
- Idempotency and checkout locks protect duplicate/concurrent admission.
- Provider runtime execution begins after checkout validation and order admission.

Evidence: `CheckoutService`, `StorefrontPricingService`, `CouponValidationService`, `ProductVariantStockRepository`, `PaymentMethodConfigurationValidator`, `CheckoutExecutionLockManagerTests`, `CheckoutCartIdempotencySnapshotBuilderTests`.

## Architecture Boundaries

Checkout Validation owns validation, admission, business rules, pricing verification, authority verification, inventory reservation, coupon/offer reservation, and order admission.

Checkout Validation does not own payment runtime, provider calls, settlement, refunds, CommerceHub, marketplace flows, or post-admission shipment orchestration.

Mongo read models are used for storefront read/selection flows such as shipping method availability through `ShippingMethodReadRepository` and `ShippingMethodDocument`. Final checkout admission authority for shipping, payment, product, coupon, storefront, order, and inventory is server-side authority loaded and persisted through checkout services, SQL reads/writes, and repositories.

Evidence: `CheckoutService`, `ShippingMethodService`, `ShippingMethodReadRepository`, `ShippingMethodDocument`, `AppDbContext`, `ProductVariantStockRepository`.

## Architecture Risks

No architectural violations were identified.

## Implementation Observations

- Shipping country/zone enforcement was not identified in the final checkout validator, although `ShippingMethodDocument.AvailableCountries` exists in the read model.
- Pickup-specific checkout validation was not identified as a distinct final validation branch.
- Provider payment runtime is delegated after order admission and is intentionally outside the B2 validation boundary.

## Certification Checklist

| Area | Result | Implementation Evidence |
| --- | --- | --- |
| Validation Pipeline | PASS | `CheckoutController.Checkout`, `CheckoutService.ProcessCheckoutAsync`, `CheckoutService.CreateOrderAsync` |
| Customer Validation | PASS | `ValidateRequestContextAsync`, `ResolveAuthorizedCustomerHeader`, `ValidateCheckoutCallerOwnershipAsync` |
| Cart Validation | PASS | `GetCartSnapshotAsync`, `RecalculatePricesAsync`, `CartService`, `CartDtos` |
| Inventory Validation | PASS | `ValidateAndReserveInventoryAsync`, `ProductVariantStockRepository.ReserveAsync` |
| Shipping Validation | PASS | `ValidateShippingMethodAsync`, `LoadShippingMethodAuthorityAsync`, `ShippingCalculationService` |
| Coupon Validation | PASS | `CouponValidationService`, `ReconcileCheckoutDiscountAuthorityAsync`, `ReserveCouponAsync` |
| Payment Validation | PASS | `ValidatePaymentMethodAsync`, `PaymentMethodConfigurationValidator`, `ValidatePaymentMethodOrderAmountLimits` |
| Pricing Validation | PASS | `StorefrontPricingService.CalculateCheckoutAsync`, `EnsureExpectedPricingMatches` |
| Business Rules | PASS | `CheckoutService`, `CheckoutRequestDto`, coupon/shipping/payment/inventory authority services |
| DTO Safety | PASS | `CheckoutRequestDto`, `CheckoutExpectedPricingDto`, `EnsureExpectedPricingMatches` |
| Runtime Boundary | PASS | `TryCreateTransactionalPaymentIntentAuthorityAsync`, `CreateCheckoutPaymentSessionAsync`, payment service delegation |
| Critical Invariants | PASS | `CheckoutService`, `StorefrontPricingService`, `ProductVariantStockRepository`, idempotency tests |
| Architecture Boundaries | PASS | `CheckoutService`, SQL authority reads/writes, repository authority, payment service delegation |
| Overall Certification | PASS | Checkout validation authority is implemented with non-violating observations |

## Evidence Index

Primary implementation evidence:

- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.API/Services/Pricing/StorefrontPricingService.cs`
- `PointOfSale.API/Services/Pricing/ShippingCalculationService.cs`
- `PointOfSale.API/Services/Pricing/TaxCalculationService.cs`
- `PointOfSale.Infrastructure/Discounts/CouponValidationService.cs`
- `PointOfSale.Core/Discounts/CouponAuthorityModels.cs`
- `PointOfSale.Core/Entities/Coupon.cs`
- `PointOfSale.API/Services/Coupons/CouponCustomerValidationRules.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.Core/Entities/ProductVariantStock.cs`
- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ShippingMethodDocument.cs`

Test evidence:

- `PointOfSale.Tests/CheckoutExecutionLockManagerTests.cs`
- `PointOfSale.Tests/CheckoutControllerIdempotencyHeaderTests.cs`
- `PointOfSale.Tests/CheckoutCartIdempotencySnapshotBuilderTests.cs`
- `PointOfSale.Tests/CheckoutRequestIdempotencyCoordinatorTests.cs`
- `PointOfSale.Tests/StorefrontPricingAuthorityTests.cs`
- `PointOfSale.Tests/StorefrontCartCouponAtomicityTests.cs`
- `PointOfSale.Tests/StorefrontCartCouponRevalidationTests.cs`
- `PointOfSale.Tests/Integration/CouponUsageConcurrencyIntegrationTests.cs`
- `PointOfSale.Tests/Integration/InventoryReservationLifecycleIntegrationTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutCouponCqrsConsistencyTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutPricingTotalsAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutGeographicIntegrationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontPaymentMethodSelectionAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ShippingMethodFinalAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ShippingMethodSelectionAuthorityCertificationTests.cs`
