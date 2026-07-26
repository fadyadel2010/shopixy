# 📊 Phase 2: Storefront Analytics SDK

## Status: ✅ COMPLETE

**Implementation Date:** 2026-01-20

---

## 🎯 Phase 2 Objective

Build a lightweight, deterministic, frontend analytics SDK that:
- Emits POS-canonical analytics events
- Has **ZERO knowledge** of Google Analytics or Meta
- Handles session tracking, batching, retries, and offline mode
- Is safe for React / SPA navigation
- Can be swapped or extended without touching business code

---

## 🏗️ Architecture Overview

```
React Component
       ↓
useAnalytics() hook
       ↓
AnalyticsEventBuilder
       • Generate EventId (UUID v4)
       • Attach SessionId
       • Resolve Context (pageUrl, language, etc.)
       ↓
AnalyticsQueue
       • Buffer in memory
       • Persist to localStorage
       • Batch (max 20 events)
       • Retry with backoff
       ↓
AnalyticsClient
       • POST /api/storefront/analytics/events
       • Include X-Tenant-Id, X-Session-Id
       ↓
Backend (Phase 1)
```

---

## 📦 SDK Package Structure

```
src/analytics/
├── index.ts                      # Main entry point with all exports
├── eventTypes.ts                 # Event types & interfaces
├── AnalyticsSession.ts           # Session management
├── AnalyticsContextResolver.ts   # Auto-populate context
├── AnalyticsEventBuilder.ts      # Build & validate events
├── AnalyticsQueue.ts             # Buffering, batching, retries
├── AnalyticsClient.ts            # Network layer
└── hooks/
    ├── index.ts                  # Hooks exports
    ├── useAnalytics.ts           # Main analytics hook
    └── useProductImpressions.ts  # IntersectionObserver for impressions
```

---

## 🔑 Core Components

### 1️⃣ Event Types (`eventTypes.ts`)

POS-native event types that mirror the backend:

| Event Type | Business Value |
|------------|----------------|
| `PageView` | Traffic analysis |
| `ProductImpression` | Discovery funnel |
| `ProductView` | Product interest |
| `SearchPerformed` | Search behavior |
| `FilterApplied` | Navigation patterns |
| `AddToCart` | Purchase intent |
| `RemoveFromCart` | Cart abandonment |
| `BeginCheckout` | Conversion funnel |
| `PurchaseCompleted` | Revenue tracking |
| `PaymentFailed` | Drop-off analysis |

### 2️⃣ Session Management (`AnalyticsSession.ts`)

```typescript
// Get current session ID (persists in localStorage)
const sessionId = getAnalyticsSessionId();

// Mark user activity (resets inactivity timer)
markSessionActivity();

// Check if session is active
const active = isSessionActive();
```

**Features:**
- ✅ SessionId survives page reloads
- ✅ Stored in localStorage
- ✅ 30-minute inactivity timeout tracking
- ✅ Shared with existing cart session

### 3️⃣ Context Resolver (`AnalyticsContextResolver.ts`)

Auto-populates context for every event:

| Field | Source |
|-------|--------|
| `pageUrl` | `window.location.href` |
| `referrer` | `document.referrer` |
| `language` | `document.documentElement.lang` |
| `screenWidth` | `window.innerWidth` |
| `screenHeight` | `window.innerHeight` |

**NOT sent from frontend** (backend captures):
- `ipAddress`
- `userAgent`
- `currency`
- `deviceType`

### 4️⃣ Event Builder (`AnalyticsEventBuilder.ts`)

```typescript
const event = buildAnalyticsEvent(
    AnalyticsEventType.ProductView,
    { productId, productName },
    { pageUrl: '/products/widget' }  // optional override
);
```

**Features:**
- ✅ Generates UUID v4 EventId
- ✅ Attaches actor (sessionId) automatically
- ✅ Attaches context automatically
- ✅ Normalizes & sanitizes payload
- ✅ Validates required fields per event type

### 5️⃣ Event Queue (`AnalyticsQueue.ts`)

```typescript
// Enqueue event (auto-batched)
enqueueAnalyticsEvent(event);

// Manual flush
await flushAnalyticsQueue();

// Check queue size
const pending = getAnalyticsQueueLength();
```

**Features:**
- ✅ Max batch size: 20 events
- ✅ Max queue size: 500 events
- ✅ Persists to localStorage (offline mode)
- ✅ Auto-flush every 10 seconds
- ✅ Flush on visibility change
- ✅ Flush on page unload
- ✅ Flush on network reconnect
- ✅ Retry with backoff (max 5 attempts)
- ✅ Never blocks UI thread

### 6️⃣ Network Client (`AnalyticsClient.ts`)

Single network layer for all analytics:

```typescript
// Single event
await sendAnalyticsEvent(event);

// Batch events
await sendAnalyticsBatch(events);

// Health check
const healthy = await checkAnalyticsHealth();
```

**Features:**
- ✅ Respects rate limits (429 handling)
- ✅ Request timeout (10 seconds)
- ✅ Includes X-Tenant-Id, X-Session-Id
- ✅ Never throws in UI (silent failure)

---

## 🎣 React Hooks

### `useAnalytics()` - Main Hook

```tsx
import { useAnalytics } from '@/analytics';

function ProductPage({ product }) {
    const analytics = useAnalytics();
    
    // Automatic page view on mount
    
    // Track product view
    useEffect(() => {
        analytics.productView(product.id, product.name);
    }, [product.id]);
    
    // Track add to cart
    const handleAddToCart = () => {
        analytics.addToCart(
            product.id,
            selectedVariantId,
            product.price,
            quantity,
            product.name
        );
    };
}
```

**Available Methods:**

| Method | Description |
|--------|-------------|
| `pageView(url?)` | Track page view (auto on route change) |
| `productImpression(productId, name?, position?)` | Product shown in list |
| `productView(productId, name?, variantId?)` | Product detail view |
| `search(query, resultCount?)` | Search performed (debounced) |
| `filterApplied(filters)` | Filter applied (debounced) |
| `addToCart(productId, variantId, price, qty, name?)` | Add to cart |
| `removeFromCart(productId, variantId, qty)` | Remove from cart |
| `beginCheckout(cartTotal)` | Checkout started |
| `paymentFailed(orderId?, error?)` | Payment failed |
| `flush()` | Manual queue flush |

### `useProductImpressions()` - Impression Tracking

```tsx
import { useProductImpressions } from '@/analytics';

function ProductList({ products }) {
    const impressionRef = useProductImpressions(products);
    
    return (
        <div ref={impressionRef}>
            {products.map((product, index) => (
                <div
                    key={product.id}
                    data-product-id={product.id}
                    data-product-name={product.name}
                    data-position={index}
                >
                    <ProductCard product={product} />
                </div>
            ))}
        </div>
    );
}
```

**Features:**
- ✅ Uses IntersectionObserver
- ✅ Tracks when products become visible
- ✅ Only tracks each product once (configurable)
- ✅ Auto-attaches to elements with `data-product-id`

---

## 🔐 Security & Safety

| Requirement | Status |
|-------------|--------|
| SDK never sends CustomerId | ✅ |
| SDK never sends auth tokens | ✅ |
| SDK never sends IP address | ✅ |
| Backend is the authority | ✅ |
| SDK only sends SessionId | ✅ |
| Session stored in localStorage (not cookies) | ✅ |

---

## 📁 Files Created

| File | Purpose |
|------|---------|
| `src/analytics/index.ts` | Main entry point |
| `src/analytics/eventTypes.ts` | TypeScript types & enums |
| `src/analytics/AnalyticsSession.ts` | Session management |
| `src/analytics/AnalyticsContextResolver.ts` | Context auto-resolution |
| `src/analytics/AnalyticsEventBuilder.ts` | Event builder & validator |
| `src/analytics/AnalyticsQueue.ts` | Queue, batch, retry logic |
| `src/analytics/AnalyticsClient.ts` | Network layer |
| `src/analytics/hooks/index.ts` | Hooks exports |
| `src/analytics/hooks/useAnalytics.ts` | Main analytics hook |
| `src/analytics/hooks/useProductImpressions.ts` | Impression tracking |

---

## 🧪 Acceptance Criteria

| Criteria | Status |
|----------|--------|
| No component calls analytics endpoint directly | ✅ |
| All analytics events go through SDK | ✅ |
| Events batch correctly (max 20) | ✅ |
| Offline mode works (localStorage) | ✅ |
| Page reload does not lose events | ✅ |
| No GA/Meta code exists | ✅ |
| No business logic in SDK | ✅ |
| SDK is tenant-agnostic | ✅ |

---

## 🚫 Hard Fail Conditions - VERIFIED CLEAN

- ❌ Any GA / gtag / fbq code → **NOT PRESENT**
- ❌ Provider-specific event naming → **NOT PRESENT**
- ❌ Components constructing AnalyticsEvent manually → **NOT PRESENT**
- ❌ Direct fetch() calls from UI → **NOT PRESENT**
- ❌ Session stored in cookies → **NOT PRESENT** (uses localStorage)

---

## 🏁 Phase 2 Success Statement

> **"The Storefront emits clean, deterministic, POS-owned analytics events through a single SDK, fully decoupled from any analytics provider."**

---

## 📊 Usage Example

### Complete Component Integration

```tsx
import { useEffect } from 'react';
import { useAnalytics } from '@/analytics';

function ProductDetailPage({ product }) {
    const analytics = useAnalytics();
    
    // Track product view on mount
    useEffect(() => {
        analytics.productView(
            product.id,
            product.name,
            selectedVariantId
        );
    }, [product.id]);
    
    const handleAddToCart = () => {
        // Business logic
        addToCart(product, quantity);
        
        // Analytics
        analytics.addToCart(
            product.id,
            selectedVariantId,
            product.price,
            quantity,
            product.name
        );
    };
    
    return (
        <div>
            <h1>{product.name}</h1>
            <button onClick={handleAddToCart}>
                Add to Cart
            </button>
        </div>
    );
}
```

### Checkout Flow

```tsx
import { useAnalytics } from '@/analytics';

function CheckoutPage() {
    const analytics = useAnalytics();
    
    // Track checkout start
    useEffect(() => {
        analytics.beginCheckout(cartTotal);
    }, []);
    
    const handlePaymentSuccess = () => {
        // PurchaseCompleted is emitted server-side by
        // AuthoritativePurchaseAnalyticsConsumer after payment confirmation.
        // Navigate to success page
    };
    
    const handlePaymentError = (orderId: string, error: string) => {
        analytics.paymentFailed(orderId, error);
        // Show error message
    };
}
```

---

## ⏭️ Phase 3 Preview (NOT IMPLEMENTED)

**Analytics Dispatchers**

Phase 3 will create dispatcher adapters for:
- Google Analytics 4
- Meta Conversions API
- Webhooks
- Admin Realtime Dashboard

**DO NOT IMPLEMENT PHASE 3 without explicit approval.**

---

## 📈 Event Flow Summary

```
User Action (click, navigate, etc.)
       ↓
React Component calls analytics.addToCart(...)
       ↓
useAnalytics hook
       ↓
AnalyticsEventBuilder.buildEvent()
       • Generate EventId
       • Attach SessionId
       • Resolve Context
       • Normalize Payload
       ↓
AnalyticsQueue.enqueue(event)
       • Add to memory queue
       • Persist to localStorage
       ↓
(After 10s or 20 events or page unload)
       ↓
AnalyticsQueue.flush()
       ↓
AnalyticsClient.sendAnalyticsBatch(events)
       • POST /api/storefront/analytics/events
       • Headers: X-Tenant-Id, X-Session-Id
       ↓
Backend (Phase 1)
       ↓
MassTransit → Redis + MongoDB
```

---

**Phase 2 Complete. Awaiting Phase 3 approval.**
