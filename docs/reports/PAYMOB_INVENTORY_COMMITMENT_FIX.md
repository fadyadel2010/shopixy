# Paymob Inventory Commitment Recovery Fix

## Problem Statement
The Paymob webhook processing had a critical bug where inventory reservations could become "zombies" - orders marked as paid but inventory never committed, blocking future sales.

### The Bug
```csharp
// Line 654: Payment status persisted FIRST
await _dbContext.SaveChangesAsync(cancellationToken);

// Line 655: Inventory committed SECOND
await CommitInventoryAsync(...);
```

**Failure Scenario:**
1. Webhook arrives, payment status is saved to DB
2. `CommitInventoryAsync` fails (network, DB, event bus)
3. Webhook retries
4. Idempotency check sees `order.PaymentStatus == PaymentStatus.Paid`
5. Early return - inventory commitment is **never attempted again**
6. Result: Order is paid, but inventory is still reserved (not committed)

## Solution: Two-Layer Defense

### Layer 1: Transaction Boundary (Primary Fix)
Wrapped the entire payment processing in a database transaction:

```csharp
await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

try
{
    // Process payment (includes inventory operations)
    result = await HandlePaymentSuccessAsync(...);
    
    // Commit everything atomically
    await dbTransaction.CommitAsync(cancellationToken);
    return result;
}
catch (Exception)
{
    // Auto-rollback on failure
    throw;
}
```

**How it works:**
- All DB operations (payment status, inventory commitment) happen in one transaction
- If anything fails, everything rolls back
- Webhook retry sees clean state and can retry safely

### Layer 2: InventoryCommitted Flag (Defense in Depth)
Added tracking to ensure inventory commitment can be recovered:

**Entity Change:**
```csharp
public class PaymentIntent
{
    // ... existing properties ...
    
    /// <summary>
    /// Whether inventory has been committed for this payment.
    /// Used to ensure inventory commitment is recoverable on webhook retry.
    /// </summary>
    public bool InventoryCommitted { get; set; }
}
```

**Idempotency Check Enhancement:**
```csharp
if (order.PaymentStatus == PaymentStatus.Paid)
{
    // CRITICAL: If payment is marked paid but inventory not committed,
    // we must complete the inventory commitment on retry
    if (!intent.InventoryCommitted)
    {
        _logger.LogWarning(
            "Order paid but inventory not committed - completing on retry");

        await CommitInventoryAsync(...);
        intent.InventoryCommitted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    
    return; // Already processed
}
```

**Set Flag After Success:**
```csharp
await CommitInventoryAsync(...);
intent.InventoryCommitted = true;  // Mark as done
await _dbContext.SaveChangesAsync(cancellationToken);
```

## Why Both Layers?

**Transaction Boundary** handles the common case:
- Prevents partial commits
- Ensures atomicity
- Clean rollback on failure

**InventoryCommitted Flag** handles edge cases:
- Transaction commits but process crashes before flag is set
- Allows safe retry of inventory commitment
- Provides audit trail of inventory state

## Files Changed

1. **PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs**
   - Added transaction boundary around payment processing
   - Enhanced idempotency check to handle partial failures
   - Set `InventoryCommitted` flag after successful commitment

2. **PointOfSale.Core/Entities/PaymentIntent.cs**
   - Added `InventoryCommitted` boolean property

3. **add_inventory_committed_to_payment_intent.sql**
   - Migration to add column
   - Backfill existing paid intents
   - Index for retry query pattern

## Migration Notes

```sql
-- Add column with default false
ALTER TABLE "PaymentIntents"
ADD COLUMN "InventoryCommitted" boolean NOT NULL DEFAULT false;

-- Backfill: Assume historical payments have inventory committed
UPDATE "PaymentIntents"
SET "InventoryCommitted" = true
WHERE "Status" IN (3, 4); -- Paid or Captured

-- Index for retry queries
CREATE INDEX "IX_PaymentIntents_Status_InventoryCommitted" 
ON "PaymentIntents" ("Status", "InventoryCommitted")
WHERE "Status" = 3 AND "InventoryCommitted" = false;
```

## Testing Checklist

- [ ] Normal payment success → inventory committed, flag set
- [ ] Payment failure → inventory released
- [ ] Webhook retry after full rollback → clean retry works
- [ ] Webhook retry after partial failure → inventory commitment completes
- [ ] Duplicate webhook → idempotency still works
- [ ] Concurrent webhooks → no race conditions
- [ ] Event emission failure → transaction rolls back
- [ ] Verify no zombie reservations created

## Impact

✅ **Prevents zombie reservations** - Orders can't be paid without inventory commitment  
✅ **Ensures atomicity** - Payment and inventory are atomic  
✅ **Enables safe retries** - Partial failures can be recovered  
✅ **Maintains idempotency** - Duplicate webhooks handled correctly  
✅ **Audit trail** - `InventoryCommitted` flag shows inventory state  

## Related Issues

This fix complements the earlier transaction boundary fix in `PaymentService.cs` (documented in `WEBHOOK_TRANSACTION_BOUNDARY_FIX.md`). The Paymob service had the same issue plus an additional ordering problem where `SaveChangesAsync` was called before `CommitInventoryAsync`.
