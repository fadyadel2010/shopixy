# AF-22: Integration Guide

## Quick Start

### 1. Update AffiliatePortal.jsx to use Enhanced Discover Tab

**Option A: Replace existing (recommended)**

```jsx
// In PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx

// Replace this import:
// import DiscoverTab from './portal/DiscoverTab'

// With this:
import DiscoverTab from './portal/DiscoverTabEnhanced'
```

**Option B: Feature flag (safer for gradual rollout)**

```jsx
// In PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx

import DiscoverTab from './portal/DiscoverTab'
import DiscoverTabEnhanced from './portal/DiscoverTabEnhanced'

// In renderTab() function:
case 'discover': 
    // Check feature flag or environment variable
    const useEnhanced = localStorage.getItem('af22_enhanced') === 'true'
    return useEnhanced 
        ? <DiscoverTabEnhanced /> 
        : <DiscoverTab initialSearch={searchParams.get('q') || ''} />
```

### 2. Run Database Migrations

The MongoDB indexes will be created automatically on application startup via `MongoDbContext.EnsureIndexesCreatedAsync()`.

No PostgreSQL migrations needed - this is a pure read model.

### 3. Run Backfill (Optional but Recommended)

Create a .NET console app or background job:

```csharp
// Example: PointOfSale.CLI/Commands/RebuildAffiliateIntelligenceCommand.cs

public class RebuildAffiliateIntelligenceCommand
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger _logger;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[AF-22:Backfill] Starting...");

        var products = await _dbContext.Products
            .Where(p => !p.IsDeleted && p.IsActive)
            .OrderBy(p => p.TenantId)
            .ToListAsync(ct);

        _logger.LogInformation("[AF-22:Backfill] Found {Count} products", products.Count);

        foreach (var product in products)
        {
            await _publishEndpoint.Publish(new ProductUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                TenantId = product.TenantId,
                ProductId = product.Id,
                Name = product.Name,
                Description = product.Description,
                Images = product.Images,
                SalePrice = product.SalePrice,
                OnlinePrice = product.OnlinePrice,
                CategoryId = product.CategoryId,
                Brand = product.Brand,
                SKU = product.SKU,
                Barcode = product.Barcode,
                IsOnlineVisible = product.IsOnlineVisible,
                Slug = product.Slug,
                TrackStock = product.TrackStock,
                AllowBackorder = product.AllowBackorder,
                HasVariants = product.HasVariants,
                ChangedFields = new[] { "Name", "Description", "Images", "OnlinePrice" }
            }, ct);
        }

        _logger.LogInformation("[AF-22:Backfill] Complete");
    }
}
```

Run:
```bash
dotnet run --project PointOfSale.CLI affiliate:intelligence:rebuild
```

### 4. Verify Installation

#### Check MongoDB Collection

```javascript
// In MongoDB shell or Compass
use PosEcommerceDb

// Check collection exists
db.affiliate_product_intelligence.countDocuments()

// Check indexes
db.affiliate_product_intelligence.getIndexes()

// Sample document
db.affiliate_product_intelligence.findOne()
```

Expected indexes:
- `_id_` (default)
- `idx_product_tenant_unique` (ProductId, TenantId) UNIQUE
- `idx_tenant` (TenantId)
- `idx_out_of_stock` (IsOutOfStock)
- `idx_commission_rate` (CommissionRate)
- `idx_product_name_text` (ProductName) TEXT

#### Test API Endpoints

```bash
# Get JWT token first
TOKEN="your_affiliate_jwt_token"

# List products
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/affiliate/discover/products?page=1&pageSize=10"

# Get product detail
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/affiliate/discover/products/{productId}"
```

#### Test Frontend

1. Navigate to: `http://localhost:5174/affiliate/portal?tab=discover`
2. Search for products
3. Toggle "In Stock Only"
4. Click a product card
5. Verify drawer opens with full details
6. Copy affiliate link
7. Share via WhatsApp/Twitter

### 5. Monitor Logs

Watch for AF-22 log entries:

```bash
# In application logs
grep "AF-22" logs/app.log

# Expected entries:
[AF-22:ProductCreated] Upserted product intelligence | ProductId: ... | TenantId: ...
[AF-22:ProductUpdated] Updated product intelligence | ProductId: ... | TenantId: ...
[AF-22:StockChanged] Updated stock | ProductId: ... | TotalStock: ...
[AF-22:PolicyChanged] Updated commission | ProductId: ... | CommissionRate: ...
[AF-22:StoreProfileUpdated] Updated 50 products | TenantId: ...
```

## Configuration

### Storefront Base URL

Update in `appsettings.json`:

```json
{
  "Storefront": {
    "BaseUrl": "http://localhost:5174"
  }
}
```

Or via environment variable:
```bash
export Storefront__BaseUrl="https://storefront.yourdomain.com"
```

### Low Stock Threshold

Currently hardcoded to 10 units in `AffiliateProductIntelligenceProjectionConsumer.cs`:

```csharp
var lowStock = totalStock > 0 && totalStock < 10;
```

To make configurable:

```json
{
  "Affiliate": {
    "LowStockThreshold": 10
  }
}
```

```csharp
// In consumer constructor
private readonly int _lowStockThreshold;

public AffiliateProductIntelligenceProjectionConsumer(
    IConfiguration configuration,
    ...)
{
    _lowStockThreshold = configuration.GetValue<int>("Affiliate:LowStockThreshold", 10);
}

// In usage
var lowStock = totalStock > 0 && totalStock < _lowStockThreshold;
```

### Currency

Currently hardcoded to "EGP". To make dynamic:

```csharp
// Get from tenant settings
var tenant = await _dbContext.Tenants.FindAsync(tenantId);
var currency = tenant?.Currency ?? "EGP";
```

## Troubleshooting

### Products not appearing in discover

**Check:**
1. MongoDB collection populated: `db.affiliate_product_intelligence.countDocuments()`
2. Products have affiliate policies: `SELECT * FROM "AffiliateProductPolicies" WHERE "IsEnabled" = true`
3. Consumer is running and processing events
4. Check logs for errors

### Stock not updating

**Check:**
1. `ProductStockChangedEvent` is being published
2. Consumer is subscribed to the event
3. MongoDB document has correct `TotalStock` value
4. Variants array is populated

### Commission rate not showing

**Check:**
1. `AffiliateProductPolicies` table has entries for products
2. `CommissionRate` is set (not 0)
3. `AffiliateProductPolicyChangedEvent` was published
4. MongoDB document has correct `CommissionRate` value

### Drawer not opening

**Check:**
1. Browser console for errors
2. API endpoint returns 200: `/api/affiliate/discover/products/{productId}`
3. JWT token is valid
4. Product exists in MongoDB collection

## Performance Optimization

### Add Caching (Optional)

For high-traffic scenarios, add Redis caching:

```csharp
// In AffiliatePortalController

private readonly IDistributedCache _cache;

[HttpGet("discover/products")]
public async Task<ActionResult<AffiliateProductDiscoveryResponse>> DiscoverProducts(...)
{
    var cacheKey = $"affiliate:discover:{page}:{pageSize}:{search}:{inStockOnly}";
    var cached = await _cache.GetStringAsync(cacheKey, ct);
    
    if (cached != null)
    {
        return Ok(JsonSerializer.Deserialize<AffiliateProductDiscoveryResponse>(cached));
    }

    // ... existing logic ...

    await _cache.SetStringAsync(
        cacheKey, 
        JsonSerializer.Serialize(response),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
        ct);

    return Ok(response);
}
```

### Optimize MongoDB Queries

If queries are slow, add compound indexes:

```javascript
// In MongoDB shell
db.affiliate_product_intelligence.createIndex(
    { TenantId: 1, IsOutOfStock: 1, CommissionRate: 1 },
    { name: "idx_tenant_stock_commission" }
)
```

## Rollback Plan

If issues arise, rollback is safe:

1. **Frontend:** Revert to old `DiscoverTab.jsx`
2. **API:** Endpoints are additive, no changes to existing endpoints
3. **Consumer:** Stop consumer, no impact on existing affiliate logic
4. **MongoDB:** Collection is read-only, can be dropped without impact

```javascript
// Drop collection (if needed)
db.affiliate_product_intelligence.drop()
```

## Support

For issues or questions:
1. Check logs: `grep "AF-22" logs/app.log`
2. Check MongoDB: `db.affiliate_product_intelligence.find().limit(10)`
3. Check API: `curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/affiliate/discover/products`
4. Review implementation summary: `AF-22_IMPLEMENTATION_SUMMARY.md`
