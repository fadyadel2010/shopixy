# SHOPIXY Checkout Order Summary C2 Implementation - 2026-08-12

## 1. Executive Summary

C2 traced Storefront Checkout Order Summary from React rendering through cart store, Storefront API client, backend cart controller, Redis cart DTO, and Mongo/SQL product projection inputs.

The authoritative backend cart line already contains product presentation snapshots, but the Storefront frontend expected different property names. Backend `CartItemDto` serializes product name as `name` and image as `thumbnailImage`; frontend checkout/cart models consumed `productName` and `imageUrl`. The first incorrect point was the frontend API boundary, where raw authoritative cart data was accepted without normalization.

Implemented the smallest safe fix: normalize cart API responses once in `PointOfSale.Storefront/src/services/api/cartResponse.ts`, then render the normalized authoritative cart line in Order Summary.

Certification: **PASS WITH EXTERNAL VERIFICATION BLOCKED**.

## 2. Current Symptom

Storefront Checkout Order Summary product presentation was incomplete. The summary row read `item.productName`, but backend cart JSON from the current C# DTO shape provides `name`. This makes product names render blank even when the authoritative cart snapshot contains the product name.

## 3. Order Summary UI Trace

Renderer: `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`.

State source:
- `useCartStore(selectCartItems)` from `PointOfSale.Storefront/src/store/cartStore.ts`
- `useCheckoutStore(selectPricingPreview)` for server pricing totals

Pre-fix row fields:
- key: `item.productId`
- image: `resolveProductImage(item)`
- alt/name: `item.productName`
- quantity: `item.quantity`
- price: `item.lineTotal`
- promotion fields when present

Defects directly related to summary integrity:
- `productName` was not normalized from backend `name`.
- React key used only `productId`, so two variants of the same product could share a key.
- Existing cart contract fields `variantAttributes`, `sku`, and `unitPrice` were not rendered in checkout summary even though the existing cart UI already renders them.

## 4. Cart/API Contract Trace

Frontend cart fetch:
- `fetchCart()` in `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- endpoint: `GET /api/storefront/cart`
- response stored directly in `useCartStore`

Backend cart endpoint:
- `StorefrontCartController.GetCart`
- calls `ICartService.GetCartAsync`
- returns `PointOfSale.API.DTOs.Cart.CartDto`

Backend serializer policy:
- `CartService` uses `JsonNamingPolicy.CamelCase`

Backend `CartItemDto` fields:
- `Name` -> JSON `name`
- `NameAr` -> JSON `nameAr`
- `SKU` -> JSON `sku`
- `UnitPrice` -> JSON `unitPrice`
- `LineTotal` -> JSON `lineTotal`
- `ThumbnailImage` -> JSON `thumbnailImage`
- `VariantAttributes` -> JSON `variantAttributes`

Frontend expected:
- `productName`
- `productNameAr`
- `imageUrl`
- `sku`
- `unitPrice`
- `lineTotal`
- `variantAttributes`

## 5. Product Data Source

Product presentation data is captured into the Redis cart line by `CartService.AddItemAsync`.

For variants:
- price from variant purchasability
- SKU from variant SKU with product fallback
- image from variant media id with product primary image fallback
- attributes from variant attributes

For simple products:
- price from product purchasability
- SKU from product SKU
- image from primary product media
- no variant attributes

Cart read revalidation updates stock, price, availability, affiliate admission, and promotion snapshots. It does not invent client product data.

## 6. Image Forensics

Backend image source:
- `ResolveVariantImageUrl(product, variant)`
- `ResolvePrimaryProductImageUrl(product)`
- stored as `CartItemDto.ThumbnailImage`
- serialized as `thumbnailImage`

Frontend image resolver already accepted `thumbnailImage`, `imageUrl`, `image`, `mainImage`, and legacy arrays, so images were less broken than names. The fix normalizes `thumbnailImage` to `imageUrl` while keeping `resolveProductImage` fallback behavior.

Missing image behavior remains safe: summary uses the existing inline SVG fallback from `resolveProductImage`.

## 7. Variant Forensics

Backend stores selected variant identity:
- `variantId`
- `sku`
- `variantAttributes`
- variant-specific price
- variant/product image fallback

Pre-fix Order Summary did not render variant attributes or SKU. It also keyed rows by `productId`, which is unsafe when two selected variants share one product.

Post-fix Order Summary:
- key uses `variantId` when present
- renders variant values as `Red / XL`
- renders SKU when present
- keeps duplicate variant lines distinct

## 8. Price/Quantity Authority

The summary remains a view of server-authoritative cart state:
- unit price: `item.unitPrice`
- quantity: `item.quantity`
- line subtotal: `item.lineTotal`
- discounted line total: `item.discountedLineTotal` when backend provided
- total/shipping/tax/payment fee: checkout pricing preview from backend

No frontend price recalculation was added. The only aggregation in tests is fixture setup, not runtime code.

## 9. Root Cause

Root cause: frontend/backend cart line contract drift.

The authoritative cart snapshot contains product presentation data, but the frontend consumed an incompatible field name:
- backend: `name`
- frontend: `productName`

The exact first wrong point is `storefrontApi.ts` returning raw `response.data` as `CartDto` without adapting backend aliases into the frontend cart contract.

## 10. Fix Implemented

Added:
- `PointOfSale.Storefront/src/services/api/cartResponse.ts`

Changed:
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`

Behavior:
- normalize `name` -> `productName`
- normalize `nameAr` -> `productNameAr`
- normalize `thumbnailImage` -> `imageUrl`
- preserve server-provided `unitPrice`, `quantity`, `lineTotal`, discounts, SKU, variant attributes, cart id, session/owner, totals, and currency
- render product name, image, variant values, SKU, quantity, unit price, and line subtotal
- use stable variant-aware row key
- render `Product unavailable` instead of a silent blank name if authoritative presentation data is missing

No backend DTO, shipping, payment, warehouse resolution, or order creation code was changed.

## 11. Regression Tests

Added:
- `PointOfSale.Storefront/src/services/api/cartResponse.test.ts`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.test.tsx`

Coverage:
- product name normalization
- image normalization from authoritative URL
- variant attributes
- SKU
- quantity
- unit price
- line subtotal
- cart refresh/session swap replacement
- product removal by refreshed authoritative cart
- distinct variant lines
- safe missing-image fallback
- no editable price controls in summary
- summary render output for authoritative line data
- shipping/payment summary behavior left intact by scope

## 12. Builds / EF

Passed:
- `npm run test` in `PointOfSale.Storefront`: 29 files, 291 tests
- `npm run build` in `PointOfSale.Storefront`
- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`
- `dotnet build PointOfSale.sln --no-restore`
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj`

EF result:
- `No changes have been made to the model since the last migration.`

Backend relevant cart/session test filter:
- command: `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~StorefrontCart|FullyQualifiedName~CheckoutSessionConsistency|FullyQualifiedName~CheckoutCart"`
- result: 10 passed, 14 failed
- blocker: existing SQLite schema setup cannot create PostgreSQL `btrim` expressions; one duplicate-session Postgres case returned existing conflict. These failures are outside the C2 frontend contract fix and occurred before C2 behavior assertions.

## 13. Browser Verification

Storefront dev server:
- `http://localhost:5174/checkout` returned the Vite shell.

API:
- `dotnet run --no-build --project PointOfSale.API/PointOfSale.API.csproj --urls http://localhost:5010` starts.
- Raw anonymous cart probe with known tenant header returned:
  - `400`
  - `TENANT_REQUIRED`
  - message: tenant context must be resolved by tenant-bound token or verified storefront domain.

Browser automation:
- no local `playwright`, `@playwright/test`, or `playwright-core` package exists in `PointOfSale.Storefront/node_modules`.

Therefore real browser checkout with a live cart, DOM/network capture, quantity update, and product removal could not be completed in this environment.

## 14. Remaining Limitations

Live browser verification is externally blocked by:
- backend tenant admission requiring tenant-bound auth/domain rather than raw local headers
- no local browser automation package
- known local product projection docs show the traced product missing from Mongo/Elasticsearch, so it is not a reliable add-to-cart seed

The code path is covered by Storefront unit/render tests and production build.

## 15. Worktree Safety

Pre-change worktree was already dirty with prior C1/C1.1/C1.2 and payment/shipping changes. C2 did not revert or modify unrelated files.

C2 files changed:
- `PointOfSale.Storefront/src/services/api/cartResponse.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`
- `PointOfSale.Storefront/src/services/api/cartResponse.test.ts`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.test.tsx`
- this report

No `git reset`, `git clean`, `git restore`, `git checkout`, `git stash`, commit, or push was performed.

## 16. Final Certification

**PASS WITH EXTERNAL VERIFICATION BLOCKED**

The Order Summary now renders normalized, server-authoritative cart line presentation and pricing data. Tests and builds pass. Live browser verification remains blocked by local tenant admission/browser tooling constraints, not by the implemented C2 fix.
