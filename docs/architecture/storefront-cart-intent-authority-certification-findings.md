# PASS SF-PA4 Cart Intent & Authority Certification

PointOfSale.Storefront Cart Intent & Authority is production-certifiable after recovery/failure hardening. Review scope was limited to `PointOfSale.Storefront` cart runtime and directly consumed backend Storefront cart endpoints/services.

## Storefront Files Reviewed

- `PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/store/cartStateResolver.ts`
- `PointOfSale.Storefront/src/types/cart.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/pages/CartPage.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCartPage.tsx`
- `PointOfSale.Storefront/src/components/cart/CartItemRow.tsx`
- `PointOfSale.Storefront/src/components/cart/CartSummary.tsx`
- `PointOfSale.Storefront/src/components/common/CartBadge.tsx`

## Backend Endpoints Reviewed

- `GET /api/storefront/cart`
- `POST /api/storefront/cart/items`
- `PUT /api/storefront/cart/items/{productId}`
- `PUT /api/storefront/cart/items/variant/{variantId}`
- `DELETE /api/storefront/cart/items/{productId}`
- `DELETE /api/storefront/cart/items/variant/{variantId}`
- `DELETE /api/storefront/cart`
- `POST /api/storefront/cart/merge`
- `GET /api/storefront/cart/recover/{token}`

## Backend Services Reviewed

- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/Cart/ICartService.cs`
- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/DTOs/Storefront/RecoveredCartSnapshotDto.cs`
- `PointOfSale.API/Services/Cart/RecoveredCartSnapshotFactory.cs`

## Findings

Cart Identity, Cart Line Identity, Add To Cart admission, Quantity Validation, Cart Merge, Line Removal, Cart Snapshot, Pricing Snapshot, Variant Validation, Availability Validation, Persistence, Projection Policy, and Recovery are backend-owned. Storefront frontend owns selected quantity, Add To Cart intent, loading/error presentation, navigation, and temporary UI state only.

Runtime hardening closed two gaps:

- `StorefrontCartRecoveryController` no longer falls back to stored abandoned-cart JSON. Recovery must resolve a live cart through `ICartService.GetCartAsync`, which re-enters cart/product/variant/stock/pricing validation, or fail closed.
- `cartStateResolver` and `cartStore` no longer preserve prior cart presentation after fetch failure, network failure, or recovery 404. Recovery hydration now sets `isInitialized: false` and `skipNextFetch: false`, forcing cart-page convergence through the live backend cart read.

## Boundary Review

Cart Boundary: backend `StorefrontCartController` plus `CartService`; recovery source is live backend cart read; failure returns error/empty presentation.

Cart Line Boundary: backend `CartService.AddItemAsync`, variant resolution, and DTO emission; recovery source is live cart revalidation; failure rejects or removes unavailable lines.

Quantity Boundary: frontend submits quantity intent; backend validates request range, cart limits, current authoritative variant stock, and merge caps; failure returns 400/409/404/503 and frontend withholds stale cart.

Snapshot Boundary: backend cart read/mutation response starts the cart snapshot; Redis is persistence/cache backing, not independent authority; failure clears stale presentation.

Merge Boundary: backend `POST /api/storefront/cart/merge` owns guest-to-customer merge under cart locks; frontend login flow may trigger merge only.

Presentation Boundary: Storefront cart components render admitted cart DTOs only; local quantity state is temporary UI and mutation intent.

Recovery Boundary: recovery token validates abandoned-cart identity, then only live backend cart re-entry can return a snapshot; stored abandoned-cart JSON cannot restore cart authority.

## Cart Pipeline

Resolved Variant -> Purchase Intent -> Backend Cart Admission -> Cart Line DTO -> Cart Snapshot -> Storefront Presentation.

Purchase Intent begins in `product-hero.tsx` when the user presses Add To Cart with backend-resolved product/variant evidence and selected quantity. Cart Authority begins in `StorefrontCartController`/`CartService.AddItemAsync`. Cart Snapshot begins when `CartService` returns the admitted `CartDto` after validation/revalidation. Storefront presentation begins when `cartStore` accepts that backend response.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontPromotionsAuthorityCertificationTests.cs`
- `PointOfSale.Tests/AbandonedCart/AbandonedCartRecoverySecurityTests.cs`
- `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`

## Required Answers

- Reviewed only PointOfSale.Storefront runtime? Yes, plus directly consumed Storefront cart backend endpoints/services.
- Frontend files modified: `cartStore.ts`, `cartStateResolver.ts`.
- Backend files modified: `StorefrontCartRecoveryController.cs`.
- Cart Identity owner: backend.
- Cart Line Identity owner: backend.
- Add To Cart admission owner: backend.
- Quantity validation owner: backend.
- Cart Merge owner: backend.
- Cart Snapshot owner: backend.
- Can Storefront frontend create Cart Lines? No.
- Can frontend exceed purchase limits? No; backend validates quantity/range/current stock and returns failure.
- Can stale Cart survive recovery? No; stored recovery snapshots are refused unless live backend cart re-entry admits a snapshot.
- Can LocalStorage become Cart Authority? No.
- Can Redis become Cart Authority? No; Redis is backend cart persistence/cache and cart reads revalidate current product/variant/stock evidence.
- Purchase Intent begins: Storefront Add To Cart UI.
- Cart Authority begins: `StorefrontCartController`/`CartService`.
- Cart Snapshot begins: admitted `CartDto` returned by backend cart service.
- Storefront presentation begins: `cartStore` accepts backend cart DTO.
- Runtime changes required: Yes.
- Production-certifiable: Yes.
