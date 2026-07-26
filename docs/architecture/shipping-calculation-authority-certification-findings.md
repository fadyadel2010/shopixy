# PASS SF-SHP3 Shipping Calculation Authority Certification

Date: 2026-07-03

## Verdict

Shipping Calculation Authority is production-certifiable for the inspected runtime after DTO-admission hardening on shipping calculation inputs.

Review scope was limited to Storefront/admin shipping calculation runtime: admin shipping method configuration feeding calculation, Storefront checkout shipping selector/summary/order summary, shipping quote presentation, checkout pricing preview, final checkout pricing admission, backend shipping calculation/pricing services, SQL order shipping snapshot, cache/replay behavior, and adjacent financial boundaries. Carrier integrations, shipment creation, tracking, fulfillment, returns, split shipment execution, warehouse execution, inventory reservation, audit, and recovery internals were not recertified.

## Ownership

Backend owns Shipping Fee, Shipping Cost, Shipping Currency, Shipping Zone where modeled by method/country eligibility, Shipping Rule, Shipping Pricing, Shipping Calculation, backend-derived Weight, Package Dimensions where modeled, Volumetric Weight where modeled, Delivery Estimate, Estimated Days, Minimum Order where modeled by adjacent payment/shipping eligibility, Maximum Weight where modeled, Free Shipping, Zone Pricing where modeled, Carrier Pricing metadata, Checkout Shipping Total, Expected Shipping Total validation, Financial Authority, and SQL Truth.

Frontend owns only address candidates, shipping method candidates, refresh/retry/navigation, admitted presentation, and rendering.

## Runtime Findings

- `ShippingCalculationService` applies backend configuration only: free-shipping threshold, base rate, per-kg rate, non-negative effective weight, and two-decimal rounding.
- Storefront final checkout submits only `ShippingMethodId`, address, payment method, and `ExpectedPricing`. Expected shipping is stale-detection evidence only; `CheckoutService.EnsureExpectedPricingMatches` rejects mismatches against fresh backend pricing.
- Final checkout revalidates tenant-owned SQL shipping method authority, tenant currency, cart/product/variant pricing, coupons, promotions, payment fees, and order totals before SQL order snapshot persistence.
- Storefront checkout displays backend `pricing.shipping` only. Zustand/localStorage do not persist accepted shipping pricing, quote DTOs, order results, checkout tokens, or SQL truth.
- Admin shipping method configuration now rejects negative base rate, negative per-kg rate, negative free-shipping threshold, negative delivery days, invalid delivery windows, and negative sort order before SQL writes.
- Standalone shipping calculate requests now reject missing method identity, negative order total, and negative total weight before calculation.
- Storefront shipping quote requests now reject missing DTOs, negative weight, non-positive package count, negative COD amount, and negative order subtotal before quote calculation. Quote responses remain no-store and frontend-admitted before React state.

## Cache, Replay, And Boundary Findings

React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projection data are derivative. They may display, invalidate, refetch, or submit stale-detection candidates only. They cannot become Shipping Financial Authority or SQL Truth.

Replay, refresh, retry, duplicate quote, duplicate checkout, and duplicate calculation cannot fabricate accepted Shipping Calculation Authority. Final checkout re-enters backend pricing and SQL order transaction authority; idempotency replays return the already-created backend result or reject payload conflicts.

Checkout, Orders, Payment, Coupons, Promotions, Smart Offers, Affiliate, CommerceHub, Marketplace, and Inventory do not overwrite Shipping Calculation Authority. They participate only through backend-owned pricing validation, discount/payment/tax interaction, stale detection, and SQL snapshot persistence.

## Pipeline

- Shipping Calculation begins when backend pricing/quote/calculation services receive admitted address/method/cart candidates and load backend configuration.
- Pricing begins when backend `ShippingCalculationService`, `ShippingMethodService`, quote service, or checkout pricing preview applies backend shipping rules to backend-admitted inputs.
- Financial Authority begins at backend checkout pricing validation and final checkout `EnsureExpectedPricingMatches` against fresh authoritative totals.
- SQL persistence begins when final checkout writes the accepted order shipping method, shipping cost, currency, and totals into SQL order snapshots.
- Presentation begins only after backend DTO emission and frontend DTO admission.

## Runtime Changes

- Hardened `ShippingMethodsController` DTO admission for admin shipping method create/update and standalone shipping calculation.
- Hardened `StorefrontShippingQuotesController` DTO admission for missing/malformed quote requests and negative quote-calculation inputs.
- Added `PointOfSale.Tests/Architecture/ShippingCalculationAuthorityCertificationTests.cs`.

## Explicit Answers

- Can frontend fabricate Shipping Fee, Free Shipping, Shipping Totals, or Shipping Currency? No.
- Can replay fabricate Shipping Calculation? No.
- Can React, React Query, Zustand, LocalStorage, Redis, or Mongo Projection become authority? No.
- Were runtime changes required? Yes, DTO-admission hardening was required.
- Is Shipping Calculation production-certifiable? Yes.
