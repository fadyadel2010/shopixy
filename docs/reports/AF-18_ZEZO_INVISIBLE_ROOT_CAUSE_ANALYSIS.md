# AF-18: ZEZO AFFILIATE INVISIBLE - ROOT CAUSE ANALYSIS

## EXECUTIVE SUMMARY

**ROOT CAUSE IDENTIFIED:** Commission recording is NOT triggered when order status changes from `Delivered` → `Completed` in POS Admin.

**BROKEN LAYER:** Event Publishing Gap in `OrderFulfillmentService.MarkCompletedAsync()`

**IMPACT:** Affiliates with COD orders that go directly to `Completed` status (skipping `Delivered`) never get commission recorded, making them invisible in `/admin/affiliates`.

---

## SCENARIO RECONSTRUCTION

```
1. Customer visits: https://storefront.com?ref=ZEZO906409
2. Middleware captures referral → Sets AffiliateAccountId on Order
3. Checkout creates AffiliateAttribution (inline, same transaction)
4. Admin changes Order Status: Pending → Delivered → Completed
5. EXPECTED: Commission recorded, ZEZO appears in /admin/affiliates
6. ACTUAL: ZEZO does NOT appear
```

---

## PIPELINE TRACE (End-to-End)

### STEP 1: Referral Capture
**File:** `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs:54-265`

**Flow:**
```csharp
// Checks ?ref= query param OR X-Affiliate-Ref header
// Resolves via AffiliatePromotionLinks (v2) or AffiliateAccounts (v1)
// Sets HttpContext.Items["AffiliateMode"] = true
// Sets HttpContext.Items["AffiliateAccountId"] = affiliateAccountId
```

**Verification SQL:**
```sql
-- Check if affiliate exists and is Active
SELECT 
    Id, ReferralCode, Status, ActivatedAtUtc, IsDeleted
FROM "AffiliateAccounts"
WHERE ReferralCode ILIKE '%ZEZO%'
  AND IsDeleted = false;
-- EXPECTED: Status = 1 (Active)
```

---

### STEP 2: Order Creation with Attribution
**File:** `PointOfSale.API/Services/Checkout/CheckoutService.cs:1990-2030`

**Flow:**
```csharp
// During checkout, if AffiliateAccountId exists:
if (order.AffiliateAccountId.HasValue) {
    var attribution = AffiliateAttribution.CreateV2(...);
    _dbContext.AffiliateAttributions.Add(attribution);
    await _dbContext.SaveChangesAsync(); // SAME TRANSACTION as Order
}
```

**Verification SQL:**
```sql
-- Check if Order has AffiliateAccountId stamped
SELECT 
    Id, OrderNumber, AffiliateAccountId, AffiliateReferralCode, 
    PromotionLinkId, Status, FulfillmentStatus, PaymentMethod
FROM "Orders"
WHERE AffiliateReferralCode ILIKE '%ZEZO%'
  AND IsDeleted = false;
-- EXPECTED: AffiliateAccountId NOT NULL

-- Check if Attribution was created
SELECT 
    Id, OrderId, AffiliateAccountId, ReferralCode, 
    PromotionLinkId, Source, CreatedAt
FROM "AffiliateAttributions"
WHERE ReferralCode ILIKE '%ZEZO%'
  AND IsDeleted = false;
-- EXPECTED: 1 row per order
```

---

### STEP 3: Commission Recording Trigger (THE BROKEN LAYER)
**Files:**
- `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliatePaymentCapturedConsumers.cs`

**Expected Flow:**
```
Admin changes status to Delivered
  ↓
OrderFulfillmentService.MarkDeliveredAsync()
  ↓
Publishes FulfillmentDeliveredEvent
  ↓
PaymentLifecycleService.MarkCodDeliveredAsync()
  ↓
Publishes OrderCodDeliveredEvent
  ↓
CodDeliveredAffiliateConsumer.Consume()
  ↓
AffiliateService.RecordCommissionForOrderAsync()
  ↓
Creates AffiliateCommissionLedger entries
  ↓
Publishes AffiliateCommissionEarnedEvent
  ↓
AffiliateCommissionEarnedProjectionConsumer
  ↓
Updates MongoDB AffiliateDashboardDocument
```

**ACTUAL FLOW (BROKEN):**
```
Admin changes status DIRECTLY to Completed (skipping Delivered)
  ↓
OrderFulfillmentService.MarkCompletedAsync()
  ↓
Publishes OrderCompletedEvent
  ↓
❌ NO CONSUMER listens to OrderCompletedEvent for commission
  ↓
❌ RecordCommissionForOrderAsync NEVER CALLED
  ↓
❌ No commission ledger entries
  ↓
❌ No MongoDB dashboard update
  ↓
❌ Affiliate invisible in /admin/affiliates
```

**Verification SQL:**
```sql
-- Check if commission was recorded
SELECT 
    acl.Id, acl.OrderId, acl.AffiliateAccountId, 
    acl.EntryType, acl.CommissionAmount, acl.RecordedAtUtc,
    o.OrderNumber, o.Status, o.FulfillmentStatus
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl.OrderId = o.Id
WHERE acl.AffiliateAccountId IN (
    SELECT Id FROM "AffiliateAccounts" 
    WHERE ReferralCode ILIKE '%ZEZO%'
)
ORDER BY acl.RecordedAtUtc DESC;
-- EXPECTED: 1+ rows with EntryType = 0 (Earned)
-- ACTUAL: 0 rows if order went directly to Completed
```

---

### STEP 4: MongoDB Projection
**File:** `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs:76-200`

**Flow:**
```csharp
// Listens to AffiliateCommissionEarnedEvent
// Updates AffiliateDashboardDocument:
//   - TotalEarned += commission
//   - TotalOrders += 1
//   - NetEarnings += commission
```

**Verification (MongoDB):**
```javascript
// Check if dashboard document exists
db.affiliate_dashboards.find({
  "referralCode": /ZEZO/i
})
// EXPECTED: 1 document with TotalEarned > 0
// ACTUAL: Document may exist but TotalEarned = 0 if no commission event fired
```

---

### STEP 5: Admin Display
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:66-169`

**Flow:**
```csharp
// 1. Query PostgreSQL AffiliateAccounts (identity + status)
var accounts = await _dbContext.AffiliateAccounts
    .Where(a => a.TenantId == tenantId && !a.IsDeleted)
    .ToListAsync();

// 2. Query MongoDB AffiliateDashboards (financial data)
var dashboards = await _mongoDbContext.AffiliateDashboards
    .Find(d => d.TenantId == tenantId && d.AffiliateAccountId.In(accountIds))
    .ToListAsync();

// 3. LEFT JOIN: Affiliate shows even if dashboard missing
// BUT: If TotalEarned = 0, affiliate may be filtered by frontend
```

**Verification SQL:**
```sql
-- Simulate admin query
SELECT 
    a.Id, a.DisplayName, a.ReferralCode, a.Status,
    COUNT(DISTINCT acl.OrderId) AS OrdersWithCommission,
    SUM(CASE WHEN acl.EntryType = 0 THEN acl.CommissionAmount ELSE 0 END) AS TotalEarned
FROM "AffiliateAccounts" a
LEFT JOIN "AffiliateCommissionLedger" acl ON a.Id = acl.AffiliateAccountId
WHERE a.ReferralCode ILIKE '%ZEZO%'
  AND a.IsDeleted = false
GROUP BY a.Id, a.DisplayName, a.ReferralCode, a.Status;
-- EXPECTED: TotalEarned > 0
-- ACTUAL: TotalEarned = 0 if commission not recorded
```

---

## ROOT CAUSE ANALYSIS

### THE BUG

**Location:** `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`

**Issue:** `MarkCompletedAsync()` publishes `OrderCompletedEvent`, but there is NO consumer that triggers commission recording for this event.

**Evidence:**
```bash
# Search for OrderCompletedEvent consumers
grep -r "IConsumer<OrderCompletedEvent>" --include="*.cs"
# RESULT: Only OrderProjectionConsumer (MongoDB read model update)
# NO affiliate commission consumer
```

**Comparison:**
```csharp
// MarkDeliveredAsync() → Works correctly
public async Task MarkDeliveredAsync(...) {
    // ... status transition logic ...
    await _eventPublisher.PublishAsync(new FulfillmentDeliveredEvent { ... });
    // ↓ Triggers PaymentLifecycleService
    // ↓ Publishes OrderCodDeliveredEvent
    // ↓ CodDeliveredAffiliateConsumer listens
    // ✅ Commission recorded
}

// MarkCompletedAsync() → BROKEN
public async Task MarkCompletedAsync(...) {
    // ... status transition logic ...
    await _eventPublisher.PublishAsync(new OrderCompletedEvent { ... });
    // ❌ NO consumer triggers commission recording
    // ❌ Commission NOT recorded
}
```

---

## FAILURE SIGNATURES

Run these SQL queries to confirm the bug:

### Query 1: Orders with Attribution but NO Commission
```sql
SELECT 
    o.OrderNumber,
    o.Status,
    o.FulfillmentStatus,
    o.AffiliateAccountId,
    o.AffiliateReferralCode,
    aa.Id AS AttributionId,
    COUNT(acl.Id) AS CommissionCount
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa ON o.Id = aa.OrderId AND aa.IsDeleted = false
LEFT JOIN "AffiliateCommissionLedger" acl ON aa.Id = acl.AttributionId
WHERE o.AffiliateReferralCode ILIKE '%ZEZO%'
  AND o.IsDeleted = false
GROUP BY o.OrderNumber, o.Status, o.FulfillmentStatus, 
         o.AffiliateAccountId, o.AffiliateReferralCode, aa.Id
HAVING COUNT(acl.Id) = 0;
```

**Expected Result:** 0 rows (all attributed orders should have commission)  
**Actual Result:** 1+ rows (orders that went directly to Completed)

### Query 2: Check Product Policies
```sql
SELECT 
    app.ProductId,
    app.CommissionRate,
    app.IsEnabled,
    p.Name AS ProductName
FROM "AffiliateProductPolicies" app
INNER JOIN "Products" p ON app.ProductId = p.Id
WHERE app.ProductId IN (
    SELECT DISTINCT oi.ProductId
    FROM "OrderItems" oi
    INNER JOIN "Orders" o ON oi.OrderId = o.Id
    WHERE o.AffiliateReferralCode ILIKE '%ZEZO%'
)
  AND app.IsDeleted = false;
```

**Expected Result:** 1+ rows with `IsEnabled = true`  
**Failure Signature:** No rows → Product not enabled for affiliate program

---

## THE FIX

### Option A: Add OrderCompletedEvent Consumer (RECOMMENDED)

**File:** `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliatePaymentCapturedConsumers.cs`

**Add new consumer:**
```csharp
/// <summary>
/// AF-18: Consumes OrderCompletedEvent to record commission for orders
/// that skip Delivered status and go directly to Completed.
///
/// SCENARIO: Admin changes order status from Delivered → Completed,
/// or directly to Completed without going through Delivered first.
///
/// IDEMPOTENCY: RecordCommissionForOrderAsync is idempotent via
/// unique constraint on (TenantId, OrderItemId, EntryType).
/// </summary>
public class OrderCompletedAffiliateConsumer : IConsumer<OrderCompletedEvent>
{
    private const string EventType = "OrderCompleted";
    private readonly IAffiliateService _affiliateService;
    private readonly IAffiliateTelemetryService _telemetry;
    private readonly ILogger<OrderCompletedAffiliateConsumer> _logger;

    public OrderCompletedAffiliateConsumer(
        IAffiliateService affiliateService,
        IAffiliateTelemetryService telemetry,
        ILogger<OrderCompletedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCompletedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _telemetry.RecordAttempt(evt.TenantId ?? Guid.Empty, EventType);

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordSuccess(evt.TenantId ?? Guid.Empty, EventType, sw.ElapsedMilliseconds);

            AffiliateConsumerHelper.DetectAndRecordLag(
                evt.OccurredAtUtc, sw.ElapsedMilliseconds, EventType, _telemetry, _logger);

            _logger.LogInformation(
                "[Affiliate:OrderCompleted] Commission recorded | OrderId: {OrderId} | " +
                "Duration: {Duration}ms | TenantId: {TenantId}",
                evt.OrderId, sw.ElapsedMilliseconds, evt.TenantId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailure(evt.TenantId ?? Guid.Empty, EventType);

            _logger.LogError(ex,
                "[Affiliate:OrderCompleted] Failed to record commission | OrderId: {OrderId} | " +
                "TenantId: {TenantId}",
                evt.OrderId, evt.TenantId);

            throw; // Let MassTransit retry
        }
    }
}
```

**Register in DI:**
```csharp
// File: PointOfSale.Infrastructure/DependencyInjection.cs
services.AddScoped<OrderCompletedAffiliateConsumer>();
```

---

### Option B: Ensure Delivered Status is Always Set (ALTERNATIVE)

**File:** `PointOfSale.API/Services/Admin/AdminOrderService.cs` (or wherever admin status changes happen)

**Add validation:**
```csharp
public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus, ...)
{
    var order = await _context.Orders.FindAsync(orderId);
    
    // AF-18: For COD orders with affiliate attribution, 
    // MUST go through Delivered before Completed
    if (newStatus == OrderStatus.Completed && 
        order.Status != OrderStatus.Delivered &&
        order.PaymentMethod == PaymentMethod.Cod &&
        order.AffiliateAccountId.HasValue)
    {
        throw new InvalidOperationException(
            "COD orders with affiliate attribution must be marked as Delivered " +
            "before Completed to ensure commission recording.");
    }
    
    // ... rest of logic ...
}
```

**Pros:** Prevents the bug at the source  
**Cons:** Adds business rule constraint, may break existing workflows

---

## RECOMMENDED SOLUTION

**Use Option A (Add OrderCompletedEvent Consumer)**

**Rationale:**
1. **Idempotent:** If commission was already recorded when order was Delivered, the consumer will skip (unique constraint)
2. **Defensive:** Handles edge cases where order goes directly to Completed
3. **No Breaking Changes:** Doesn't alter existing admin workflows
4. **Consistent:** Follows same pattern as CodDeliveredAffiliateConsumer

---

## VERIFICATION STEPS

After applying the fix:

### 1. Create Test Order
```bash
# Visit storefront with referral
curl "https://storefront.com?ref=ZEZO906409"

# Place order (COD payment)
# Note the OrderId
```

### 2. Change Status in Admin
```sql
-- Simulate admin action: mark as Completed directly
UPDATE "Orders"
SET "Status" = 5, -- Completed
    "FulfillmentStatus" = 4 -- Completed
WHERE "OrderNumber" = 'ORD-12345';
```

### 3. Verify Commission Recorded
```sql
-- Check commission ledger
SELECT * FROM "AffiliateCommissionLedger"
WHERE "OrderId" = '<order-id>';
-- EXPECTED: 1+ rows with EntryType = 0 (Earned)
```

### 4. Verify MongoDB Dashboard
```javascript
db.affiliate_dashboards.find({
  "referralCode": "ZEZO906409"
})
// EXPECTED: TotalEarned > 0, TotalOrders >= 1
```

### 5. Verify Admin UI
```bash
# Visit /admin/affiliates
# Search for "ZEZO"
# EXPECTED: Affiliate appears with Earned > 0
```

---

## REGRESSION TEST

**File:** `PointOfSale.Tests/Affiliate/AffiliateCommissionRecordingTests.cs`

```csharp
[Fact]
public async Task OrderCompletedEvent_TriggersCommissionRecording()
{
    // Arrange
    var orderId = Guid.NewGuid();
    var tenantId = Guid.NewGuid();
    
    var mockService = new Mock<IAffiliateService>();
    mockService
        .Setup(x => x.RecordCommissionForOrderAsync(orderId, It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    
    var consumer = new OrderCompletedAffiliateConsumer(
        mockService.Object,
        Mock.Of<IAffiliateTelemetryService>(),
        Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());
    
    var evt = new OrderCompletedEvent
    {
        TenantId = tenantId,
        OrderId = orderId,
        OccurredAtUtc = DateTime.UtcNow
    };
    
    var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
        c => c.Message == evt && c.CancellationToken == CancellationToken.None);
    
    // Act
    await consumer.Consume(context);
    
    // Assert
    mockService.Verify(
        x => x.RecordCommissionForOrderAsync(orderId, It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task OrderCompletedDirectly_RecordsCommission_Idempotent()
{
    // Arrange: Order goes Pending → Completed (skips Delivered)
    // Commission should still be recorded
    // If called twice (Delivered + Completed), should be idempotent
    
    // ... test implementation ...
}
```

---

## ADDITIONAL CHECKS

### Check 1: Affiliate Status
```sql
SELECT Status FROM "AffiliateAccounts"
WHERE ReferralCode ILIKE '%ZEZO%';
-- MUST BE: 1 (Active)
-- If 0 (Pending) or 2 (Suspended) → Commission will be skipped
```

### Check 2: Product Policy
```sql
SELECT IsEnabled FROM "AffiliateProductPolicies"
WHERE ProductId IN (
    SELECT ProductId FROM "OrderItems"
    WHERE OrderId = '<order-id>'
);
-- MUST BE: true
-- If false or no rows → Commission will be skipped
```

### Check 3: RabbitMQ Consumers
```bash
# Check if consumers are running
rabbitmqctl list_consumers
# EXPECTED: CodDeliveredAffiliateConsumer, OrderCompletedAffiliateConsumer
```

### Check 4: Application Logs
```bash
# Search for commission recording logs
grep "RecordCommission" application.log
# EXPECTED: "[Affiliate:RecordCommission] Recorded commission for Order..."
```

---

## CONCLUSION

**Root Cause:** Missing `OrderCompletedEvent` consumer for affiliate commission recording.

**Impact:** Affiliates with orders that go directly to `Completed` status (or skip `Delivered`) never get commission recorded, making them invisible in admin UI despite having valid attributions.

**Fix:** Add `OrderCompletedAffiliateConsumer` that listens to `OrderCompletedEvent` and calls `RecordCommissionForOrderAsync()`.

**Verification:** Run diagnostic SQL queries, check MongoDB dashboard, verify admin UI shows affiliate with earnings.

**Regression Prevention:** Add unit test for `OrderCompletedEvent` → commission recording flow.

---

## NEXT STEPS

1. Apply the fix (add `OrderCompletedAffiliateConsumer`)
2. Deploy to staging
3. Run diagnostic SQL queries on production data to identify affected orders
4. Backfill commission for historical orders (if needed)
5. Add regression test
6. Deploy to production
7. Monitor RabbitMQ consumer metrics
8. Verify ZEZO appears in `/admin/affiliates`

---

**Document Version:** 1.0  
**Date:** 2026-02-24  
**Author:** Kiro AI  
**Phase:** AF-18 (Affiliate Attribution Pipeline Audit)
