# AF-21: Quick Reference Guide

## 🚀 What Changed?

Affiliate Portal now shows real-time order status from POS Admin:
- **Order Status**: Pending → Confirmed → Completed / Cancelled
- **Fulfillment Status**: Created → Packed → OutForDelivery → Delivered → Completed
- **Payment Method**: COD, Card, Wallet, etc.
- **Order Total**: Amount customer paid

## 📡 Event Flow

```
POS Admin Action → Event Published → Consumer Updates MongoDB → Affiliate Portal Reads
```

### Example: Order Delivered
```
Admin clicks "Mark Delivered"
  ↓
FulfillmentDeliveredEvent published
  ↓
OrderLifecycleAffiliateProjectionConsumer receives event
  ↓
Updates MongoDB: fulfillmentStatus = "Delivered"
  ↓
Affiliate refreshes portal → sees "Delivered"
```

## 🔍 API Response Changes

### Before AF-21
```json
{
  "orderNumber": "ORD-123",
  "status": "Approved",
  "totalCommission": 44.00,
  "currency": "EGP"
}
```

### After AF-21
```json
{
  "orderNumber": "ORD-123",
  "status": "Approved",
  "orderStatus": "Completed",           // ← NEW
  "fulfillmentStatus": "Delivered",     // ← NEW
  "paymentMethod": "COD",               // ← NEW
  "paidAmount": 200.00,                 // ← NEW
  "totalCommission": 44.00,
  "currency": "EGP"
}
```

## 🎯 Key Files Modified

| File | Change |
|------|--------|
| `AffiliateDocuments.cs` | Added 4 fields to `AffiliateOrderDocument` |
| `AffiliateProjectionConsumers.cs` | Added `OrderLifecycleAffiliateProjectionConsumer` |
| `Program.cs` | Registered new consumer |
| `AffiliatePortalController.cs` | Extended DTO and mapping |

## 🔧 Backfill Command

```bash
# Run backfill script to populate existing orders
dotnet run --project PointOfSale.CLI -- affiliate:backfill-order-status
```

Or use the SQL script: `af21_backfill_order_status.sql`

## ✅ Verification

### Check MongoDB
```javascript
// Count orders with new fields
db.affiliate_orders.countDocuments({ 
  orderStatus: { $exists: true, $ne: "" } 
})

// Sample document
db.affiliate_orders.findOne({ 
  orderStatus: { $exists: true } 
})
```

### Check API
```bash
curl -H "Authorization: Bearer {token}" \
  https://api.example.com/api/affiliate/orders
```

Look for `orderStatus`, `fulfillmentStatus`, `paymentMethod`, `paidAmount` in response.

## 🚨 Troubleshooting

### Issue: New fields are empty
**Cause:** Backfill not run yet  
**Fix:** Run backfill script

### Issue: Consumer not processing events
**Cause:** Consumer not registered or RabbitMQ queue not created  
**Fix:** Check Program.cs registration, restart app, check RabbitMQ management UI

### Issue: Cross-tenant data visible
**Cause:** TenantId filter missing (should never happen)  
**Fix:** Check consumer code, verify TenantId in events

## 📊 Status Field Values

### OrderStatus (from Order.Status enum)
- `Pending` - Order placed, awaiting confirmation
- `Confirmed` - Order confirmed by admin
- `Processing` - Order being prepared
- `Completed` - Order fulfilled successfully
- `Cancelled` - Order cancelled
- `Refunded` - Order refunded

### FulfillmentStatus (from FulfillmentStatus enum)
- `Created` - Order created, not yet confirmed
- `Confirmed` - Ready for fulfillment
- `Packed` - Items packed, ready to ship
- `OutForDelivery` - With carrier/driver
- `Delivered` - Delivered to customer
- `Completed` - Fulfillment complete
- `Cancelled` - Fulfillment cancelled
- `Returned` - Order returned by customer
- `FailedDelivery` - Delivery attempt failed

### PaymentMethod
- `COD` - Cash on Delivery
- `Card` - Credit/Debit Card
- `Wallet` - Digital Wallet
- `BankTransfer` - Bank Transfer
- `Unknown` - Not specified

## 🎓 Architecture Principles

1. **Event-Driven**: All updates via events, no direct MongoDB writes
2. **Tenant-Safe**: TenantId filter on every update
3. **Idempotent**: ProcessedEventIds prevents duplicate processing
4. **No SQL Joins**: Pure MongoDB reads, no PostgreSQL queries
5. **Separation of Concerns**: Commission logic separate from order lifecycle

## 📞 Support

For issues or questions:
1. Check logs: `[Affiliate:Lifecycle:*]` pattern
2. Verify RabbitMQ queues exist
3. Check MongoDB documents have new fields
4. Review event flow in MassTransit

---

**Quick Start:** Deploy → Run Backfill → Verify API → Done! 🎉
