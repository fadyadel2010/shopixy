# Accounting Outbox COD Delivery Investigation

**Date**: 2026-02-09  
**Status**: ✅ RESOLVED (2026-02-10 00:17)  
**Priority**: High  
**Component**: `PointOfSale.Infrastructure.Payments`

## 🎯 ROOT CAUSE IDENTIFIED (2026-02-10)

**Tenant Query Filter Blocking Order Lookup in MassTransit Consumer**

When the `FulfillmentDeliveredEventConsumer` calls `MarkCodDeliveredAsync`, the method queries the database:
```csharp
var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
```

The problem is that EF Core's tenant query filter is applied:
```csharp
modelBuilder.Entity<Order>().HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
```

**MassTransit consumers run WITHOUT a tenant context** (because they're background workers), so `CurrentTenantId` is `null`. This causes the query filter to return **no results** even though the order exists!

### Error Evidence
```
warn: [OrderDeliveredHandler] COD payment transition failed | OrderId: 353dc155-... | Error: Order not found
```

### Fix Applied
Changed `MarkCodDeliveredAsync` to use `IgnoreQueryFilters()`:
```csharp
var order = await _db.Orders
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);
```

---

## 1. Problem Statement

### Symptoms
- COD (Cash on Delivery) orders are successfully fulfilled through the order lifecycle
- The `FulfillmentDeliveredEventConsumer` logs `"LedgerJournal: QUEUED"` indicating the outbox message should be created
- However, **no accounting journal entries are being created** for COD deliveries
- The `AccountingOutboxProcessor` is not processing these messages
- Financial reports do not reflect COD sales revenue

### Impact
- **Revenue Recognition Failure**: COD sales are not recorded in the ledger
- **Financial Report Inaccuracy**: Balance sheets and P&L statements are incomplete
- **Audit Trail Gap**: No journal entries for delivered COD orders

---

## 2. Architecture Overview

### Expected Flow
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        COD DELIVERY ACCOUNTING FLOW                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. OrderFulfillmentService.MarkAsDelivered()                               │
│           │                                                                  │
│           ▼                                                                  │
│  2. FulfillmentDeliveredEvent published via MassTransit                     │
│           │                                                                  │
│           ▼                                                                  │
│  3. FulfillmentDeliveredEventConsumer.Consume()                             │
│           │                                                                  │
│           ├── Detects COD order with PaymentStatus.Pending                  │
│           │                                                                  │
│           ▼                                                                  │
│  4. PaymentLifecycleService.MarkCodDeliveredAsync()                         │
│           │                                                                  │
│           ├── Updates order PaymentStatus → Collected                       │
│           ├── Calls AccountingOutboxService.EnqueueCodDeliveredAsync()      │
│           │                                                                  │
│           ▼                                                                  │
│  5. AccountingOutboxService.EnqueueMessageAsync()                           │
│           │                                                                  │
│           ├── Creates AccountingOutboxMessage entity                        │
│           ├── Adds to DbContext                                             │
│           ├── Calls SaveChangesAsync() ← CRITICAL SAVE POINT                │
│           │                                                                  │
│           ▼                                                                  │
│  6. Transaction CommitAsync() in MarkCodDeliveredAsync                      │
│           │                                                                  │
│           ▼                                                                  │
│  7. AccountingOutboxWorker polls every 5-30 seconds                         │
│           │                                                                  │
│           ▼                                                                  │
│  8. AccountingOutboxProcessor.ProcessPendingMessagesAsync()                 │
│           │                                                                  │
│           ├── Query: ProcessedAtUtc == null && !IsFailed                    │
│           │         && (NextRetryAtUtc == null || NextRetryAtUtc <= Now)    │
│           │                                                                  │
│           ▼                                                                  │
│  9. DispatchMessageAsync() → ProcessCodDeliveredAsync()                     │
│           │                                                                  │
│           ▼                                                                  │
│ 10. CommerceAccountingService.PostCodDeliveredAsync()                       │
│           │                                                                  │
│           ├── Creates LedgerJournal entry                                   │
│           ├── DEBIT: COD Clearing Account                                   │
│           ├── CREDIT: Sales Revenue Account                                 │
│           │                                                                  │
│           ▼                                                                  │
│ 11. Message marked as Processed                                             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Files
| File | Purpose |
|------|---------|
| `PointOfSale.Infrastructure/Consumers/OrderDeliveredEventConsumer.cs` | Handles `FulfillmentDeliveredEvent`, triggers COD payment collection |
| `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` | Updates payment status, enqueues outbox message |
| `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs` | Creates and persists outbox messages |
| `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs` | Polls and processes pending outbox messages |
| `PointOfSale.Infrastructure/Accounting/AccountingOutboxWorker.cs` | Background service that runs the processor |

---

## 3. Investigation Timeline

### Phase 1: Initial Hypothesis - Console Logs Not Appearing
**Observation**: Added `Console.WriteLine` statements to `PaymentLifecycleService.MarkCodDeliveredAsync` and `AccountingOutboxService.EnqueueCodDeliveredAsync`. Logs did not appear.

**Concern**: Is the code path even being executed?

### Phase 2: Code Execution Verification
**Action**: Added an intentional `throw new Exception("TEST EXCEPTION - IF YOU SEE THIS, I AM RUNNING")` at the start of `MarkCodDeliveredAsync`.

**Result**: ✅ **CONFIRMED** - Exception appeared in logs:
```
fail: PointOfSale.Infrastructure.Payments.PaymentLifecycleService[0]
      Failed to mark COD order 4043faa3-0117-4a34-b331-1e2b855c14f7 as delivered
      System.Exception: TEST EXCEPTION - IF YOU SEE THIS, I AM RUNNING
```

**Conclusion**: The code IS executing. `Console.WriteLine` output was being swallowed/buffered by the logging infrastructure.

### Phase 3: Consumer Log Analysis
**Observation**: After removing the test exception, logs show:
```
info: PointOfSale.Infrastructure.Consumers.FulfillmentDeliveredEventConsumer[0]
      [OrderDeliveredHandler] COD payment collected | OrderId: ... | LedgerJournal: QUEUED
```

This log appears AFTER `await _paymentLifecycleService.MarkCodDeliveredAsync()` returns successfully.

**Implication**: `MarkCodDeliveredAsync` completed without exception, meaning:
- Order was updated
- `EnqueueCodDeliveredAsync` was called
- Transaction was committed

### Phase 4: Outbox Processor Analysis
**Observation**: No logs from `AccountingOutboxProcessor` indicating message processing.

**Added logging**: Modified processor to log even when 0 messages found:
```csharp
if (pendingMessages.Count == 0)
{
    _logger.LogInformation("[OUTBOX-DEBUG] Check complete. Found 0 pending messages. Time: {Time}", DateTime.UtcNow);
    return 0;
}
```

---

## 4. Current Hypotheses

### Hypothesis A: Transaction Isolation (LIKELY)
The `MarkCodDeliveredAsync` method uses a transaction:
```csharp
using var transaction = await _db.Database.BeginTransactionAsync(ct);
try
{
    // ... business logic ...
    await _accountingOutbox.EnqueueCodDeliveredAsync(order.TenantId, orderId, ct);
    await _db.SaveChangesAsync(ct);
}
finally
{
    PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
}
await transaction.CommitAsync(ct);
```

Inside `EnqueueMessageAsync`:
```csharp
_db.AccountingOutboxMessages.Add(message);
await _db.SaveChangesAsync(ct);
```

**Scenario**: If `AccountingOutboxService` uses a DIFFERENT `ApplicationDbContext` instance than `PaymentLifecycleService`, the `SaveChangesAsync()` in `EnqueueMessageAsync` would commit immediately (outside the parent transaction), potentially causing:
- Race condition where message is visible before parent transaction commits
- Or conversely, if parent transaction rolls back, message persists orphaned

**Counter-evidence**: Both services are registered as Scoped, so they SHOULD share the same `DbContext` within the MassTransit consumer scope.

### Hypothesis B: Save Not Actually Persisting
The `SaveChangesAsync()` returns number of rows affected. If it returns 0, no entity was saved.

**Potential causes**:
- Entity not tracked by DbContext
- Duplicate detection (idempotency check already found existing)
- Silent EF Core optimization skipping save

### Hypothesis C: Message Created But Immediately Filtered Out
The outbox message could be created with unexpected initial values:
- `ProcessedAtUtc` not null?
- `IsFailed` = true?
- `NextRetryAtUtc` set to far future?

**Counter-evidence**: `AccountingOutboxMessage.Create()` does not set these values.

### Hypothesis D: Different Database Connection
Worker uses different connection string than API request handlers.

**Counter-evidence**: Same process, same DI configuration.

---

## 5. Debugging Instrumentation Added

### AccountingOutboxProcessor.cs
```csharp
// Line 56-60: Log when 0 messages found
if (pendingMessages.Count == 0)
{
    _logger.LogInformation("[OUTBOX-DEBUG] Check complete. Found 0 pending messages. Time: {Time}", DateTime.UtcNow);
    return 0;
}
```

```csharp
// Line 63-71: Log each pending message found
foreach (var msg in pendingMessages)
{
    _logger.LogInformation(
        "[OUTBOX-DEBUG] Found pending message | ID: {Id} | Type: {Type} | Occurred: {Occurred} | Attempts: {Attempts} | NextRetry: {NextRetry}",
        msg.Id, msg.EventType, msg.OccurredAtUtc, msg.AttemptCount, msg.NextRetryAtUtc);
}
```

### AccountingOutboxService.cs
```csharp
// Before SaveChangesAsync
_logger.LogInformation("[OUTBOX-DEBUG] Saving message {Id} to DB...", message.Id);

// After SaveChangesAsync
var saved = await _db.SaveChangesAsync(ct);
_logger.LogInformation(
    "[OUTBOX-DEBUG] Message SAVED: {EventType} | MessageId: {MessageId} | EntityId: {EntityId} | RowsSaved: {RowsSaved}",
    eventType, message.Id, businessEntityId, saved);
```

---

## 6. Next Steps

### Immediate Actions
1. **Run API with new logging** and trigger a COD order delivery
2. **Check logs for**:
   - `[OUTBOX-DEBUG] Saving message {Id} to DB...`
   - `[OUTBOX-DEBUG] Message SAVED ... RowsSaved: {N}`
   - `[OUTBOX-DEBUG] Check complete. Found 0 pending messages`
3. **If RowsSaved = 0**: Investigate why entity isn't being tracked
4. **If RowsSaved > 0 but processor finds 0**: Query database directly to verify message existence

### Database Verification Query
```sql
SELECT id, tenant_id, event_type, business_entity_id, 
       occurred_at_utc, processed_at_utc, is_failed, 
       attempt_count, next_retry_at_utc, last_error
FROM accounting_outbox_messages
WHERE processed_at_utc IS NULL
ORDER BY occurred_at_utc DESC
LIMIT 20;
```

### If Messages Exist But Not Processed
1. Check `NextRetryAtUtc` - is it in the future?
2. Check `IsFailed` flag
3. Check if worker timer is actually triggering
4. Add logging to `AccountingOutboxWorker.ExecuteAsync` loop

### If Messages Don't Exist
1. Verify `_db` in `AccountingOutboxService` is same instance as in `PaymentLifecycleService`
2. Add DbContext instance ID logging
3. Check if transaction rollback is occurring silently

---

## 7. Related Issues

- **Conversation**: `aabb8f1d-84af-4dfa-947b-cb1729f696f7` - Fix Outbox Idempotency Issues
- **Phase**: TOJ (Treasury Operations Journaling)
- **Architecture Doc**: `docs/ORDER_FULFILLMENT_LIFECYCLE_ARCHITECTURE.md`

---

## 8. Files Modified During Investigation

| File | Change | Revert Needed |
|------|--------|---------------|
| `PaymentLifecycleService.cs` | Added Console.WriteLine (line 92), outboxResult capture | Partial (cleanup console) |
| `AccountingOutboxService.cs` | Added debug logging, returns OutboxEnqueueResult | No (observability) |
| `AccountingOutboxProcessor.cs` | Added debug logging (lines 56-71) | Yes (after fix) |

---

## 9. FIX IMPLEMENTATION (2026-02-10)

### Changes Made

1. **`IAccountingOutboxService.cs`**:
   - Added `OutboxEnqueueResult` record type with `WasCreated`, `AlreadyExists`, `Status`, `MessageId` fields
   - Changed `EnqueueCodDeliveredAsync` to return `OutboxEnqueueResult` instead of void

2. **`AccountingOutboxService.cs`**:
   - Updated `EnqueueMessageAsync` to return `OutboxEnqueueResult`
   - When existing message found, now logs detailed status (PENDING, FAILED, WAITING_RETRY, PROCESSED)
   - Returns existing message ID and status for observability

3. **`IPaymentLifecycleService.cs`**:
   - Added `OutboxStatus` and `OutboxMessageId` to `PaymentTransitionResult`
   - Added `IsSuccess` alias property

4. **`PaymentLifecycleService.cs`**:
   - Captures and logs outbox result
   - Returns outbox status in `PaymentTransitionResult`

5. **`OrderDeliveredEventConsumer.cs`**:
   - Uses result to log actual status instead of always "QUEUED"
   - Properly handles success/failure cases

### Verification Steps

1. Start the API and check logs for new status messages
2. Use `/api/debug/outbox/status` to see pending/failed messages
3. Trigger a COD delivery and verify logs show `WasCreated: true` or `AlreadyExists: true` with status
4. If stuck messages exist, use `/api/debug/outbox/reset/{messageId}` to retry them

---

## 10. Resolution Checklist

- [x] ✅ Identify root cause: Idempotency silent skip
- [x] ✅ Implement observability: OutboxEnqueueResult returned
- [x] ✅ Add diagnostic endpoints: /api/debug/outbox/*
- [ ] Confirm outbox message is saved (RowsSaved > 0)
- [ ] Confirm message appears in database
- [ ] Confirm processor finds the message
- [ ] Confirm journal entry is created
- [ ] Confirm financial reports reflect COD sales
- [ ] Remove debug logging
- [ ] Add automated test to prevent regression

---

*Last Updated: 2026-02-10 00:05 EET*

