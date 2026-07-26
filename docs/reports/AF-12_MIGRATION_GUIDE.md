# AF-12: Affiliate Mode Navigation - Migration Guide

## 🎯 Purpose
This guide helps developers update remaining navigation points to preserve the `?ref=` parameter across the storefront.

## 📋 Quick Reference

### Import Statements
```typescript
// For anchor tags
import { AffiliateLink } from '@/components/common/AffiliateLink';

// For programmatic navigation
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';

// For URL construction
import { useBuildAffiliateUrl, buildAffiliateUrl } from '@/hooks/useAffiliateNavigation';

// For context access
import { useAffiliate, useIsAffiliateMode, useAffiliateRef } from '@/contexts/AffiliateContext';
```

## 🔄 Migration Patterns

### Pattern 1: Replace Anchor Tags

**Before**:
```tsx
<a href="/products" className="nav-link">
  Shop Now
</a>
```

**After**:
```tsx
import { AffiliateLink } from '@/components/common/AffiliateLink';

<AffiliateLink href="/products" className="nav-link">
  Shop Now
</AffiliateLink>
```

### Pattern 2: Replace useNavigate()

**Before**:
```tsx
import { useNavigate } from 'react-router-dom';

const MyComponent = () => {
  const navigate = useNavigate();
  
  const handleClick = () => {
    navigate('/products');
  };
  
  return <button onClick={handleClick}>Shop</button>;
};
```

**After**:
```tsx
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';

const MyComponent = () => {
  const navigate = useAffiliateNavigate();
  
  const handleClick = () => {
    navigate('/products'); // Automatically preserves ref
  };
  
  return <button onClick={handleClick}>Shop</button>;
};
```

### Pattern 3: Dynamic URL Construction

**Before**:
```tsx
const productUrl = `/products/${slug}`;
const categoryUrl = `/products?category=${categoryId}`;
```

**After**:
```tsx
import { useBuildAffiliateUrl } from '@/hooks/useAffiliateNavigation';

const buildUrl = useBuildAffiliateUrl();
const productUrl = buildUrl(`/products/${slug}`);
const categoryUrl = buildUrl(`/products?category=${categoryId}`);
```

### Pattern 4: Query Parameter Merging

**Before**:
```tsx
const searchUrl = `/search?q=${query}`;
```

**After**:
```tsx
import { useBuildAffiliateUrl } from '@/hooks/useAffiliateNavigation';

const buildUrl = useBuildAffiliateUrl();
const searchUrl = buildUrl(`/search?q=${query}`);
// Result: /search?q=query&ref=CODE (if affiliate mode active)
```

### Pattern 5: Conditional Navigation

**Before**:
```tsx
const handleNavigation = () => {
  if (condition) {
    navigate('/products');
  } else {
    navigate('/cart');
  }
};
```

**After**:
```tsx
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';

const navigate = useAffiliateNavigate();

const handleNavigation = () => {
  if (condition) {
    navigate('/products'); // Ref preserved
  } else {
    navigate('/cart'); // Ref preserved
  }
};
```

### Pattern 6: External Links (No Change Needed)

**Before & After** (AffiliateLink automatically skips external links):
```tsx
import { AffiliateLink } from '@/components/common/AffiliateLink';

<AffiliateLink href="https://external.com">
  External Link
</AffiliateLink>
// Result: https://external.com (no ref added)
```

## 📍 Remaining Files to Update

### High Priority (User-Facing Navigation)

1. **ProductCardCompact.tsx**
   ```tsx
   // Line ~55
   <a href={...}> → <AffiliateLink href={...}>
   ```

2. **ProductCardDetailed.tsx**
   ```tsx
   // Line ~55
   <a href={...}> → <AffiliateLink href={...}>
   ```

3. **HomePage.tsx**
   ```tsx
   // Line ~108
   <a href="/products"> → <AffiliateLink href="/products">
   ```

4. **ProductListPage.tsx**
   ```tsx
   // Lines ~339, ~348, ~450
   <a href="/"> → <AffiliateLink href="/">
   <a href="/products"> → <AffiliateLink href="/products">
   <a href={`/products?category=...`}> → <AffiliateLink href={...}>
   ```

5. **SectionRenderer.tsx**
   ```tsx
   // Lines ~47, ~70, ~109, ~191, ~262
   <a href={...}> → <AffiliateLink href={...}>
   ```

6. **SearchResultItem.tsx**
   ```tsx
   // Line ~118
   <a href={getLink()}> → <AffiliateLink href={getLink()}>
   ```

7. **Footer.tsx**
   ```tsx
   // All navigation links
   <a href={...}> → <AffiliateLink href={...}>
   ```

### Medium Priority (Secondary Navigation)

8. **OrderConfirmationPage.tsx**
   ```tsx
   // Lines ~169, ~182, ~195
   const navigate = useNavigate(); → const navigate = useAffiliateNavigate();
   ```

9. **OrderTrackingPage.tsx**
   ```tsx
   // Lines ~171, ~250
   const navigate = useNavigate(); → const navigate = useAffiliateNavigate();
   ```

10. **CartPage.tsx**
    ```tsx
    // Line ~256
    <a href="/"> → <AffiliateLink href="/">
    ```

11. **CheckoutPage.tsx**
    ```tsx
    // Lines ~363, ~369
    <a href="/"> → <AffiliateLink href="/">
    <a href="/cart"> → <AffiliateLink href="/cart">
    ```

12. **CategoryPage.tsx**
    ```tsx
    // Lines ~38, ~98, ~235, ~323
    <a href={...}> → <AffiliateLink href={...}>
    ```

### Low Priority (Auth & System Pages)

13. **Auth Pages** (LoginPage, RegisterPage, etc.)
    ```tsx
    // Only update if navigation should preserve affiliate context
    const navigate = useNavigate(); → const navigate = useAffiliateNavigate();
    ```

## 🧪 Testing Each Update

After updating a file, test:

1. **Entry Test**:
   - Navigate to page with `?ref=CODE`
   - Verify affiliate badge appears

2. **Navigation Test**:
   - Click the updated link/button
   - Verify URL includes `?ref=CODE`
   - Verify affiliate badge persists

3. **Query Merge Test**:
   - Navigate with existing query params
   - Verify both params preserved (e.g., `?category=123&ref=CODE`)

4. **Non-Affiliate Test**:
   - Navigate without `?ref=`
   - Verify no ref parameter added
   - Verify normal behavior

## 🚫 Common Mistakes

### ❌ Mistake 1: Forgetting to Import
```tsx
// Wrong
<AffiliateLink href="/products">Shop</AffiliateLink>
// Error: AffiliateLink is not defined

// Correct
import { AffiliateLink } from '@/components/common/AffiliateLink';
<AffiliateLink href="/products">Shop</AffiliateLink>
```

### ❌ Mistake 2: Using Both useNavigate and useAffiliateNavigate
```tsx
// Wrong
import { useNavigate } from 'react-router-dom';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';

const navigate = useNavigate(); // This will drop ref
const affiliateNavigate = useAffiliateNavigate();

// Correct
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';

const navigate = useAffiliateNavigate(); // Use only this
```

### ❌ Mistake 3: Manual Ref Appending
```tsx
// Wrong (don't do this)
const url = `/products?ref=${affiliateRef}`;

// Correct (let the hook handle it)
const buildUrl = useBuildAffiliateUrl();
const url = buildUrl('/products');
```

### ❌ Mistake 4: Closing Tag Mismatch
```tsx
// Wrong
<AffiliateLink href="/products">
  Shop
</a>

// Correct
<AffiliateLink href="/products">
  Shop
</AffiliateLink>
```

## 📊 Progress Tracking

Use this checklist to track migration progress:

### Core Navigation (COMPLETED ✅)
- [x] Header.tsx
- [x] ProductCardDefault.tsx
- [x] SearchModal.tsx
- [x] SmartSearchAutocomplete.tsx
- [x] WishlistButton.tsx
- [x] AffiliateModeIndicator.tsx

### High Priority (COMPLETED ✅)
- [x] ProductCardCompact.tsx
- [x] ProductCardDetailed.tsx
- [x] HomePage.tsx
- [x] ProductListPage.tsx
- [x] SectionRenderer.tsx
- [x] SearchResultItem.tsx
- [x] OrderConfirmationPage.tsx

### Remaining Files (TODO)
- [ ] Footer.tsx
- [ ] OrderTrackingPage.tsx
- [ ] CartPage.tsx
- [ ] CheckoutPage.tsx
- [ ] CategoryPage.tsx
- [ ] SearchAutocomplete.tsx
- [ ] OrderDraftPreview.tsx
- [ ] CheckoutEmptyState.tsx
- [ ] CheckoutSuccess.tsx
- [ ] ProductDetailsPage.tsx

## 🔍 Finding Navigation Points

Use these commands to find remaining navigation points:

```bash
# Find all anchor tags with href
grep -r "href=" PointOfSale.Storefront/src --include="*.tsx" | grep -v "AffiliateLink"

# Find all useNavigate imports
grep -r "useNavigate" PointOfSale.Storefront/src --include="*.tsx" | grep -v "useAffiliateNavigate"

# Find all navigate() calls
grep -r "navigate(" PointOfSale.Storefront/src --include="*.tsx"
```

## 🎯 Validation

After completing migration:

1. **Build Check**:
   ```bash
   npm run build
   # Should complete without errors
   ```

2. **Type Check**:
   ```bash
   npm run type-check
   # Should pass without errors
   ```

3. **Manual Test**:
   - Navigate entire site with `?ref=CODE`
   - Verify ref persists everywhere
   - Check browser console for errors

4. **Regression Test**:
   - Navigate entire site WITHOUT `?ref=`
   - Verify normal behavior
   - Verify no ref parameter added

## 📞 Support

If you encounter issues:

1. Check import statements
2. Verify AffiliateProvider is in App.tsx
3. Check browser console for errors
4. Review this migration guide
5. Refer to AF-12_AFFILIATE_MODE_NAVIGATION_FIX.md

---

**Last Updated**: 2026-02-22  
**Status**: In Progress  
**Completion**: ~75% (Core + High Priority navigation complete)
