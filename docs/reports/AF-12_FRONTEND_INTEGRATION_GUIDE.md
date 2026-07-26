# AF-12 Affiliate Mode — Frontend Integration Guide

**Date:** 2026-02-22  
**Audience:** Frontend Development Team  
**Status:** Ready for Implementation

---

## 📋 OVERVIEW

This guide explains how to integrate Affiliate Mode into the storefront frontend. When users visit the storefront with a referral code (`?ref=CODE`), they enter Affiliate Mode where they can only see and purchase products that are affiliate-enabled.

---

## 🎯 KEY REQUIREMENTS

### 1. Preserve `?ref` Parameter in All Navigation

**CRITICAL:** The `?ref=CODE` parameter MUST be preserved across all internal navigation to maintain Affiliate Mode throughout the user's session.

#### Why This Matters:
- Without the `?ref` parameter, users exit Affiliate Mode
- They can then see non-affiliate products
- This breaks commission attribution
- Affiliate partners lose revenue

#### Where to Preserve:
- ✅ Product links (category pages, search results, featured products)
- ✅ Category links (navigation menu, breadcrumbs)
- ✅ Cart page links
- ✅ Checkout flow links
- ✅ Internal redirects
- ✅ "Back to shop" links

---

## 💻 IMPLEMENTATION

### React/TypeScript Example

#### 1. Create a Utility Hook

```typescript
// hooks/useAffiliateMode.ts
import { useSearchParams } from 'react-router-dom';

export function useAffiliateMode() {
  const [searchParams] = useSearchParams();
  const refCode = searchParams.get('ref');
  
  return {
    isAffiliateMode: !!refCode,
    refCode: refCode || null,
    
    // Helper to append ref to any URL
    appendRef: (url: string): string => {
      if (!refCode) return url;
      
      const separator = url.includes('?') ? '&' : '?';
      return `${url}${separator}ref=${refCode}`;
    },
    
    // Helper to get ref as query string
    getRefQuery: (): string => {
      return refCode ? `?ref=${refCode}` : '';
    }
  };
}
```

#### 2. Use in Product Links

```typescript
// components/ProductCard.tsx
import { Link } from 'react-router-dom';
import { useAffiliateMode } from '@/hooks/useAffiliateMode';

export function ProductCard({ product }) {
  const { appendRef } = useAffiliateMode();
  
  return (
    <Link to={appendRef(`/products/${product.slug}`)}>
      <img src={product.image} alt={product.name} />
      <h3>{product.name}</h3>
      <p>{product.price}</p>
    </Link>
  );
}
```

#### 3. Use in Category Links

```typescript
// components/CategoryNav.tsx
import { Link } from 'react-router-dom';
import { useAffiliateMode } from '@/hooks/useAffiliateMode';

export function CategoryNav({ categories }) {
  const { appendRef } = useAffiliateMode();
  
  return (
    <nav>
      {categories.map(category => (
        <Link 
          key={category.id} 
          to={appendRef(`/categories/${category.slug}`)}
        >
          {category.name}
        </Link>
      ))}
    </nav>
  );
}
```

#### 4. Use in Programmatic Navigation

```typescript
// pages/ProductPage.tsx
import { useNavigate } from 'react-router-dom';
import { useAffiliateMode } from '@/hooks/useAffiliateMode';

export function ProductPage() {
  const navigate = useNavigate();
  const { appendRef } = useAffiliateMode();
  
  const handleAddToCart = async () => {
    await addToCart(productId);
    // Navigate to cart while preserving ref
    navigate(appendRef('/cart'));
  };
  
  return (
    <button onClick={handleAddToCart}>
      Add to Cart
    </button>
  );
}
```

---

## 🎨 VISUAL INDICATORS

### 1. Detect Affiliate Mode from Response Headers

The backend adds these headers to all product-related responses:

```
X-Affiliate-Mode: true
X-Affiliate-Account-Id: {guid}
```

#### Example: Axios Interceptor

```typescript
// api/axios.ts
import axios from 'axios';

const api = axios.create({
  baseURL: process.env.REACT_APP_API_URL
});

// Intercept responses to detect affiliate mode
api.interceptors.response.use(response => {
  const isAffiliateMode = response.headers['x-affiliate-mode'] === 'true';
  const affiliateAccountId = response.headers['x-affiliate-account-id'];
  
  if (isAffiliateMode) {
    // Store in context or state management
    window.__AFFILIATE_MODE__ = {
      active: true,
      accountId: affiliateAccountId
    };
  }
  
  return response;
});

export default api;
```

### 2. Show Affiliate Mode Badge

```typescript
// components/AffiliateModeBadge.tsx
import { useAffiliateMode } from '@/hooks/useAffiliateMode';

export function AffiliateModeBadge() {
  const { isAffiliateMode } = useAffiliateMode();
  
  if (!isAffiliateMode) return null;
  
  return (
    <div className="affiliate-badge">
      🎯 Shopping via Partner
    </div>
  );
}
```

#### CSS Example

```css
.affiliate-badge {
  position: fixed;
  top: 80px;
  right: 20px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 8px 16px;
  border-radius: 20px;
  font-size: 14px;
  font-weight: 600;
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.3);
  z-index: 1000;
  animation: slideIn 0.3s ease-out;
}

@keyframes slideIn {
  from {
    transform: translateX(100%);
    opacity: 0;
  }
  to {
    transform: translateX(0);
    opacity: 1;
  }
}
```

---

## 🧪 TESTING CHECKLIST

### Manual Testing

#### Test 1: Initial Entry
1. Visit: `https://storefront.com/?ref=TESTCODE`
2. ✅ Verify badge appears: "🎯 Shopping via Partner"
3. ✅ Verify only affiliate products are shown

#### Test 2: Product Navigation
1. Click on a product from category page
2. ✅ Verify URL contains `?ref=TESTCODE`
3. ✅ Verify badge still visible
4. ✅ Verify product details load correctly

#### Test 3: Category Navigation
1. Click on a category link
2. ✅ Verify URL contains `?ref=TESTCODE`
3. ✅ Verify badge still visible
4. ✅ Verify only affiliate products shown

#### Test 4: Search
1. Use search bar
2. ✅ Verify URL contains `?ref=TESTCODE`
3. ✅ Verify badge still visible
4. ✅ Verify only affiliate products in results

#### Test 5: Cart Flow
1. Add product to cart
2. Navigate to cart page
3. ✅ Verify URL contains `?ref=TESTCODE`
4. ✅ Verify badge still visible

#### Test 6: Checkout Flow
1. Proceed to checkout
2. ✅ Verify URL contains `?ref=TESTCODE`
3. ✅ Verify badge still visible
4. ✅ Verify order completes successfully

#### Test 7: Browser Refresh
1. Refresh page while in Affiliate Mode
2. ✅ Verify `?ref=TESTCODE` persists in URL
3. ✅ Verify badge still visible

#### Test 8: Direct URL Access
1. Copy product URL with `?ref=TESTCODE`
2. Open in new tab
3. ✅ Verify Affiliate Mode activates
4. ✅ Verify badge appears

---

## ⚠️ COMMON PITFALLS

### ❌ DON'T: Use `window.location.href` without preserving ref

```typescript
// BAD - Loses ref parameter
window.location.href = '/products/laptop';
```

```typescript
// GOOD - Preserves ref parameter
const { appendRef } = useAffiliateMode();
window.location.href = appendRef('/products/laptop');
```

### ❌ DON'T: Hard-code links without ref

```typescript
// BAD - Hard-coded link loses ref
<a href="/cart">View Cart</a>
```

```typescript
// GOOD - Dynamic link preserves ref
const { appendRef } = useAffiliateMode();
<a href={appendRef('/cart')}>View Cart</a>
```

### ❌ DON'T: Remove query params on navigation

```typescript
// BAD - Strips all query params
navigate('/products', { replace: true });
```

```typescript
// GOOD - Preserves ref param
const { appendRef } = useAffiliateMode();
navigate(appendRef('/products'), { replace: true });
```

---

## 🔍 DEBUGGING

### Check if Affiliate Mode is Active

```typescript
// In browser console
const params = new URLSearchParams(window.location.search);
console.log('Ref Code:', params.get('ref'));
console.log('Affiliate Mode:', !!params.get('ref'));
```

### Check Response Headers

```typescript
// In browser console (after API call)
fetch('/api/storefront/products/discover?ref=CODE', {
  headers: { 'X-Tenant-Id': 'your-tenant-id' }
})
.then(response => {
  console.log('X-Affiliate-Mode:', response.headers.get('X-Affiliate-Mode'));
  console.log('X-Affiliate-Account-Id:', response.headers.get('X-Affiliate-Account-Id'));
});
```

### Verify Product Filtering

```typescript
// Check if non-affiliate products are hidden
const products = await fetchProducts({ ref: 'CODE' });
console.log('All products are affiliate-enabled:', 
  products.every(p => p.isAffiliateEnabled === true)
);
```

---

## 📊 ANALYTICS INTEGRATION

### Track Affiliate Mode Sessions

```typescript
// analytics/affiliate.ts
export function trackAffiliateModeSession(refCode: string) {
  // Google Analytics
  gtag('event', 'affiliate_mode_activated', {
    ref_code: refCode,
    timestamp: new Date().toISOString()
  });
  
  // Custom analytics
  analytics.track('Affiliate Mode Activated', {
    refCode,
    timestamp: Date.now()
  });
}
```

### Track Affiliate Conversions

```typescript
// pages/OrderConfirmation.tsx
import { useAffiliateMode } from '@/hooks/useAffiliateMode';

export function OrderConfirmation({ order }) {
  const { isAffiliateMode, refCode } = useAffiliateMode();
  
  useEffect(() => {
    if (isAffiliateMode && refCode) {
      // Track affiliate conversion
      analytics.track('Affiliate Order Completed', {
        orderId: order.id,
        refCode,
        orderTotal: order.total,
        timestamp: Date.now()
      });
    }
  }, [order, isAffiliateMode, refCode]);
  
  return <div>Order confirmed!</div>;
}
```

---

## 🚀 DEPLOYMENT CHECKLIST

### Pre-Deployment
- [ ] Implement `useAffiliateMode` hook
- [ ] Update all product links to use `appendRef`
- [ ] Update all category links to use `appendRef`
- [ ] Update all navigation links to use `appendRef`
- [ ] Add affiliate mode badge component
- [ ] Add response header detection
- [ ] Add analytics tracking

### Testing
- [ ] Test all navigation flows with `?ref=CODE`
- [ ] Test browser refresh preserves ref
- [ ] Test direct URL access with ref
- [ ] Test cart flow with ref
- [ ] Test checkout flow with ref
- [ ] Test badge visibility
- [ ] Test analytics tracking

### Post-Deployment
- [ ] Monitor affiliate mode activation rate
- [ ] Monitor ref parameter drop-off rate
- [ ] Monitor affiliate conversion rate
- [ ] Verify commission attribution is correct

---

## 📞 SUPPORT

### Backend API Endpoints

All storefront product endpoints support Affiliate Mode:

- `GET /api/storefront/products/{slug}?ref=CODE`
- `GET /api/storefront/products/by-id/{id}?ref=CODE`
- `GET /api/storefront/products?ref=CODE`
- `GET /api/storefront/products/search?ref=CODE`
- `GET /api/storefront/products/discover?ref=CODE`
- `POST /api/storefront/cart/items?ref=CODE`
- `POST /api/storefront/checkout?ref=CODE`

### Response Headers

When `?ref=CODE` is valid, all responses include:

```
X-Affiliate-Mode: true
X-Affiliate-Account-Id: {guid}
```

### Error Codes

If a non-affiliate product is accessed in Affiliate Mode:

- Product endpoints: `404 Not Found`
- Cart endpoint: `400 Bad Request` with `PRODUCT_NOT_ELIGIBLE` error code
- Checkout endpoint: `400 Bad Request` with `CHECKOUT_CART_INVALID` error code

---

## 📚 ADDITIONAL RESOURCES

- Backend Implementation Guide: `AF-12_HARDENING_IMPLEMENTATION_GUIDE.md`
- Security Audit Report: `AF-12_DEEP_AUDIT_REPORT.md`
- Completion Summary: `AF-12_HARDENING_COMPLETE.md`

---

**Frontend Integration Guide Completed:** 2026-02-22  
**Next Action:** Implement in storefront frontend  
**Estimated Time:** 4-6 hours for full implementation
