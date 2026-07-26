# AF-23: Frontend Quick Reference

## 🚀 Quick Start

### Using the Readiness Hook

```javascript
import { useAffiliateReadiness } from '../hooks/useAffiliateReadiness'

function MyComponent() {
  const readiness = useAffiliateReadiness()

  if (readiness.loading) {
    return <div>Checking readiness...</div>
  }

  if (!readiness.isReady) {
    return <div>Affiliate not ready</div>
  }

  return <div>Affiliate features available</div>
}
```

### Using the Warning Component

```javascript
import AffiliateReadinessWarning from '../components/AffiliateReadinessWarning'

// Inline variant (for cards/modals)
<AffiliateReadinessWarning
  storefrontLive={readiness.storefrontLive}
  hasPaymentMethod={readiness.hasPaymentMethod}
  hasShippingMethod={readiness.hasShippingMethod}
  missingRequirements={readiness.missingRequirements}
  variant="inline"
/>

// Full-page variant (for empty states)
<AffiliateReadinessWarning
  storefrontLive={readiness.storefrontLive}
  hasPaymentMethod={readiness.hasPaymentMethod}
  hasShippingMethod={readiness.hasShippingMethod}
  missingRequirements={readiness.missingRequirements}
  variant="fullpage"
/>
```

---

## 📋 Readiness Object Structure

```typescript
{
  loading: boolean,           // True while fetching
  isReady: boolean,           // True if all prerequisites met
  storefrontLive: boolean,    // True if storefront is live
  hasPaymentMethod: boolean,  // True if ≥1 active payment method
  hasShippingMethod: boolean, // True if ≥1 active shipping method
  missingRequirements: string[], // Human-readable missing items
  error: Error | null         // Error if fetch failed
}
```

---

## 🎯 Common Patterns

### Pattern 1: Disable Feature When Not Ready

```javascript
const readiness = useAffiliateReadiness()

<button
  disabled={!readiness.isReady}
  onClick={handleEnableAffiliate}
  title={!readiness.isReady ? 'Complete setup requirements first' : ''}
>
  Enable Affiliate
</button>
```

### Pattern 2: Show Warning Instead of Feature

```javascript
const readiness = useAffiliateReadiness()

if (!readiness.isReady) {
  return <AffiliateReadinessWarning {...readiness} variant="inline" />
}

return <AffiliateFeature />
```

### Pattern 3: Conditional API Call

```javascript
const readiness = useAffiliateReadiness()

useEffect(() => {
  if (!readiness.loading && readiness.isReady) {
    fetchAffiliateData()
  }
}, [readiness.loading, readiness.isReady])
```

### Pattern 4: Block Save Operation

```javascript
const handleSave = async () => {
  if (!readiness.isReady) {
    setError('Affiliate system is not ready')
    return
  }
  
  await saveAffiliateSettings()
}
```

---

## 🔗 Navigation Paths

```javascript
// Storefront Settings
navigate('/settings?tab=storefront')

// Payment Methods
navigate('/payment-methods')

// Shipping Methods
navigate('/shipping-methods')
```

---

## 🎨 Styling Classes

### Warning Card
```jsx
<div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
  {/* Content */}
</div>
```

### Checklist Item (Complete)
```jsx
<div className="flex items-center gap-3">
  <div className="w-6 h-6 bg-green-100 rounded-full flex items-center justify-center">
    <svg className="w-4 h-4 text-green-600">✓</svg>
  </div>
  <span className="text-sm font-medium text-gray-900">Item complete</span>
</div>
```

### Checklist Item (Missing)
```jsx
<div className="flex items-center gap-3">
  <div className="w-6 h-6 bg-red-100 rounded-full flex items-center justify-center">
    <svg className="w-4 h-4 text-red-600">✗</svg>
  </div>
  <span className="text-sm font-medium text-gray-600">Item missing</span>
</div>
```

---

## 🐛 Debugging

### Check Readiness Response

```javascript
const readiness = useAffiliateReadiness()

console.log('[AF-23] Readiness:', {
  loading: readiness.loading,
  isReady: readiness.isReady,
  storefrontLive: readiness.storefrontLive,
  hasPaymentMethod: readiness.hasPaymentMethod,
  hasShippingMethod: readiness.hasShippingMethod,
  missing: readiness.missingRequirements
})
```

### Test API Endpoint

```bash
# Using curl
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5000/api/admin/affiliate/readiness

# Expected response
{
  "isReady": false,
  "storefrontLive": true,
  "hasPaymentMethod": false,
  "hasShippingMethod": false,
  "missingRequirements": [
    "At least one active payment method required",
    "At least one active shipping method required"
  ]
}
```

---

## ⚠️ Common Mistakes

### ❌ DON'T: Check readiness in render without loading check
```javascript
// BAD
if (!readiness.isReady) {
  return <Warning />
}
```

### ✅ DO: Always check loading first
```javascript
// GOOD
if (readiness.loading) {
  return <Loading />
}

if (!readiness.isReady) {
  return <Warning />
}
```

### ❌ DON'T: Assume readiness is always available
```javascript
// BAD
const isReady = readiness.isReady
```

### ✅ DO: Use optional chaining and defaults
```javascript
// GOOD
const isReady = readiness?.isReady ?? false
```

### ❌ DON'T: Bypass backend checks
```javascript
// BAD - Frontend only check
if (readiness.isReady) {
  await api.post('/enable-affiliate') // No backend validation
}
```

### ✅ DO: Let backend enforce
```javascript
// GOOD - Backend still validates
try {
  await api.post('/enable-affiliate')
} catch (err) {
  if (err.response?.data?.code === 'AFFILIATE_NOT_READY') {
    setError('System not ready')
  }
}
```

---

## 📱 Responsive Considerations

### Mobile-Friendly Buttons
```jsx
<button className="w-full sm:w-auto px-4 py-2 text-sm">
  Go to Settings
</button>
```

### Stacked Layout on Mobile
```jsx
<div className="flex flex-col sm:flex-row gap-4">
  {/* Content */}
</div>
```

---

## 🧪 Testing Helpers

### Mock Readiness Hook
```javascript
// In tests
jest.mock('../hooks/useAffiliateReadiness', () => ({
  useAffiliateReadiness: () => ({
    loading: false,
    isReady: false,
    storefrontLive: true,
    hasPaymentMethod: false,
    hasShippingMethod: false,
    missingRequirements: ['Payment method required'],
    error: null
  })
}))
```

### Test Scenarios
```javascript
describe('AffiliateFeature', () => {
  it('shows warning when not ready', () => {
    mockReadiness({ isReady: false })
    render(<AffiliateFeature />)
    expect(screen.getByText(/not ready/i)).toBeInTheDocument()
  })

  it('shows feature when ready', () => {
    mockReadiness({ isReady: true })
    render(<AffiliateFeature />)
    expect(screen.getByText(/enable affiliate/i)).toBeInTheDocument()
  })
})
```

---

## 🔄 State Management

### No Global State Needed
The hook manages its own state internally. No Redux/Context required.

### Refresh Pattern
```javascript
// If you need to refresh readiness
const [key, setKey] = useState(0)

<MyComponent key={key} />

// Trigger refresh
setKey(k => k + 1)
```

---

## 📚 Related Files

- **Hook:** `src/hooks/useAffiliateReadiness.js`
- **Component:** `src/components/AffiliateReadinessWarning.jsx`
- **Product Card:** `src/components/ProductAffiliateSettingsCard.jsx`
- **Affiliate List:** `src/modules/affiliate/pages/AdminAffiliatesList.jsx`

---

## 🆘 Support

### Common Issues

**Issue:** "Readiness always shows loading"
**Solution:** Check if backend endpoint exists and returns valid JSON

**Issue:** "Navigation buttons don't work"
**Solution:** Verify routes exist in router configuration

**Issue:** "Warning shows even when ready"
**Solution:** Check backend response format matches expected structure

**Issue:** "Console errors about missing props"
**Solution:** Ensure all required props passed to AffiliateReadinessWarning

---

**Last Updated:** 2026-02-25  
**Version:** 1.0.0  
**Status:** Production Ready
