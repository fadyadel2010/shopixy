# AF-22 Consumer Registration Fix

## Problem Found ❌

The `AffiliateProductIntelligenceProjectionConsumer` was **NOT registered** in MassTransit!

This is why creating new products didn't populate the MongoDB collection.

## Solution Applied ✅

Added consumer registration in `PointOfSale.API/Program.cs`:

```csharp
// AF-22: Product Intelligence projection consumer — builds affiliate_product_intelligence MongoDB collection.
// Listens to: ProductCreated, ProductUpdated, ProductStockChanged, AffiliateProductPolicyChanged, StoreProfileUpdated.
// CRITICAL: Without this, affiliate discover page shows "No Products Found".
// Event-driven, read-only, no SQL joins, tenant-safe.
x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateProductIntelligenceProjectionConsumer>();
```

## What This Fixes

Now when you:
1. ✅ Create a new product → `ProductCreatedEvent` published
2. ✅ Update a product → `ProductUpdatedEvent` published
3. ✅ Change stock → `ProductStockChangedEvent` published
4. ✅ Enable affiliate → `AffiliateProductPolicyChangedEvent` published
5. ✅ Update store profile → `StoreProfileUpdatedEvent` published

The consumer will automatically:
- ✅ Process the event
- ✅ Build/update MongoDB document
- ✅ Make product visible in affiliate discover

## Next Steps

### 1. Restart Backend Server

**CRITICAL:** You must restart your backend for the consumer registration to take effect!

```bash
# Stop your backend (Ctrl+C)
# Then restart it
dotnet run --project PointOfSale.API
```

### 2. Verify Consumer is Registered

Check backend logs on startup for:

```
[MassTransit] Consumer registered: AffiliateProductIntelligenceProjectionConsumer
```

Or similar MassTransit registration message.

### 3. Test with New Product

**Option A: Create a new product**
1. Go to POS admin
2. Create a new product
3. Fill in all fields (name, price, images, etc.)
4. Save
5. Wait 1-2 seconds for event processing
6. Refresh: `http://localhost:5173/affiliate/portal?tab=discover`
7. Product should appear!

**Option B: Update existing product**
1. Go to POS admin
2. Edit any existing product
3. Change name or price
4. Save
5. Wait 1-2 seconds
6. Refresh discover page
7. Product should appear!

### 4. Verify MongoDB

Check if document was created:

```javascript
use PosEcommerceDb
db.affiliate_product_intelligence.find().pretty()
```

You should see documents with:
- ProductId
- Name
- Price
- CommissionRate
- EstimatedCommission
- TotalStock
- StorefrontUrl
- etc.

## Troubleshooting

### Issue: Still no products after restart

**Check 1: Backend logs**
Look for errors like:
- MongoDB connection failed
- Event processing error
- Consumer exception

**Check 2: RabbitMQ**
- Is RabbitMQ running?
- Check RabbitMQ management UI: `http://localhost:15672`
- Look for queues with names like `affiliate-product-intelligence-*`

**Check 3: Event publishing**
Add logging to verify events are being published:
- Check if `ProductCreatedEvent` is published when creating product
- Check if event reaches RabbitMQ

**Check 4: Consumer execution**
Add breakpoint or logging in `AffiliateProductIntelligenceProjectionConsumer.Consume()` method

### Issue: Consumer registered but not processing

**Possible causes:**
1. Event not being published (check publisher)
2. Wrong exchange/queue binding (check RabbitMQ)
3. Consumer throwing exception (check logs)
4. MongoDB connection issue (check connection string)

### Issue: MongoDB document created but not showing in UI

**Check API:**
```bash
curl http://localhost:5000/api/affiliate/discover/products?pageSize=5
```

If API returns data but UI doesn't show it:
- Clear browser cache
- Hard refresh (Ctrl+Shift+R)
- Check browser console for errors

## Testing Checklist

After restarting backend:

- [ ] Backend starts without errors
- [ ] Consumer registration logged
- [ ] Create new product
- [ ] Check MongoDB for document
- [ ] Check API returns product
- [ ] Refresh discover page
- [ ] Product appears in UI
- [ ] Click product → Drawer opens
- [ ] All product details visible

## Why This Happened

The AF-22 implementation was complete:
- ✅ Consumer class created
- ✅ MongoDB document defined
- ✅ API endpoints created
- ✅ Frontend components created

But the consumer was never registered in MassTransit, so it never received events!

This is a common mistake when adding new consumers - forgetting to register them in `Program.cs`.

## Prevention

When adding new consumers in the future:

1. Create consumer class
2. **Register in Program.cs** ← Don't forget this!
3. Test event flow
4. Verify consumer processes events

## Status

- ✅ Consumer registration: FIXED
- ⏳ Backend restart: REQUIRED
- ⏳ Testing: PENDING

**Action Required:** Restart your backend server now!
