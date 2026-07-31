# SIMPLE-03 Cart Persistence Report

## Root Cause

The active storefront cart runtime is the Zustand store in `PointOfSale.Storefront/src/store/cartStore.ts`, backed by the Storefront Cart API.

Source trace:

- Product Details add button: `ProductHeroRuntimeContainer` reads `useCartStore((state) => state.addItem)` and calls `addItem(product.id, 1, selectedVariant?.id)` after variant validation (`RuntimeSectionContainer.tsx:316`, `RuntimeSectionContainer.tsx:357`, `RuntimeSectionContainer.tsx:370-371`).
- Listing / Featured Products / Best Sellers / Search-style product grids all render through the shared `ProductCard` runtime path. `ProductCardRuntime` calls `addItem(product.id, 1)` for quick add (`ProductCardRuntime.tsx:60`, `ProductCardRuntime.tsx:105`, `ProductCardRuntime.tsx:115`).
- Cart page reads `selectCartItems`, `selectIsCartEmpty`, and `selectIsCartInitialized`; on mount it fetches only when `!isInitialized` (`CartPage.tsx:70`, `CartPage.tsx:79`, `CartPage.tsx:82`, `CartPage.tsx:102`). If not empty, it renders `items.map(...)` into `CartItemRow` (`CartPage.tsx:234-235`).
- Before this fix, `addItem` stored the returned API cart but did not set `isInitialized: true`. That left the Cart page eligible to immediately call `fetchCart()` on entry and replace the just-confirmed mutation response.

Backend persistence was inspected and is not a silent success path:

- `StorefrontCartController` exposes `GET /api/storefront/cart` and `POST /api/storefront/cart/items` (`StorefrontCartController.cs:99`, `StorefrontCartController.cs:174`).
- Cart ownership is tenant + authenticated customer or guest session (`StorefrontCartController.cs:1817`, `StorefrontCartController.cs:1858-1859`, `StorefrontCartController.cs:1879-1880`).
- Controller documentation states Redis is the only cart state storage (`StorefrontCartController.cs:50`).
- `CartService.AddItemInternalAsync` reads the Redis cart, creates one if missing, adds or updates the item, recalculates totals, and awaits Redis save before returning (`CartService.cs:490`, `CartService.cs:492`, `CartService.cs:611`, `CartService.cs:651`).
- `SaveCartToRedisAsync` performs `StringSetAsync`; failures throw `RedisUnavailableException`, so a successful API response is not produced after a swallowed Redis write (`CartService.cs:2925`, `CartService.cs:2971`).

Product identity was also verified:

- Variant products require a `variantId`; missing selection throws `VariantRequiredException` (`CartService.cs:399`, `CartService.cs:403`, `CartService.cs:409`).
- Simple products may omit `variantId`; backend chooses the active default variant (`CartService.cs:413-414`).

## Files Modified

- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts`
- `SIMPLE-03_CART_PERSISTENCE_REPORT.md`

## Fix Applied

Successful cart mutations now mark the cart store initialized when accepting an API-confirmed cart:

- Add item
- Update quantity
- Remove item
- Clear cart optimistic state
- Apply coupon
- Remove coupon
- Apply order draft

This keeps the mutation response authoritative for immediate Cart page rendering while browser refresh and direct `/cart` entry still use `fetchCart()` against the backend.

## Runtime Evidence

Source-backed render proof:

- After add, `cartStore.addItem()` sets the returned `CartDto` and now sets `isInitialized: true` (`cartStore.ts:389`, `cartStore.ts:412`).
- Cart page only fetches on mount when `!isInitialized` (`CartPage.tsx:102`).
- Cart page renders `CartEmptyState` only when `selectIsCartEmpty` is true; otherwise it maps `items` to `CartItemRow` (`CartPage.tsx:187`, `CartPage.tsx:234-235`).

Automated verification:

- `npm run test -- cartStore.persistence.test.ts` passed: 1 file, 4 tests.
- `npm run test` passed: 21 files, 202 tests.
- `npm run build` passed.

No live browser session was executed in this workspace because no browser automation dependency/configuration was present in the storefront project. The new regression tests verify the active runtime state transition that CartPage depends on, including direct refresh-style fetch.

## Regression Evidence

Covered by `cartStore.persistence.test.ts`:

- Product Details Add To Cart keeps the returned item in the active cart and sets `isInitialized`.
- Variant Add To Cart sends `productId`, `quantity`, and `variantId`, and preserves the returned variant cart row.
- Listing quick-add uses the same canonical `addItem(product.id, 1)` pipeline.
- Browser refresh / direct Cart URL path loads persisted guest cart through `fetchCart()`.

Unaffected by source inspection/build:

- Product listing, category routes, search/product grids, featured products, and best-seller style grids still use the shared `ProductCard` runtime.
- CommerceHub/backend cart storage remains Redis-backed and variant-aware.
- Browser back/forward navigation uses the same in-memory Zustand cart state after mutation.

Observation: `CartItemRow` links product rows to `/products/${item.sku}` (`CartItemRow.tsx:105`, `CartItemRow.tsx:120`). That is separate from the cart persistence failure and was not changed.

## Final Certification

SIMPLE-03 PASSED WITH OBSERVATIONS
