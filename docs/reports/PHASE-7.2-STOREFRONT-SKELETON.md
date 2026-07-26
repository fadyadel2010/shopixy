# Phase 7.2 – Storefront Skeleton (React + Tailwind)

## Overview

This phase implements the **E-commerce Storefront Skeleton** using React + Tailwind CSS, fully driven by backend APIs and StoreConfig. This is a frontend foundation only - no advanced UI, themes, or business logic yet.

## Architecture

```
App.tsx
    ↓
bootstrap.ts
    ↓
GET /api/storefront/store-config
    ↓
storeConfigStore (Zustand)
    ↓
applyTheme() → CSS Variables on :root
    ↓
Render UI
```

## Tech Stack

- **React** 18.3.1 (same version as POS)
- **Tailwind CSS** 3.4.10
- **TypeScript** 5.5.3
- **Vite** 5.4.2
- **Zustand** 4.5.5 (state management)
- **React Router** 6.26.0
- **Axios** 1.7.7 (HTTP client)

## Project Structure

```
PointOfSale.Storefront/
├── src/
│   ├── app/
│   │   ├── App.tsx          # Main app component
│   │   ├── router.tsx       # React Router config
│   │   └── bootstrap.ts     # App initialization
│   ├── components/
│   │   ├── layout/
│   │   │   ├── Header.tsx   # Header with variants
│   │   │   ├── Footer.tsx   # Footer component
│   │   │   └── Layout.tsx   # Layout wrapper
│   │   └── common/
│   │       ├── Loading.tsx      # Loading skeletons
│   │       ├── ErrorBoundary.tsx # Error handling
│   │       ├── EmptyState.tsx   # Empty state display
│   │       ├── ConfigError.tsx  # Config error fallback
│   │       └── ProductCard.tsx  # Product card
│   ├── pages/
│   │   ├── HomePage.tsx
│   │   ├── ProductListPage.tsx
│   │   ├── ProductDetailsPage.tsx
│   │   ├── CartPage.tsx          # Placeholder
│   │   └── CheckoutPage.tsx      # Placeholder
│   ├── services/
│   │   └── api/
│   │       ├── httpClient.ts     # Axios with X-Tenant-Id
│   │       ├── storefrontApi.ts  # API methods
│   │       └── endpoints.ts      # API endpoints
│   ├── store/
│   │   ├── storeConfigStore.ts   # StoreConfig state
│   │   └── cartStore.ts          # Empty placeholder
│   ├── theme/
│   │   ├── applyTheme.ts         # Apply CSS variables
│   │   └── cssVariables.ts       # Variable mappings
│   ├── types/
│   │   ├── storeConfig.ts        # Store config types
│   │   ├── product.ts            # Product types
│   │   ├── category.ts           # Category types
│   │   └── api.ts                # API error types
│   └── styles/
│       └── index.css             # Tailwind + CSS vars
├── package.json
├── vite.config.ts
├── tailwind.config.js
└── tsconfig.json
```

## API Integration

### Endpoints Used (READ-ONLY)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/storefront/store-config` | GET | Store configuration |
| `/api/storefront/products` | GET | Paginated product list |
| `/api/storefront/products/{slug}` | GET | Single product details |
| `/api/storefront/categories` | GET | Category tree |

### Headers

All requests include:
- `X-Tenant-Id: {tenant-guid}` - Required for multi-tenant isolation
- `Content-Type: application/json`

### Environment Variables

```bash
VITE_API_URL=         # Optional: API base URL (uses proxy if empty)
VITE_TENANT_ID=       # Required: Tenant ID for storefront
```

## Theme System

### CSS Variables Strategy

❌ **Forbidden:**
- Inline styles
- Hardcoded Tailwind colors
- Conditional JSX based on theme

✅ **Required:**
- CSS Variables on `:root`
- Tailwind configured to use CSS variables

### CSS Variables Applied

| Variable | Source | Example |
|----------|--------|---------|
| `--color-primary` | `theme.colors.primary` | `#2563eb` |
| `--color-secondary` | `theme.colors.secondary` | `#0f172a` |
| `--color-background` | `theme.colors.background` | `#ffffff` |
| `--color-text` | `theme.colors.text` | `#020617` |
| `--color-muted` | `theme.colors.muted` | `#64748b` |
| `--font-family` | `theme.font.family` | `Inter` |
| `--font-base-size` | `theme.font.baseSize` | `16px` |
| `--border-radius` | `theme.ui.borderRadius` | `8px` |

### Tailwind Configuration

```javascript
colors: {
  primary: 'var(--color-primary)',
  secondary: 'var(--color-secondary)',
  background: 'var(--color-background)',
  text: 'var(--color-text)',
  muted: 'var(--color-muted)',
}
```

## Layout Variants

### Header Variants

| Variant | Description |
|---------|-------------|
| `standard` | Full navigation with logo left |
| `centered` | Logo centered, nav below |
| `minimal` | Just logo and cart |

Implementation uses **component composition** - NOT conditionals:

```tsx
const HEADER_VARIANTS = {
  standard: HeaderStandard,
  centered: HeaderCentered,
  minimal: HeaderMinimal,
};

const HeaderComponent = HEADER_VARIANTS[layout.headerVariant];
```

### Product Card Variant (Placeholder)

- `default`, `compact`, `detailed`
- Currently **logs variant value only**
- Actual UI differences in Phase 7.3

## State Management

### storeConfigStore (Zustand)

```typescript
interface StoreConfigState {
  config: StoreConfig;
  isLoading: boolean;
  isLoaded: boolean;
  error: string | null;
  
  setConfig: (config: StoreConfig) => void;
  setLoading: (loading: boolean) => void;
  setError: (error: string | null) => void;
}
```

### cartStore (Empty)

- Placeholder only
- NO implementation yet

## Error Handling

- **Global ErrorBoundary** - Catches uncaught errors
- **Config error screen** - Shown when StoreConfig fails
- **Graceful loading skeletons** - For data fetching

## Logging (Dev Only)

In development mode, logs:
- `[Bootstrap] StoreConfig loaded`
- `[Theme] Applied CSS variables`
- `[Theme] Layout variants`
- `[ProductCard] Variant`

No production logging.

## Running the Storefront

```bash
cd PointOfSale.Storefront
npm install
npm run dev
```

Runs on port **5174** (to avoid conflicts with POS on 5173).

API is proxied to `http://localhost:5010`.

## Building

```bash
npm run build
```

Outputs to `dist/` folder.

## What's NOT Implemented (Per Spec)

- ❌ Auth
- ❌ Checkout logic
- ❌ Cart logic
- ❌ Payment
- ❌ SEO
- ❌ Animations
- ❌ Responsive polish
- ❌ Multiple themes
- ❌ Page builder / CMS logic
- ❌ Admin logic

## Acceptance Criteria ✅

- [x] App boots with StoreConfig
- [x] CSS variables applied correctly
- [x] Header/Footer visible
- [x] Products list renders
- [x] Product details render
- [x] No hardcoded colors/fonts
- [x] Clean folder structure
- [x] Build passes

## Next Phase

Proceed to **Phase 7.3 – Theme Variants & UI Polishing**

## Files Created

| File | Purpose |
|------|---------|
| `package.json` | Dependencies and scripts |
| `vite.config.ts` | Vite build config with proxy |
| `tailwind.config.js` | Tailwind with CSS variables |
| `tsconfig.json` | TypeScript config |
| `src/app/App.tsx` | Main app component |
| `src/app/router.tsx` | React Router routes |
| `src/app/bootstrap.ts` | App initialization |
| `src/components/layout/*` | Layout components |
| `src/components/common/*` | Common components |
| `src/pages/*` | Page components |
| `src/services/api/*` | API integration |
| `src/store/*` | Zustand stores |
| `src/theme/*` | Theme utilities |
| `src/types/*` | TypeScript types |
| `src/styles/index.css` | Global styles |
