# 🛡️ Phase 3.5: Production Hardening - Financially Bulletproof

## Implementation Complete ✅

**Date:** 2026-02-06  
**Status:** Core hardening complete, ready for integration

---

## 📋 Overview

Phase 3.5 hardens the commerce-to-ledger integration with production-grade guarantees:

- **Exactly-once posting** via transactional outbox
- **Atomic transactions** (business + accounting together)
- **Crash resilience** via persistent outbox
- **Full traceability** via audit trail
- **Observability** via metrics and alerts

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    BUSINESS LAYER                               │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              IAccountingTransactionBoundary                     │
│    ┌─────────────────────────────────────────────────────┐     │
│    │              SINGLE TRANSACTION                      │     │
│    │   ┌───────────────┐    ┌────────────────────┐       │     │
│    │   │ Business Save │ +  │ Outbox Message     │       │     │
│    │   │ (SalesInvoice)│    │ (EnqueuePosSale)   │       │     │
│    │   └───────────────┘    └────────────────────┘       │     │
│    └─────────────────────────────────────────────────────┘     │
│                             │                                   │
│                             ▼                                   │
│                         COMMIT                                  │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼ (Background Worker)
┌─────────────────────────────────────────────────────────────────┐
│                 AccountingOutboxProcessor                       │
│   ┌─────────────────────────────────────────────────────┐      │
│   │ 1. Read pending message                              │      │
│   │ 2. Idempotency double-check                          │      │
│   │ 3. Call CommerceAccountingService                    │      │
│   │ 4. Create audit trail                                │      │
│   │ 5. Mark processed                                    │      │
│   └─────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              IFinancialPostingService (Phase 1)                 │
│                   IMMUTABLE LEDGER                              │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📁 Files Created

### Entities
| File | Description |
|------|-------------|
| `Core/Entities/Accounting/AccountingOutboxMessage.cs` | Outbox message entity with retry logic |
| `Core/Entities/Accounting/CommerceAccountingAudit.cs` | Audit trail entity |

### Interfaces
| File | Description |
|------|-------------|
| `Core/Interfaces/Accounting/IAccountingOutboxService.cs` | Outbox enqueue operations |
| `Core/Interfaces/Accounting/IAccountingMetrics.cs` | Metrics interface |

### Services
| File | Description |
|------|-------------|
| `Infrastructure/Accounting/AccountingOutboxService.cs` | Outbox enqueueing |
| `Infrastructure/Accounting/AccountingOutboxProcessor.cs` | Background processing |
| `Infrastructure/Accounting/AccountingOutboxWorker.cs` | Background worker |
| `Infrastructure/Accounting/AccountingMetrics.cs` | Metrics implementation |
| `Infrastructure/Accounting/AccountingTransactionBoundary.cs` | Transaction wrapper |

### Configurations
| File | Description |
|------|-------------|
| `Infrastructure/Data/Configurations/AccountingOutboxMessageConfiguration.cs` | EF Core config |
| `Infrastructure/Data/Configurations/CommerceAccountingAuditConfiguration.cs` | EF Core config |

### Migrations
| File | Description |
|------|-------------|
| `Infrastructure/Migrations/Sql/20260206_Phase35_ProductionHardening.sql` | Database migration |

---

## 🔐 Guarantees

### 1. Exactly-Once Posting

```
┌─────────────────────────────────────────────────────────────────┐
│                    DOUBLE IDEMPOTENCY SHIELD                    │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 1: Outbox Level                                          │
│  ─────────────────────────────────────────────                  │
│  UNIQUE INDEX (TenantId, IdempotencyKey)                        │
│  + Pre-check before insert                                      │
│                                                                 │
│  LAYER 2: Ledger Level                                          │
│  ─────────────────────────────────────────────                  │
│  PostJournalAsync returns existing journal if key matches       │
│  + Processor calls ExistsByIdempotencyKeyAsync before posting   │
└─────────────────────────────────────────────────────────────────┘
```

### 2. Atomic Transactions

```csharp
// CORRECT: Business + Outbox in same transaction
await _transactionBoundary.ExecuteAsync(async (db, outbox) =>
{
    var invoice = CreateSalesInvoice(...);
    db.SalesInvoices.Add(invoice);
    
    await outbox.EnqueuePosSaleAsync(tenantId, invoice.Id);
    
    // BOTH succeed or BOTH rollback
    return invoice;
});

// ❌ WRONG: Separate transactions
var invoice = await _salesService.CreateAsync(...);  // Transaction 1
await _outbox.EnqueuePosSaleAsync(...);              // Transaction 2 - CRASH HERE = LOST!
```

### 3. Crash Resilience

```
Timeline:
─────────────────────────────────────────────────────────────────
  T1: Business + Outbox saved (committed)
  T2: Server crashes ❌
  T3: Server restarts
  T4: OutboxProcessor reads pending message
  T5: Journal posted ✅
─────────────────────────────────────────────────────────────────
Result: No lost events. Outbox survives crash.
```

### 4. Retry with Exponential Backoff

```
Attempt 1: Immediate
Attempt 2: +1 minute
Attempt 3: +2 minutes
Attempt 4: +4 minutes
Attempt 5: +8 minutes
Attempt 6: FAILED (manual intervention required)
```

---

## 🔧 Usage Examples

### POS Sale with Accounting

```csharp
public class SalesService
{
    private readonly IAccountingTransactionBoundary _txBoundary;
    private readonly ITenantContext _tenant;
    
    public async Task<SalesInvoice> CreateSaleAsync(CreateSaleRequest request)
    {
        return await _txBoundary.ExecuteAsync(async (db, outbox) =>
        {
            // 1. Create the sale
            var invoice = new SalesInvoice
            {
                TenantId = _tenant.TenantId,
                InvoiceNumber = await GenerateInvoiceNumberAsync(db),
                TotalAmount = request.TotalAmount,
                PaymentMethod = request.PaymentMethod,
                // ... other properties
            };
            
            foreach (var item in request.Items)
            {
                invoice.Items.Add(new SalesInvoiceItem { ... });
            }
            
            db.SalesInvoices.Add(invoice);
            
            // 2. Enqueue for accounting (SAME TRANSACTION!)
            await outbox.EnqueuePosSaleAsync(_tenant.TenantId, invoice.Id);
            
            return invoice;
        });
    }
}
```

### Stripe Webhook Handler

```csharp
[HttpPost("webhook")]
public async Task<IActionResult> HandleStripeWebhook()
{
    var stripeEvent = // ... parse webhook
    
    if (stripeEvent.Type == "payment_intent.succeeded")
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        var orderId = paymentIntent.Metadata["orderId"];
        
        await _txBoundary.ExecuteAsync(async (db, outbox) =>
        {
            // 1. Update order status
            var order = await db.Orders.FindAsync(orderId);
            order.PaymentStatus = PaymentStatus.Paid;
            
            // 2. Enqueue for accounting
            await outbox.EnqueueOnlinePaymentAsync(
                order.TenantId,
                order.Id,
                paymentIntent.Id,
                "STRIPE");
        });
    }
    
    return Ok();
}
```

---

## 📊 Metrics & Alerts

### Counters

| Metric | Description |
|--------|-------------|
| `pos_sales_posted_total` | POS sales posted to ledger |
| `pos_purchases_posted_total` | POS purchases posted |
| `ecommerce_payments_posted_total` | E-commerce payments posted |
| `refunds_posted_total` | Refunds posted |
| `settlements_posted_total` | Settlements posted |
| `accounting_failures_total` | Posting failures |
| `idempotent_calls_total` | Duplicate (idempotent) calls |
| `outbox_pending_count` | Pending messages in outbox |
| `outbox_failed_count` | Failed messages requiring intervention |

### Alert Thresholds

```yaml
alerts:
  - name: OutboxBacklog
    condition: outbox_pending_count > 100
    severity: warning
    action: Check for processing issues
    
  - name: FailedMessages
    condition: outbox_failed_count > 0
    severity: critical
    action: Manual intervention required
    
  - name: HighFailureRate
    condition: accounting_failures_total increases by > 10 in 1h
    severity: critical
    action: Check connectivity and validation
```

---

## 🗃️ Database Tables

### AccountingOutboxMessages

```sql
CREATE TABLE "AccountingOutboxMessages" (
    "Id" UUID PRIMARY KEY,
    "TenantId" UUID NOT NULL,
    "EventType" VARCHAR(50) NOT NULL,      -- PosSale, OnlinePayment, etc.
    "BusinessEntityId" UUID NOT NULL,       -- Invoice ID, Order ID, etc.
    "PayloadJson" TEXT NOT NULL,            -- Serialized event data
    "IdempotencyKey" VARCHAR(255) NOT NULL, -- SALE:tenant:invoiceId
    "OccurredAtUtc" TIMESTAMP NOT NULL,
    "ProcessedAtUtc" TIMESTAMP,             -- NULL = pending
    "JournalId" UUID,                       -- Result journal ID
    "AttemptCount" INTEGER DEFAULT 0,
    "LastError" VARCHAR(2000),
    "NextRetryAtUtc" TIMESTAMP,
    "IsFailed" BOOLEAN DEFAULT false
);

-- Critical indexes
UNIQUE INDEX (TenantId, IdempotencyKey)
INDEX (ProcessedAtUtc, IsFailed) WHERE ProcessedAtUtc IS NULL AND IsFailed = false
```

### CommerceAccountingAudit

```sql
CREATE TABLE "CommerceAccountingAudit" (
    "Id" UUID PRIMARY KEY,
    "TenantId" UUID NOT NULL,
    "BusinessType" VARCHAR(50) NOT NULL,    -- SalesInvoice, Order, Return
    "BusinessId" UUID NOT NULL,             -- The entity ID
    "BusinessReference" VARCHAR(100),       -- Invoice number, order number
    "JournalId" UUID NOT NULL,
    "JournalNumber" VARCHAR(50),
    "OperationType" VARCHAR(50) NOT NULL,   -- Sale, COGS, Payment, Refund
    "Amount" DECIMAL(18, 4),
    "PostedAtUtc" TIMESTAMP NOT NULL,
    "IdempotencyKey" VARCHAR(255),
    "WasIdempotent" BOOLEAN DEFAULT false,
    
    FOREIGN KEY (JournalId) REFERENCES JournalEntries(Id)
);

-- Lookup indexes
INDEX (TenantId, BusinessType, BusinessId)  -- "What journals for this sale?"
INDEX (JournalId)                            -- "What created this journal?"
```

---

## 🔄 Background Worker

### Configuration

```csharp
// In Program.cs
builder.Services.AddCoreLedgerEngine();
builder.Services.AddAccountingOutboxWorker(); // Adds the background worker
```

### Behavior

- **Poll Interval**: 5 seconds (when processing messages)
- **Idle Interval**: 30 seconds (when no pending messages)
- **Batch Size**: 50 messages per run
- **Retry Strategy**: Exponential backoff up to 5 attempts
- **Graceful Shutdown**: Completes current batch before stopping

---

## 🧪 Testing Scenarios

### Chaos Tests Required

| Scenario | Expected Result |
|----------|-----------------|
| DB crash during sale | Outbox survives, journal posts on restart |
| Same webhook received twice | Second call is idempotent, no duplicate journal |
| Concurrent mixed payments | Each posts exactly once |
| Network timeout during posting | Retry succeeds, only one journal created |
| Invalid data in outbox | Fails after 5 attempts, marked as failed |

### Verification Queries

```sql
-- Check for orphaned business records (no journal)
SELECT s.Id, s.InvoiceNumber, s.TotalAmount
FROM "SalesInvoices" s
LEFT JOIN "CommerceAccountingAudit" a ON a."BusinessId" = s.Id
WHERE a.Id IS NULL AND s."CreatedAt" < NOW() - INTERVAL '1 hour';

-- Check for pending outbox messages
SELECT * FROM "v_AccountingOutboxPending";

-- Check for failed messages requiring intervention
SELECT * FROM "v_AccountingOutboxFailed";
```

---

## 🚀 DI Registration

```csharp
// Program.cs
builder.Services.AddCoreLedgerEngine();
builder.Services.AddAccountingOutboxWorker();
```

Registers:
- `IFinancialPostingService` (Phase 1)
- `ITenantAccountSeeder` (Phase 2)
- `ITreasuryTransferService` (Phase 2)
- `IAccountResolver` (Phase 3)
- `ICommerceAccountingService` (Phase 3)
- `IAccountingOutboxService` (Phase 3.5)
- `IAccountingOutboxProcessor` (Phase 3.5)
- `IAccountingTransactionBoundary` (Phase 3.5)
- `IAccountingMetrics` (Phase 3.5 - Singleton)
- `AccountingOutboxWorker` (Phase 3.5 - HostedService)

---

## ✅ Definition of Done

| Requirement | Status |
|-------------|--------|
| Outbox pattern implemented | ✅ |
| Transaction boundary enforced | ✅ |
| Idempotency double-check | ✅ |
| Exponential backoff retry | ✅ |
| Audit trail created | ✅ |
| Metrics visible | ✅ |
| Background worker | ✅ |
| SQL migration | ✅ |
| DI registration | ✅ |

---

## 🔜 Next Steps

### Integration Phase
1. Update `SalesController` to use `IAccountingTransactionBoundary`
2. Update `StripeWebhookController` to use transaction boundary
3. Add metrics endpoint for monitoring
4. Deploy and run migration

### Phase 4: Financial Reports
- Trial Balance
- General Ledger
- Profit & Loss Statement
- Balance Sheet
- Cash Flow Statement
- Clearing Aging Report
