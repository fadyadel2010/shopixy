# FE-1 Integration Checklist

## ✅ What Was Done

1. **Created New Components:**
   - ✅ `DiscoverTabEnhanced.jsx` - Intelligence-driven discover UI
   - ✅ `ProductCard.jsx` - Product card with badges
   - ✅ `ProductDrawer.jsx` - Product detail drawer
   - ✅ `CommissionBadge.jsx` - Commission rate badge
   - ✅ `StockBadge.jsx` - Stock status badge
   - ✅ `TrendBadge.jsx` - Trend indicator (placeholder)
   - ✅ `AffiliateLandingDiscover.jsx` - Public landing discover

2. **Updated Integration:**
   - ✅ `AffiliatePortal.jsx` - Now imports and uses `DiscoverTabEnhanced`

3. **Applied FE-1A Hardening:**
   - ✅ `ProductDrawer.jsx` - All 6 hardening measures
   - ✅ `ProductCard.jsx` - Defensive optional chaining & image fallback

## 🔧 Why You Don't See Changes

The frontend needs to be rebuilt! The React app is compiled and served from a build directory.

### To See Your Changes:

**Option 1: Development Server (Hot Reload)**
```bash
cd PointOfSale.Web
npm run dev
# or
yarn dev
```
Then visit: `http://localhost:5173/affiliate/portal?tab=discover`

**Option 2: Full Rebuild**
```bash
cd PointOfSale.Web
npm run build
# or
yarn build
```
Then restart your backend server.

## 🧪 Testing Steps

Once the dev server is running:

### 1. Navigate to Discover Tab
```
http://localhost:5173/affiliate/portal?tab=discover
```

### 2. You Should See:
- ✅ Product grid with intelligence cards
- ✅ Search bar at top
- ✅ "In Stock Only" filter button
- ✅ Commission badges on cards (gold/blue/gray)
- ✅ Stock badges on cards (green/orange/gray)
- ✅ "You earn X" on each card
- ✅ Sorting hint: "Sorted by: In-stock • Highest commission • Newest"

### 3. Click a Product Card:
- ✅ Drawer slides from right
- ✅ Image carousel (if multiple images)
- ✅ Price & commission breakdown
- ✅ Inventory status
- ✅ Variant list (if variants exist)
- ✅ Store info
- ✅ Copy affiliate link button
- ✅ Share buttons (WhatsApp, Facebook)
- ✅ Visit Storefront button

### 4. Test FE-1A Hardening:
- ✅ Press ESC → Drawer closes
- ✅ Click copy link → Checkmark appears
- ✅ Broken image → Placeholder shows
- ✅ Navigate images → Carousel works
- ✅ No console errors

## 🚨 Common Issues

### Issue 1: "Cannot find module DiscoverTabEnhanced"
**Solution:** Make sure the file exists at:
```
PointOfSale.Web/src/modules/affiliate/pages/portal/DiscoverTabEnhanced.jsx
```

### Issue 2: "affiliateApi.discoverProductsIntelligence is not a function"
**Solution:** Check that `affiliateApi.js` has the methods:
```javascript
discoverProductsIntelligence: ({ search, minCommissionRate, inStockOnly, page, pageSize }) => ...
getProductIntelligence: (productId) => ...
```

### Issue 3: API returns 404
**Solution:** Backend needs to be running with AF-22 endpoints:
```
GET /api/affiliate/discover/products
GET /api/affiliate/discover/products/{productId}
```

### Issue 4: No products showing
**Solution:** 
1. Check if AF-22 projection consumer is running
2. Check if MongoDB has `affiliate_product_intelligence` collection
3. Run backfill script: `AF-22-Backfill-ProductIntelligence.ps1`

### Issue 5: Old discover tab still showing
**Solution:** 
1. Clear browser cache
2. Hard refresh (Ctrl+Shift+R or Cmd+Shift+R)
3. Restart dev server

## 📋 Backend Requirements

For FE-1 to work, you need:

1. ✅ AF-22 backend implementation complete
2. ✅ MongoDB collection populated
3. ✅ API endpoints working:
   - `GET /api/affiliate/discover/products`
   - `GET /api/affiliate/discover/products/{productId}`
4. ✅ Projection consumer running

## 🎯 Quick Verification

Run this in browser console (after navigating to discover tab):

```javascript
// Check if new component is loaded
console.log('DiscoverTabEnhanced loaded:', 
  document.querySelector('[class*="grid"]') !== null
)

// Check API
fetch('/api/affiliate/discover/products?pageSize=1')
  .then(r => r.json())
  .then(d => console.log('API works:', d))
  .catch(e => console.error('API error:', e))
```

## ✅ Success Criteria

When everything works, you should see:

1. ✅ New discover UI with product cards
2. ✅ Commission badges (colored)
3. ✅ Stock badges
4. ✅ Search works
5. ✅ Filters work
6. ✅ Drawer opens on click
7. ✅ ESC closes drawer
8. ✅ Copy link works
9. ✅ No console errors
10. ✅ Mobile responsive

## 🚀 Next Steps

After verifying FE-1 works:

1. Test all hardening measures (FE-1A)
2. Test on mobile devices
3. Test with different product types (simple, variants, out of stock)
4. Test with broken images
5. Test with missing data fields
6. Performance test with many products
7. Deploy to staging
8. User acceptance testing
9. Deploy to production

## 📝 Files Changed

**Frontend:**
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx` (import changed)
- `PointOfSale.Web/src/modules/affiliate/pages/portal/DiscoverTabEnhanced.jsx` (new)
- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProductCard.jsx` (new)
- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx` (new)
- `PointOfSale.Web/src/modules/affiliate/pages/portal/badges/*.jsx` (new)
- `PointOfSale.Web/src/modules/affiliate/pages/landing/AffiliateLandingDiscover.jsx` (new)

**Backend:**
- None (FE-1 is frontend-only)

## 🎉 Status

- ✅ FE-1 Implementation: COMPLETE
- ✅ FE-1A Hardening: COMPLETE
- ✅ Integration: COMPLETE
- ⏳ Testing: PENDING (needs dev server restart)
- ⏳ Deployment: PENDING

**Action Required:** Restart dev server to see changes!
