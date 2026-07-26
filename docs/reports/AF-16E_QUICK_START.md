# AF-16E Quick Start Guide

## 🎯 What Was Fixed

Affiliate promotion links were pointing to the wrong storefront domain because products in Elasticsearch/MongoDB didn't carry their tenant's `StorefrontBaseUrl`. The discovery API was resolving ONE URL for ALL products based on the current user's tenant.

## ✅ Current Status

**TRANSITION MODE ACTIVE** - The API is now filtering out products without `StorefrontBaseUrl` instead of crashing. You'll see warnings like:

```
[AF-16E] TRANSITION MODE: Filtering products without StorefrontBaseUrl | Count: 6
```

This is expected! You need to run the reindex script to backfill the data.

## 🚀 How to Fix (Choose ONE Option)

### Option 1: SQL Trigger (Simplest - Recommended)

Run this in your PostgreSQL database:

```sql
UPDATE "Products" p
SET "UpdatedAt" = NOW()
FROM "AffiliateProductPolicies" app
WHERE app."ProductId" = p."Id" 
  AND app."IsEnabled" = true 
  AND app."IsDeleted" = false
  AND p."IsDeleted" = false
  AND p."IsActive" = true;
```

This triggers the projection consumer to reprocess all affiliate products with the new `StorefrontBaseUrl` field.

### Option 2: PowerShell Script

```bash
.\af16e-reindex-products.ps1
```

Requires admin JWT token. Provides progress reporting.

### Option 3: Admin API Endpoint

```bash
POST http://localhost:5000/api/admin/af16e-reindex/trigger
Authorization: Bearer {your-admin-token}
```

## 🧪 Verify the Fix

1. **Check MongoDB** (after reindex):
   ```javascript
   // In MongoDB shell
   db.public_affiliate_products.find({ storefrontBaseUrl: { $exists: true, $ne: "" } }).count()
   // Should match total affiliate products
   ```

2. **Test Discovery API**:
   ```bash
   GET /api/affiliate/discovery
   Authorization: Bearer {affiliate-token}
   ```
   
   Should return products with `storefrontBaseUrl` field:
   ```json
   {
     "items": [
       {
         "productId": "...",
         "slug": "product-a",
         "storefrontBaseUrl": "http://anatico.localhost:5174"
       }
     ]
   }
   ```

3. **Check Logs** - Warnings should stop appearing after reindex

## 📊 What Changed

### Before
```json
{
  "storefrontBaseUrl": "http://anatico.localhost:5174",  // ← Same for ALL products
  "items": [
    { "productId": "...", "slug": "product-from-mena" },
    { "productId": "...", "slug": "product-from-anatico" }
  ]
}
```

### After
```json
{
  "items": [
    {
      "productId": "...",
      "slug": "product-from-mena",
      "storefrontBaseUrl": "http://mena.localhost:5174"  // ← Correct domain
    },
    {
      "productId": "...",
      "slug": "product-from-anatico",
      "storefrontBaseUrl": "http://anatico.localhost:5174"  // ← Correct domain
    }
  ]
}
```

## 🔒 Enable Strict Mode (After Reindex)

Once all products are reindexed, enable fail-fast mode:

1. Open `PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs`
2. Find line ~249 with the TODO comment
3. Uncomment the `throw new InvalidOperationException(...)` line
4. Remove the filter logic (`items = items.Where(...)`)
5. Redeploy

This will prevent any future data integrity violations.

## 📝 Files Modified

- `PublicAffiliateProductSearchDocument.cs` - Added `StorefrontBaseUrl` + `TenantId`
- `PublicAffiliateProductDocument.cs` - Added `StorefrontBaseUrl`
- `AffiliateDiscoveryItem.cs` - Added `StorefrontBaseUrl` + `TenantId`
- `AffiliateProductPolicyProjectionConsumer.cs` - Populates `StorefrontBaseUrl` during indexing
- `AffiliateProductDiscoveryService.cs` - Fail-fast validation (transition mode)
- `AffiliateDiscoveryController.cs` - Uses per-product URLs

## 🆘 Troubleshooting

**Q: Still seeing warnings after reindex?**
A: Check if the projection consumer is running. Verify RabbitMQ is processing events.

**Q: Products missing from discovery?**
A: They're being filtered out. Run the reindex script to fix them.

**Q: How long does reindex take?**
A: ~1-2 seconds per 100 products. For 1000 products, expect ~10-20 seconds.

**Q: Can I run reindex multiple times?**
A: Yes, it's idempotent and safe to run multiple times.

## 📚 Full Documentation

See `AF-16E_MULTI_TENANT_DISCOVER_FIX_COMPLETE.md` for complete technical details.
