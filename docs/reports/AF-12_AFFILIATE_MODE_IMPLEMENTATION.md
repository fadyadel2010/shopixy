# AF-12 — Affiliate Mode Storefront Implementation

**Date:** 2026-02-22  
**Phase:** AF-12 - Products-Only Experience  
**Status:** 🟡 IN PROGRESS (Core Infrastructure Complete)

---

## 🎯 OBJECTIVE

Introduce a dedicated Affiliate Mode inside the Storefront that guarantees affiliates only send traffic to commission-eligible products, eliminating commission leakage and ensuring economic fairness.

---

## 🏗 ARCHITECTURE OVERVIEW

```
Affiliate Link (?ref=CODE)
    ↓
AffiliateReferralMiddleware
    ↓
Validates referral code
    ↓
Sets HttpContext.Items["AffiliateMode"] = true
Sets HttpContext.Items["AffiliateAccountId"] = {guid}
    ↓
Storefront Queries
    ↓
Append Filter: IsAffiliateEnabled = true
    ↓
Only affiliate-enabled products visible
```

**Key Principle:** Affiliate Mode is request-scoped behavioral state, NOT a new storefront.

---

## ✅ COMPLETED COMPONENTS

### 1. Core Infrastructure

#### **AffiliateModeResolver** ✅
**File:** `PointOfSale.API/Helpers/AffiliateModeResolver.cs`

**Purpose:** Provides request-scoped detection of Affiliate Mode.

**Methods:**
- `IsAffiliateMode(HttpContext)` - Returns true if middleware activated Affiliate Mode
- `GetAffiliateAccountId(HttpContext)` - Returns AffiliateAccountId if in Affiliate Mode
- `SetAffiliateMode(HttpContext, Guid)` - Internal method called by middleware

**Security:**
- Frontend cannot activate Affiliate Mode
- No query param like `?affiliate=true`
- Server-side enforcement only
- Request-scoped only (NOT persisted)

#### **AffiliateReferralMiddleware** ✅
**File:** `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs`

**Changes:**
- Added `using PointOfSale.API.Helpers`
- After validating `?ref=CODE`, calls `AffiliateModeResolver.SetAffiliateMode(context, affiliateAccountId)`
- Logs: `[Affiliate:Mode] Activated | AffiliateId: {id} | Ref: {code} | TenantId: {tenant}`

**Behavior:**
- Validates referral code against database
- Only activates for Active affiliate accounts
- Sets request-scoped flag (no cookies, no database)
- Fire-and-forget event publishing unchanged

### 2. Data Layer - MongoDB Repository

#### **IProductReadRepository Interface** ✅
**File:** `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`

**Updated Method Signatures:**
```csharp
// All methods now support affiliateModeOnly parameter
Task<(List<ProductDocument> Items, long TotalCount)> GetProductListAsync(
    ..., bool affiliateModeOnly = false, ...);

Task<(List<ProductDocument> Items, long TotalCount)> GetProductListByCategoryTreeAsync(
    ..., bool affiliateModeOnly = false, ...);

Task<(List<ProductDocument> Items, long TotalCount)> SearchProductsAsync(
    ..., bool affiliateModeOnly = false, ...);

Task<List<ProductDocument>> GetByProductIdsAsync(
    ..., bool affiliateModeOnly = false, ...);

Task<List<ProductDocument>> GetFeaturedProductsAsync(
    ..., bool affiliateModeOnly = false, ...);

Task<List<ProductDocument>> GetNewArrivalsAsync(
    ..., bool affiliateModeOnly = false, ...);
```

**Implementation:**
- All methods append `IsAffiliateEnabled = true` filter when `affiliateModeOnly = true`
- Filter applied at MongoDB query level (server-side enforcement)
- No performance impact (uses existing indexes)
- Backward compatible (default parameter = false)

---

## 🔄 REMAINING WORK

### 3. Service Layer Updates (PARTIALLY COMPLETE)

#### **StorefrontProductService** ✅ COMPLETE
**File:** `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`

**Changes Made:**
```csharp
// Updated interface signatures
Task<ProductReadDto?> GetProductBySlugAsync(..., bool affiliateModeOnly = false, ...);
Task<ProductReadDto?> GetProductByIdAsync(..., bool affiliateModeOnly = false, ...);
Task<PagedResult<ProductListItemDto>> GetProductListAsync(..., bool affiliateModeOnly = false, ...);
```

**Implementation:**
- `GetProductBySlugAsync` - Filters cached and MongoDB results by IsAffiliateEnabled
- `GetProductByIdAsync` - Filters cached and MongoDB results by IsAffiliateEnabled
- `GetProductListAsync` - Passes affiliateModeOnly to repository, filters cached results
- Returns null for non-affiliate products when Affiliate Mode is active
- Logs filtering actions for observability

#### **ProductSearchService** ✅ COMPLETE
**File:** `PointOfSale.API/Services/Storefront/ProductSearchService.cs`

**Changes Made:**
```csharp
// Updated interface signature
Task<SearchResult<ProductListItemDto>> SearchProductsAsync(
    ..., bool affiliateModeOnly = false, ...);
```

**Implementation:**
- Passes `affiliateModeOnly` to Elasticsearch service
- Passes `affiliateModeOnly` to MongoDB hydration
- Passes `affiliateModeOnly` to MongoDB fallback search
- All search paths respect Affiliate Mode filter
- Logs Affiliate Mode status in search operations

#### **StorefrontHomeService** 🔴 TODO
**File:** `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`

**Required Changes:**
```csharp
public async Task<ProductReadDto?> GetProductBySlugAsync(
    Guid tenantId, 
    string slug,
    bool affiliateModeOnly = false,  // ADD THIS
    CancellationToken cancellationToken = default)
{
    // Pass affiliateModeOnly to FindBySlugAsync
    // If Affiliate Mode and product not affiliate-enabled → return null
}
```

**Methods to Update:**
- `GetProductBySlugAsync` - Add affiliateModeOnly parameter
- `GetProductByIdAsync` - Add affiliateModeOnly parameter
- Pass parameter through to repository calls

#### **ProductSearchService** 🔴 TODO
**File:** `PointOfSale.API/Services/Storefront/ProductSearchService.cs`

**Required Changes:**
```csharp
public async Task<SearchResult<ProductListItemDto>> SearchProductsAsync(
    Guid tenantId,
    ProductSearchQuery query,
    string correlationId,
    bool affiliateModeOnly = false,  // ADD THIS
    CancellationToken cancellationToken = default)
{
    // Pass to Elasticsearch service
    // Pass to MongoDB fallback
}
```

#### **StorefrontHomeService** 🔴 TODO
**File:** `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`

**Required Changes:**
- `GetFeaturedProductsAsync` - Add affiliateModeOnly parameter
- `GetNewArrivalsAsync` - Add affiliateModeOnly parameter
- `GetBestSellersAsync` - Add affiliateModeOnly parameter (if exists)

#### **ProductDiscoveryService** 🔴 TODO
**File:** `PointOfSale.API/Services/Storefront/ProductDiscoveryService.cs`

**Required Changes:**
- `DiscoverProductsAsync` - Add affiliateModeOnly parameter
- Pass through to repository/Elasticsearch

### 4. Controller Layer Updates (REQUIRED)

#### **StorefrontProductsController** 🔴 TODO
**File:** `PointOfSale.API/Controllers/StorefrontProductsController.cs`

**Required Changes:**
```csharp
[HttpGet("{slug}")]
public async Task<IActionResult> GetProductBySlug(string slug, ...)
{
    // Detect Affiliate Mode
    var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);
    
    // Pass to service
    var product = await _productService.GetProductBySlugAsync(
        tenantId, slug, affiliateMode, cancellationToken);
    
    // If Affiliate Mode and product not found → return 404 or redirect
    if (affiliateMode && product == null)
    {
        return NotFound(new { message = "Product not available in affiliate mode" });
    }
}
```

**Methods to Update:**
- `GetProductBySlug` - Add Affiliate Mode detection
- `GetProductById` - Add Affiliate Mode detection
- `SearchProducts` - Add Affiliate Mode detection
- `DiscoverProducts` - Add Affiliate Mode detection

#### **StorefrontHomeController** 🔴 TODO
**File:** `PointOfSale.API/Controllers/StorefrontHomeController.cs`

**Required Changes:**
```csharp
[HttpGet("featured-products")]
public async Task<IActionResult> GetFeaturedProducts(...)
{
    var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);
    var products = await _homeService.GetFeaturedProductsAsync(
        tenantId, affiliateMode, traceId, cancellationToken);
    return Ok(products);
}
```

**Methods to Update:**
- `GetFeaturedProducts` - Add Affiliate Mode detection
- `GetNewArrivals` - Add Affiliate Mode detection
- `GetBestSellers` - Add Affiliate Mode detection

#### **StorefrontCategoriesController** 🔴 TODO
**File:** `PointOfSale.API/Controllers/StorefrontCategoriesController.cs`

**Required Changes:**
- `GetCategoryBySlug` - Add Affiliate Mode detection
- Pass affiliateModeOnly to product queries
- Filter out categories with zero affiliate products (optional)

### 5. Elasticsearch Integration (REQUIRED)

#### **ProductSearchIndexService** 🔴 TODO
**File:** `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs`

**Required Changes:**
```csharp
public async Task<ProductSearchResult> SearchAsync(
    Guid tenantId,
    string query,
    int page,
    int pageSize,
    Guid? categoryId = null,
    decimal? minPrice = null,
    decimal? maxPrice = null,
    PromotionRankingConfig? promotionRankingConfig = null,
    bool affiliateModeOnly = false,  // ADD THIS
    CancellationToken cancellationToken = default)
{
    // Add filter to Elasticsearch query
    if (affiliateModeOnly)
    {
        filters.Add(f => f.Term(t => t.Field(p => p.IsAffiliateEnabled).Value(true)));
    }
}
```

### 6. Frontend Integration (OPTIONAL)

#### **Visual Indicator**
- Add badge in storefront header: "🎯 Affiliate Store"
- Show when `AffiliateMode` is active
- Lets customer know they're browsing curated collection

#### **Home Behavior**
- When AffiliateMode: `/` → `/affiliate-products`
- Or: Show only affiliate products on home page

#### **Navigation**
- Hide categories with zero affiliate products
- Hide promotional banners unrelated to affiliate products

---

## 🛡️ SECURITY & INVARIANTS

✅ **Enforced:**
- Affiliate Mode only activates through middleware
- Frontend never controls the flag
- No query param like `?affiliate=true`
- No cookies for AffiliateMode
- All enforcement server-side
- Request-scoped only (NOT persisted)

❌ **Forbidden:**
- Showing non-affiliate products in affiliate mode
- Client-side filtering only
- Creating a second storefront
- Storing AffiliateMode in database
- Allowing checkout of non-affiliate products when AffiliateMode=true

---

## 📊 OBSERVABILITY

### Metrics to Add
```csharp
// In middleware or controllers
_metrics.IncrementCounter("affiliate.mode.requests_total", 
    new[] { ("tenant_id", tenantId.ToString()) });

_metrics.IncrementCounter("affiliate.mode.blocked_product_requests_total",
    new[] { ("tenant_id", tenantId.ToString()), ("product_id", productId.ToString()) });
```

### Logging
```csharp
_logger.LogInformation(
    "[AffiliateMode] ProductBlocked | ProductId: {ProductId} | AffiliateId: {AffiliateId} | TenantId: {TenantId}",
    productId, affiliateAccountId, tenantId);
```

---

## 🧪 TESTING REQUIREMENTS

### Backend Tests

#### **Unit Tests** 🔴 TODO
```csharp
[Fact]
public void AffiliateModeResolver_IsAffiliateMode_ReturnsTrueWhenSet()
{
    var context = new DefaultHttpContext();
    AffiliateModeResolver.SetAffiliateMode(context, Guid.NewGuid());
    Assert.True(AffiliateModeResolver.IsAffiliateMode(context));
}

[Fact]
public void AffiliateModeResolver_IsAffiliateMode_ReturnsFalseWhenNotSet()
{
    var context = new DefaultHttpContext();
    Assert.False(AffiliateModeResolver.IsAffiliateMode(context));
}

[Fact]
public async Task GetProductListAsync_WithAffiliateModeOnly_FiltersCorrectly()
{
    // Arrange: Create products with IsAffiliateEnabled = true/false
    // Act: Call with affiliateModeOnly = true
    // Assert: Only affiliate-enabled products returned
}
```

#### **Integration Tests** 🔴 TODO
```csharp
[Fact]
public async Task StorefrontProducts_WithAffiliateReferral_OnlyShowsAffiliateProducts()
{
    // Arrange: Create affiliate account with referral code
    // Act: GET /api/storefront/products?ref=CODE
    // Assert: Only IsAffiliateEnabled=true products in response
}

[Fact]
public async Task StorefrontProducts_WithoutAffiliateReferral_ShowsAllProducts()
{
    // Arrange: Create products
    // Act: GET /api/storefront/products (no ?ref=)
    // Assert: All products in response
}

[Fact]
public async Task ProductDetail_WithAffiliateMode_NonAffiliateProduct_Returns404()
{
    // Arrange: Create non-affiliate product
    // Act: GET /api/storefront/products/{slug}?ref=CODE
    // Assert: 404 Not Found
}
```

### Frontend Tests 🔴 TODO
- Affiliate link lands on affiliate-products page
- Home redirects correctly when in Affiliate Mode
- Non-affiliate product cannot be opened
- Visual indicator shows when in Affiliate Mode

---

## 🏆 ACCEPTANCE CRITERIA

- [ ] Affiliate traffic cannot see or buy non-affiliate products
- [ ] Non-affiliate traffic unchanged
- [ ] No duplicate storefront exists
- [ ] No commission leakage
- [ ] All product queries respect Affiliate Mode filter
- [ ] Product detail returns 404 for non-affiliate products in Affiliate Mode
- [ ] Search respects Affiliate Mode filter
- [ ] Homepage sections respect Affiliate Mode filter
- [ ] Checkout flow unchanged
- [ ] Tests pass

---

## 📝 IMPLEMENTATION CHECKLIST

### Phase 1: Core Infrastructure ✅ COMPLETE
- [x] Create AffiliateModeResolver helper
- [x] Update AffiliateReferralMiddleware to set Affiliate Mode
- [x] Update IProductReadRepository interface signatures
- [x] Update ProductReadRepository implementations
- [x] Verify compilation

### Phase 2: Service Layer ✅ COMPLETE
- [x] Update StorefrontProductService
- [x] Update ProductSearchService
- [x] Update StorefrontHomeService
- [x] Update ProductDiscoveryService (N/A - not found in codebase)
- [x] Update Elasticsearch ProductSearchIndexService

### Phase 3: Controller Layer 🔴 PENDING
- [ ] Update StorefrontProductsController
- [ ] Update StorefrontHomeController
- [ ] Update StorefrontCategoriesController
- [ ] Add Affiliate Mode detection to all endpoints

### Phase 4: Testing 🔴 PENDING
- [ ] Write unit tests for AffiliateModeResolver
- [ ] Write unit tests for repository filtering
- [ ] Write integration tests for controllers
- [ ] Write end-to-end tests for affiliate flow

### Phase 5: Observability 🔴 PENDING
- [ ] Add metrics for Affiliate Mode requests
- [ ] Add metrics for blocked product requests
- [ ] Add logging for Affiliate Mode activation
- [ ] Add logging for blocked products

### Phase 6: Frontend (Optional) 🔴 PENDING
- [ ] Add visual indicator in storefront header
- [ ] Implement home page redirect logic
- [ ] Hide non-affiliate categories
- [ ] Update navigation

---

## 🚀 DEPLOYMENT PLAN

1. **Deploy Phase 1** (Core Infrastructure) - Zero impact, backward compatible
2. **Deploy Phase 2** (Service Layer) - Zero impact, default parameters
3. **Deploy Phase 3** (Controller Layer) - Activates Affiliate Mode filtering
4. **Monitor** - Check metrics, logs, error rates
5. **Deploy Phase 6** (Frontend) - Visual enhancements

---

## 📈 PERFORMANCE IMPACT

**Cost:** Single boolean filter in existing indexes.
- No extra tables
- No joins
- No new services
- Negligible overhead

**Indexes Required:** None (IsAffiliateEnabled already indexed)

---

## 🎯 BUSINESS OUTCOME

- Affiliates protected from commission leakage
- Higher affiliate retention
- Predictable ROI for affiliates
- Platform becomes attractive to professional affiliates
- Elevates platform from "Has affiliate feature" to "Built for affiliate economics"

---

**Next Steps:** Continue with Phase 2 (Service Layer Updates)
