# PASS SF-PA3.6 Product Availability & Purchasability Authority Certification

Date: 2026-06-29

## Scope

Reviewed only PointOfSale.Storefront product-detail availability presentation and the backend API/service boundary directly consumed by that Storefront runtime:

- `PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx`
- `PointOfSale.Storefront/src/hooks/useVariantSelector.ts`
- `PointOfSale.Storefront/src/types/product.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.API/Services/Storefront/IStorefrontProductService.cs`
- `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`

No CommerceHub, Marketplace, checkout validation, order placement, reservation, warehouse allocation, or stock deduction runtime was certified by this pass.

## Findings

Storefront Product Detail previously rendered button state and stock badge from frontend boolean composition over `product.isAvailable`, selected variant stock, variant selection state, and local loading/error state. Backend product detail and variant resolution already re-entered Storefront SQL/product authority before DTO admission, but the frontend still owned the final "can add to cart" presentation decision.

Runtime hardening added backend-composed `ProductAvailabilityPresentationDto` evidence for product detail, list/card products, and resolved variants. `StorefrontProductService` now emits backend-owned `isAvailable`, `canPurchase`, `canAddToCart`, `canBuyNow`, `stockLabel`, `maximumQuantity`, and `unavailableReason` evidence at the same admission boundary that already recomposes current product/variant authority.

Storefront Product Detail now fails closed unless admitted availability evidence says `canAddToCart === true`. Variant products use only the latest backend variant-resolution response as the purchasability evidence source. Missing, malformed, delayed, failed, stale, or unresolved availability DTOs withhold purchase CTAs instead of enabling them.

## Authority Rule

Availability, Purchasability, Purchase Policy, Add To Cart permission, Buy Now permission, Purchase Limits, Maximum Quantity, stock badge content, DTO admission, projection policy, persistence, recovery, and failure behavior are backend-owned for PointOfSale.Storefront Product Detail.

Frontend Product Detail may render, disable controls, show loading/errors, submit selected option candidates, submit Add To Cart intent, navigate, and display backend stock labels only. It must never calculate purchase authority from product/variant stock fields, infer maximum quantity, repair malformed availability DTOs, enable Add To Cart or Buy Now from React state, or recover availability from Redis, MongoDB, Elasticsearch, browser cache/BFCache/history, or previous React state.

## Regression Freeze

Added/updated `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs` to freeze backend-composed availability DTO evidence and prevent Storefront PDP from reintroducing local `product.isAvailable && stock` Add To Cart authority.
