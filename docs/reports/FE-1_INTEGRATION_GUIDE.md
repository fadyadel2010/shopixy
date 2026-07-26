# FE-1: Integration Guide

## Quick Start (5 Minutes)

### Step 1: Replace Discover Tab

Open `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx`

**Find:**
```jsx
import DiscoverTab from './portal/DiscoverTab'
```

**Replace with:**
```jsx
import DiscoverTabEnhanced from './portal/DiscoverTabEnhanced'
```

**Find:**
```jsx
case 'discover': return <DiscoverTab initialSearch={searchParams.get('q') || ''} />
```

**Replace with:**
```jsx
case 'discover': return <DiscoverTabEnhanced />
```

### Step 2: Test

1. Navigate to `/affiliate/portal?tab=discover`
2. Search for products
3. Click a product card
4. Verify drawer opens
5. Copy affiliate link
6. Test share buttons

Done! ✅

## Advanced Integration

### Feature Flag (Gradual Rollout)

```jsx
// In AffiliatePortal.jsx
import DiscoverTab from './portal/DiscoverTab'
import DiscoverTabEnhanced from './portal/DiscoverTabEnhanced'

// Add state
const [useEnhancedDiscover, setUseEnhancedDiscover] = useState(
    localStorage.getItem('fe1_enhanced') === 'true'
)

// In renderTab()
case 'discover': 
    return useEnhancedDiscover 
        ? <DiscoverTabEnhanced /> 
        : <DiscoverTab initialSearch={searchParams.get('q') || ''} />
```

**Enable for user:**
```javascript
localStorage.setItem('fe1_enhanced', 'true')
```

**Disable for user:**
```javascript
localStorage.removeItem('fe1_enhanced')
```

### Landing Page Integration

Create a new landing page route:

```jsx
// In your router
import AffiliateLandingDiscover from './modules/affiliate/pages/landing/AffiliateLandingDiscover'

<Route 
    path="/affiliate/discover" 
    element={
        <AffiliateLandingDiscover 
            onSignUpClick={() => navigate('/affiliate/register')} 
        />
    } 
/>
```

Or embed in existing landing page:

```jsx
import AffiliateLandingDiscover from './modules/affiliate/pages/landing/AffiliateLandingDiscover'

function AffiliateLanding() {
    return (
        <div>
            <Hero />
            <Features />
            <AffiliateLandingDiscover 
                onSignUpClick={() => navigate('/affiliate/register')} 
            />
            <Testimonials />
            <Footer />
        </div>
    )
}
```

## Customization

### Adjust Page Size

```jsx
// In DiscoverTabEnhanced.jsx
const pageSize = 20 // Change to 12, 16, 24, etc.
```

### Add Commission Filter

```jsx
// In DiscoverTabEnhanced.jsx

// Add UI
<select 
    value={minCommissionRate || ''} 
    onChange={(e) => setMinCommissionRate(e.target.value ? parseFloat(e.target.value) : null)}
    className="px-4 py-3 rounded-xl bg-white/[0.03] border border-white/[0.08] text-white text-sm"
>
    <option value="">All Commissions</option>
    <option value="0.1">10%+</option>
    <option value="0.15">15%+</option>
    <option value="0.2">20%+</option>
    <option value="0.25">25%+</option>
</select>
```

### Customize Badge Colors

```jsx
// In CommissionBadge.jsx
// Change thresholds or colors
if (commissionRate > 0.30) { // Changed from 0.25
    return (
        <span className="... bg-purple-500/10 text-purple-400 ...">
            {percentage}% Premium
        </span>
    )
}
```

### Add Category Filter

```jsx
// In DiscoverTabEnhanced.jsx

const [categoryId, setCategoryId] = useState(null)

// In fetchProducts
affiliateApi.discoverProductsIntelligence({
    search: debouncedQuery || undefined,
    inStockOnly,
    minCommissionRate,
    categoryId, // Add this
    page,
    pageSize
})

// Add UI
<select 
    value={categoryId || ''} 
    onChange={(e) => setCategoryId(e.target.value || null)}
>
    <option value="">All Categories</option>
    {/* Load categories from API */}
</select>
```

## Troubleshooting

### Products Not Loading

**Check:**
1. Backend is running
2. MongoDB has data (run backfill)
3. API endpoint returns 200
4. Browser console for errors

**Test API directly:**
```bash
curl -H "Authorization: Bearer {token}" \
  "http://localhost:5000/api/affiliate/discover/products?page=1&pageSize=10"
```

### Drawer Not Opening

**Check:**
1. `selectedProductId` state is set
2. `ProductDrawer` component is rendered
3. `AnimatePresence` is wrapping drawer
4. No console errors

**Debug:**
```jsx
console.log('Selected Product ID:', selectedProductId)
```

### Affiliate Link Invalid

**Check:**
1. `referralCode` is loaded from profile
2. `storefrontUrl` is present in product data
3. Link format: `{storefrontUrl}?ref={code}`

**Debug:**
```jsx
console.log('Referral Code:', referralCode)
console.log('Storefront URL:', product.storefrontUrl)
console.log('Affiliate Link:', affiliateLink)
```

### Images Not Loading

**Check:**
1. Image URLs are valid
2. CORS is configured
3. Images exist on server

**Fallback:**
```jsx
{product.images && product.images.length > 0 ? (
    <img 
        src={product.images[0]} 
        alt={product.name}
        onError={(e) => {
            e.target.style.display = 'none'
            // Show fallback icon
        }}
    />
) : (
    <Package className="w-10 h-10 text-gray-700" />
)}
```

### Badges Not Showing

**Check:**
1. Badge components are imported
2. Props are passed correctly
3. Tailwind classes are compiled

**Debug:**
```jsx
console.log('Commission Rate:', product.commissionRate)
console.log('Is Out of Stock:', product.isOutOfStock)
console.log('Low Stock:', product.lowStock)
```

## Performance Optimization

### Lazy Load Images

Already implemented:
```jsx
<img loading="lazy" ... />
```

### Reduce Page Size

```jsx
const pageSize = 12 // Smaller pages load faster
```

### Add Caching

```jsx
// In affiliateApi.js
const cache = new Map()

discoverProductsIntelligence: (params) => {
    const cacheKey = JSON.stringify(params)
    if (cache.has(cacheKey)) {
        return Promise.resolve(cache.get(cacheKey))
    }
    return affiliateAxios.get('/affiliate/discover/products', { params })
        .then(r => {
            cache.set(cacheKey, r.data)
            return r.data
        })
}
```

### Debounce Search

Already implemented (400ms):
```jsx
useEffect(() => {
    const timer = setTimeout(() => {
        setDebouncedQuery(searchQuery)
        setPage(1)
    }, 400)
    return () => clearTimeout(timer)
}, [searchQuery])
```

## Accessibility

### Keyboard Navigation

All interactive elements are keyboard accessible:
- Tab to navigate
- Enter to activate
- Escape to close drawer

### Screen Readers

Add ARIA labels:
```jsx
<button 
    aria-label="View product details"
    onClick={onClick}
>
    View Details
</button>
```

### Focus Management

Drawer should trap focus:
```jsx
// In ProductDrawer.jsx
useEffect(() => {
    if (productId) {
        // Focus first element in drawer
        const firstElement = document.querySelector('.drawer-content button')
        firstElement?.focus()
    }
}, [productId])
```

## Testing

### Manual Testing Checklist

- [ ] Search products
- [ ] Filter by stock
- [ ] Click product card
- [ ] Drawer opens
- [ ] Navigate images
- [ ] View variants
- [ ] Copy link
- [ ] Share WhatsApp
- [ ] Share Facebook
- [ ] Visit storefront
- [ ] Close drawer
- [ ] Pagination
- [ ] Empty state
- [ ] Loading state
- [ ] Mobile responsive

### Automated Testing

```jsx
// Example test
import { render, screen, fireEvent } from '@testing-library/react'
import DiscoverTabEnhanced from './DiscoverTabEnhanced'

test('opens drawer when product card is clicked', async () => {
    render(<DiscoverTabEnhanced />)
    
    // Wait for products to load
    await screen.findByText('Product Name')
    
    // Click product card
    fireEvent.click(screen.getByText('View Details'))
    
    // Drawer should open
    expect(screen.getByText('Promote This Product')).toBeInTheDocument()
})
```

## Deployment

### Build

```bash
cd PointOfSale.Web
npm run build
```

### Deploy

1. Deploy built files to web server
2. Update `AffiliatePortal.jsx` to use enhanced version
3. Clear CDN cache if applicable
4. Test in production

### Rollback

If issues arise:

```jsx
// Revert AffiliatePortal.jsx
import DiscoverTab from './portal/DiscoverTab'
case 'discover': return <DiscoverTab initialSearch={searchParams.get('q') || ''} />
```

Redeploy. Old version works immediately.

## Monitoring

### Track Usage

```jsx
// In DiscoverTabEnhanced.jsx
useEffect(() => {
    // Track page view
    analytics.track('Affiliate Discover Viewed', {
        page,
        search: debouncedQuery,
        inStockOnly
    })
}, [page, debouncedQuery, inStockOnly])

// In ProductDrawer.jsx
useEffect(() => {
    if (productId) {
        // Track drawer open
        analytics.track('Product Drawer Opened', {
            productId
        })
    }
}, [productId])
```

### Error Tracking

```jsx
// In fetchProducts
.catch((error) => {
    console.error('Failed to fetch products:', error)
    // Send to error tracking service
    errorTracker.captureException(error)
    setData(null)
})
```

## Support

For issues or questions:
1. Check browser console for errors
2. Verify API is returning data
3. Test with old DiscoverTab (rollback)
4. Review `FE-1_IMPLEMENTATION_SUMMARY.md`

## Success Criteria

✅ Discover shows enriched cards  
✅ Drawer opens with full info  
✅ Affiliate links work  
✅ No console errors  
✅ No regression  
✅ Mobile responsive  
✅ Zero backend changes  

FE-1 Complete! 🎉
