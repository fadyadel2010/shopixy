# Phase 7.6 – Cart UI (Storefront)

## Overview

This phase implements a production-ready Cart UI in the Storefront that integrates with the existing Cart API. This is a UI-only phase with no backend changes.

## Goals Achieved

✅ Cart loads automatically on page open  
✅ Adding product updates cart instantly  
✅ Quantity update is reflected immediately (optimistic UI)  
✅ Cart survives page refresh (Redis + Session)  
✅ No console errors  
✅ No hardcoded styles  
✅ Build passes: `npm run build`  

## Architecture

```
CartPage.tsx
      ↓
useCartStore (Zustand)
      ↓
storefrontApi
      ↓
┌─────────────────────────────────────┐
│ GET  /api/storefront/cart           │
│ POST /api/storefront/cart/items     │
│ PUT  /api/storefront/cart/items/:id │
│ DEL  /api/storefront/cart/items/:id │
│ DEL  /api/storefront/cart           │
└─────────────────────────────────────┘
      ↓
httpClient (X-Tenant-Id + X-Session-Id)
```

## Files Created/Modified

### Types (`src/types/`)

| File | Purpose |
|------|---------|
| `cart.ts` | CartDto, CartItemDto, request types |
| `index.ts` | Added cart types export |

### Cart Store (`src/store/`)

| File | Purpose |
|------|---------|
| `cartStore.ts` | Full Zustand store with API integration |
| `index.ts` | Updated with cart exports |

### API (`src/services/api/`)

| File | Changes |
|------|---------|
| `endpoints.ts` | Added cart endpoints |
| `storefrontApi.ts` | Added cart API functions |

### Cart Components (`src/components/cart/`)

| File | Purpose |
|------|---------|
| `CartSkeleton.tsx` | Loading skeleton |
| `CartEmptyState.tsx` | Empty cart state |
| `CartItemRow.tsx` | Cart item with quantity controls |
| `CartSummary.tsx` | Order summary with checkout |
| `index.ts` | Barrel export |

### Pages

| File | Changes |
|------|---------|
| `CartPage.tsx` | Full cart page implementation |

## Cart Store API

### State

```typescript
interface CartState {
    cart: CartDto | null;
    isLoading: boolean;
    error: string | null;
    updatingItemId: string | null;
    isInitialized: boolean;
}
```

### Actions

```typescript
interface CartActions {
    fetchCart: () => Promise<void>;
    addItem: (productId: string, quantity: number) => Promise<boolean>;
    updateQuantity: (productId: string, quantity: number) => Promise<boolean>;
    removeItem: (productId: string) => Promise<boolean>;
    clearCart: () => Promise<boolean>;
    clearError: () => void;
}
```

### Selectors

```typescript
selectCart(state)           // Full cart object
selectCartItems(state)      // Cart items array
selectCartItemCount(state)  // Total item count
selectCartSubtotal(state)   // Subtotal amount
selectCartCurrency(state)   // Currency code
selectIsCartLoading(state)  // Loading state
selectCartError(state)      // Error message
selectIsCartEmpty(state)    // Whether cart is empty
selectUpdatingItemId(state) // ID of item being updated
selectIsCartInitialized(state) // Whether initial fetch completed
```

### Usage Example

```tsx
import { useCartStore, selectCartItemCount } from '@/store';

// In component
const itemCount = useCartStore(selectCartItemCount);
const addItem = useCartStore((state) => state.addItem);

// Add to cart
await addItem(productId, 1);
```

## UI States

| State | UI Behavior |
|-------|-------------|
| Loading | Skeleton rows (not spinner) |
| Empty cart | Friendly empty state + CTA |
| Error | Inline error banner + retry |
| Updating quantity | Optimistic UI + disabled buttons |
| Max quantity hit | Soft warning message |
| Remove item | Smooth transition |

## Cart API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/storefront/cart` | Fetch current cart |
| POST | `/api/storefront/cart/items` | Add item to cart |
| PUT | `/api/storefront/cart/items/{productId}` | Update item quantity |
| DELETE | `/api/storefront/cart/items/{productId}` | Remove item |
| DELETE | `/api/storefront/cart` | Clear entire cart |

All requests automatically include:
- `X-Tenant-Id` header
- `X-Session-Id` header

## Component Props

### CartItemRow

| Prop | Type | Description |
|------|------|-------------|
| item | CartItemDto | Cart item data |
| isUpdating | boolean | Whether item is being updated |
| currency | string | Currency code |
| onQuantityChange | function | Quantity change handler |
| onRemove | function | Remove handler |

### CartSummary

| Prop | Type | Description |
|------|------|-------------|
| subtotal | number | Cart subtotal |
| currency | string | Currency code |
| itemCount | number | Total items |
| isEmpty | boolean | Whether cart is empty |
| onClearCart | function | Clear cart handler |
| isClearing | boolean | Whether clearing in progress |

## Optimistic Updates

The cart store implements optimistic updates for better UX:

1. **Update Quantity**: 
   - Immediately updates UI
   - Sends API request
   - Rolls back on failure

2. **Remove Item**:
   - Immediately removes from UI
   - Sends API request
   - Rolls back on failure

3. **Clear Cart**:
   - Immediately clears UI
   - Sends API request
   - Rolls back on failure

## Mobile-First Design

- Touch-friendly quantity controls (44px+ tap targets)
- Responsive layout (stacked on mobile, side-by-side on desktop)
- Sticky summary on desktop
- Clear visual feedback for all actions

## Integration Points

### Adding to Cart from Product Cards

```tsx
// In ProductCard or ProductDetailsPage
const addItem = useCartStore((state) => state.addItem);

const handleAddToCart = async () => {
    const success = await addItem(product.id, 1);
    if (success) {
        // Show success toast or navigate to cart
    }
};
```

### Cart Icon in Header

```tsx
// In Header component
const itemCount = useCartStore(selectCartItemCount);

<a href="/cart" className="relative">
    <CartIcon />
    {itemCount > 0 && (
        <span className="badge">{itemCount}</span>
    )}
</a>
```

## Build Verification

```bash
cd PointOfSale.Storefront
npm run build
# ✓ built in ~2s
# Bundle size: ~334kB (gzip: ~102kB)
```

## Non-Goals (Not Implemented)

Per specification:
- ❌ Checkout logic
- ❌ Payment UI
- ❌ Address forms
- ❌ Authentication
- ❌ Backend changes

## Next Steps

Future phases could add:
- Checkout flow (Phase 7.7+)
- Saved addresses
- Payment integration
- Order confirmation
