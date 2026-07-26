# Phase 7.5 – Storefront Data Flows, Search & UX Hardening

## Overview

This phase implements production-ready data flows and UX behaviors for the Storefront without introducing new backend logic or breaking existing contracts.

## Goals Achieved

✅ Product listing with pagination & category filtering  
✅ Search works with debounce and proper UX  
✅ All loading states use skeletons (not spinners)  
✅ All errors handled gracefully  
✅ UI reacts correctly to StoreConfig variants  
✅ Mobile UX is clean and touch-friendly  
✅ No regression in Phase 7.2 – 7.4  
✅ `npm run build` passes cleanly  

## Architecture

```
UI Interaction
      ↓
Query State (React Hooks)
      ↓
Storefront API Service
      ↓
Normalized Response
      ↓
UI State Machine (loading → success | error | empty)
```

## Files Created

### Hooks (`src/hooks/`)

| File | Purpose |
|------|---------|
| `useProducts.ts` | Fetch paginated products with category filter |
| `useCategories.ts` | Fetch and cache categories |
| `useProductSearch.ts` | Debounced search with status tracking |
| `index.ts` | Module exports |

### Utilities (`src/utils/`)

| File | Purpose |
|------|---------|
| `debounce.ts` | Debounce function and useDebounce hook |
| `scrollToTop.ts` | Smooth scroll utilities |
| `index.ts` | Module exports |

### Data Components (`src/components/data/`)

| File | Purpose |
|------|---------|
| `SkeletonGrid.tsx` | Grid of skeleton cards (variant-aware) |
| `EmptyState.tsx` | Friendly empty state with variants |
| `InlineError.tsx` | Inline error with retry |
| `RetryBlock.tsx` | Compact retry block |
| `CategoryNav.tsx` | Horizontal scrollable category nav |
| `SearchInput.tsx` | Debounced search input with status |
| `Pagination.tsx` | Accessible pagination component |
| `index.ts` | Module exports |

### Updated Pages

| File | Changes |
|------|---------|
| `ProductListPage.tsx` | Full rewrite with hooks, search, filtering |
| `HomePage.tsx` | Uses data hooks for loading/error states |

## Features Implemented

### 1. Product Listing Data Flow

```typescript
// Hook usage
const { products, pagination, isLoading, error, refetch } = useProducts({
    page: 1,
    pageSize: 20,
    categoryId: 'optional-category-id',
});
```

**UX States:**
| State | Behavior |
|-------|----------|
| Loading | Skeleton grid |
| Empty | Friendly empty message + icon |
| Error | Inline retry block |
| Success | Responsive grid |

### 2. Category Navigation

```typescript
// Hook usage (cached)
const { categories, isLoading, error, refetch } = useCategories();
```

**Features:**
- Horizontal scrollable navigation
- Click updates product list
- Selected category highlighted
- Mobile touch-friendly
- Categories cached in memory (no reload on page change)
- Fade gradients for scroll overflow indication

### 3. Search UI Integration

```typescript
// Hook usage
const {
    query,
    setQuery,
    results,
    status,
    error,
    clearSearch,
} = useProductSearch({ categoryId });
```

**Features:**
- 400ms debounce
- Minimum 2 characters required
- Search query in URL (bookmarkable)
- Clear search resets list
- Pagination preserved

**UX States:**
| State | Behavior |
|-------|----------|
| idle | No search active |
| typing | Input changing, waiting for debounce |
| searching | API call in progress |
| success | Results displayed |
| no-results | Empty state with clear action |
| error | Soft warning + retry |

### 4. Pagination UX

```typescript
<Pagination
    currentPage={1}
    totalPages={10}
    totalCount={200}
    pageSize={20}
    onPageChange={(page) => handlePageChange(page)}
/>
```

**Features:**
- Page numbers with ellipsis
- Previous / Next buttons
- Disabled states
- Scroll to top on page change
- Mobile-friendly (shows "1 / 10" instead of numbers)
- Item count display ("Showing 1-20 of 200 products")

**Rules:**
- Page state reflected in URL query params
- Pagination resets on category change
- Pagination resets on search query change

### 5. Unified Loading & Error Patterns

#### SkeletonGrid
```typescript
<SkeletonGrid count={8} />
// Automatically uses correct variant from StoreConfig
```

#### DataEmptyState
```typescript
<DataEmptyState
    variant="search" // 'default' | 'search' | 'category' | 'error'
    title="No products found"
    message="Try a different search term."
    action={{
        label: 'Clear Search',
        onClick: handleClear,
    }}
/>
```

#### InlineError
```typescript
<InlineError
    message="Failed to load products"
    onRetry={refetch}
/>
```

#### RetryBlock
```typescript
<RetryBlock
    message="Something went wrong"
    onRetry={handleRetry}
    inline={false}
/>
```

## Data Flow Pattern

### Product List Page

```
URL: /products?page=2&category=abc&q=shirt
         ↓
┌────────────────────────────────────────┐
│ Parse URL params                       │
│ - page: 2                              │
│ - categoryId: 'abc'                    │
│ - searchQuery: 'shirt'                 │
└────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────┐
│ Determine mode                         │
│ - isSearchMode: searchQuery.length >= 2│
└────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────┐
│ Call appropriate hook                  │
│ - Search: useProductSearch()           │
│ - List: useProducts()                  │
└────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────┐
│ Render based on state                  │
│ - Loading → SkeletonGrid              │
│ - Error → InlineError                  │
│ - Empty → DataEmptyState              │
│ - Success → ProductGrid + Pagination   │
└────────────────────────────────────────┘
```

### Category Caching

```
First load:
  useCategories() → fetchCategories() → Cache in module → Return data

Subsequent loads:
  useCategories() → Return from cache (no API call)

Force refresh:
  refetch() → Clear cache → fetchCategories() → Update cache
```

## Component Props Reference

### SkeletonGrid
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| count | number | 8 | Number of skeletons |
| variant | string | StoreConfig | Override variant |

### DataEmptyState
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| title | string | required | Title text |
| message | string | - | Description |
| variant | string | 'default' | Icon variant |
| action | object | - | Action button |

### CategoryNav
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| categories | array | required | Category list |
| selectedId | string | - | Selected ID |
| onSelect | function | required | Selection callback |
| showAll | boolean | true | Show "All" option |
| isLoading | boolean | false | Loading state |

### SearchInput
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| value | string | required | Current query |
| onChange | function | required | Change callback |
| onClear | function | required | Clear callback |
| status | SearchStatus | required | Current status |
| minChars | number | 2 | Min chars hint |

### Pagination
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| currentPage | number | required | Current page |
| totalPages | number | required | Total pages |
| totalCount | number | - | Total items |
| pageSize | number | - | Items per page |
| onPageChange | function | required | Page change callback |

## Accessibility

All components implement:
- ARIA labels
- Role attributes
- Focus states
- Keyboard navigation
- Screen reader support

## Build Verification

```bash
cd PointOfSale.Storefront
npm run build
# ✓ built in ~2s
# Bundle size: ~320kB (gzip: ~100kB)
```

## Non-Goals (Not Implemented)

Per specification:
- ❌ Cart UI
- ❌ Checkout UI
- ❌ User login
- ❌ Wishlist
- ❌ Reviews
- ❌ SEO changes
- ❌ Backend edits

## Next Steps

Potential future enhancements:
- Product filtering (price range, attributes)
- Sort options implementation
- Search suggestions/autocomplete
- Recently viewed products
- Related products
