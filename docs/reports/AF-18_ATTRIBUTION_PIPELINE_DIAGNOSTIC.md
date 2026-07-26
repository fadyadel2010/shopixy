# AF-18: Affiliate Attribution Pipeline Diagnostic Report

## Executive Summary

**PROBLEM**: Customer used referral code `?ref=ZEZO906409`, order was placed and completed, but affiliate "ZEZO" does NOT appear in `/admin/affiliates`.

**ROOT CAUSE CANDIDATES** (in order of likelihood):
1. **MongoDB Dashboard Projection Failure** - Commission recorded in PostgreSQL but MongoDB dashboard not updated
2. **Commission Recording Failure** - AffiliateAttribution exists but commission ledger entries not created
3. **Product Policy Missing** - Product not enabled for affiliate program
4. **Attribution Creation Failure** - Order stamped but AffiliateAttribution not created
5. **Middleware Stamping Failure** - Referral code not captured or affiliate mode not activated

---

## Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 1: REFERRAL CAPTURE                                                │
│ AffiliateReferralMiddleware                                             │
│ • Captures ?ref=ZEZO906409 from query param or X-Affiliate-Ref header  │
│ • Resolves AffiliatePromotionLink (v2) or AffiliateAccount (v1)        │
│ • Sets AffiliateModeResolver context (request-scoped)                   │
│ • Publishes AffiliateLinkClickedEvent (analytics)                       │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 2: ORDER STAMPING                                                  │
│ CheckoutService.CreateOrderAsync() ~line 1906                           │
│ • Reads AffiliateAccountId from AffiliateModeResolver                   │
│ • Stamps Order.AffiliateAccountId                                       │
│ • Stamps Order.AffiliateReferralCode (snapshot)                         │
│ • Stamps Order.PromotionLinkId (if v2 flow)                             │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 3: ATTRIBUTION CREATION (INLINE)                                   │
│ CheckoutService.CreateOrderAsync() ~line 2000-2030                      │
│ • Creates AffiliateAttribution entity                                   │
│ • Links OrderId → AffiliateAccountId                                    │
│ • Committed in SAME TRANSACTION as Order + Shipment                     │
│ • CRITICAL: Must exist before payment capture                           │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 4: ORDER FULFILLMENT                                               │
│ Admin changes order status: Pending → Delivered → Completed             │
│ • FulfillmentDeliveredEvent published                                   │
│ • OrderCompletedEvent published                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 5: COMMISSION RECORDING                                            │
│ AffiliatePaymentCapturedConsumers (3 consumers for different events)    │
│ • CodDeliveredAffiliateConsumer                                         │
│ • OnlinePaymentCapturedAffiliateConsumer                                │
│ • WalletPaymentCapturedAffiliateConsumer                                │
│ • Calls AffiliateService.RecordCommissionForOrderAsync()                │
│ • Looks up AffiliateAttribution (MUST exist)                            │
│ • Looks up AffiliateProductPolicies (MUST be enabled)                   │
│ • Creates AffiliateCommissionLedger entries (EntryType = Earned)        │
│ • Publishes AffiliateCommissionEarnedEvent                              │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 6: MONGODB PROJECTION                                              │
│ AffiliateCommissionEarnedProjectionConsumer                             │
│ • Updates AffiliateDashboardDocument (TotalEarned, TotalOrders)         │
│ • Updates AffiliateOrderDocument (commission items)                     │
│ • Updates AffiliateEarningsSummaryDocument (per-currency)               │
│ • Idempotent via ProcessedEventIds                                      │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 7: ADMIN QUERY                                                     │
│ AdminAffiliateController.ListAffiliates() ~line 67-152                  │
│ • Queries PostgreSQL AffiliateAccounts (Status, DisplayName, etc.)      │
│ • Queries MongoDB AffiliateDashboards (TotalEarned, TotalOrders)        │
│ • Joins by AffiliateAccountId                                           │
│ • Returns merged result to /admin/affiliates                            │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Diagnostic Queries

### Phase 1: PostgreSQL Verification

Run the SQL queries in `af18-diagnostic-queries.sql` to verify:

1. **AffiliateAccount exists** for ZEZO with Status = Active
2. **Order.AffiliateAccountId** is populated (middleware stamped correctly)
3. **AffiliateAttribution** was created (checkout transaction succeeded)
4. **AffiliateCommissionLedger** has Earned entries (commission recorded)
5. **AffiliateProductPolicy** exists and IsEnabled = true (product eligible)

### Phase 2: MongoDB Verification

Run the MongoDB queries in `af18-mongodb-diagnostic.js` to verify:

1. **AffiliateDashboardDocument** exists for ZEZO's AffiliateAccountId
2. **TotalEarned > 0** and **TotalOrders > 0** in dashboard
3. **AffiliateOrderDocument** exists with commission items
4. **ProcessedEventIds** array is populated (events were consumed)

---

## Failure Signatures & Fixes

### Scenario A: AffiliateAccount Missing or Inactive

**SQL Evidence**:
```sql
-- STEP 1 returns no rows OR Status != 1
SELECT * FROM "AffiliateAccounts" WHERE ReferralCode ILIKE '%ZEZO%';
```

**Root Cause**: Affiliate never registered or account was deactivated.

**Fix**:
1. Check affiliate registration flow
2. Verify email confirmation if required
3. Check if admin manually deactivated the account
4. Reactivate account:
```sql
UPDATE "AffiliateAccounts"
SET Status = 1, ActivatedAtUtc = NOW()
WHERE ReferralCode = 'ZEZO906409' AND IsDeleted = false;
```

---

### Scenario B: Order Not Stamped with AffiliateAccountId

**SQL Evidence**:
```sql
-- STEP 2 returns rows but AffiliateAccountId IS NULL
SELECT OrderNumber, AffiliateAccountId, AffiliateReferralCode
FROM "Orders"
WHERE AffiliateReferralCode ILIKE '%ZEZO%';
```

**Root Cause**: `AffiliateReferralMiddleware` didn't capture referral or `AffiliateModeResolver` context was lost.

**Possible Causes**:
- Referral code was in URL but middleware didn't fire (path exclusion?)
- Cookie `AFF_SESSION` was blocked by browser
- Frontend didn't send `X-Affiliate-Ref` header for SPA API calls
- Checkout happened in different session/browser

**Fix**:
1. Check application logs for `[AffiliateMW] Ref found` entries
2. Verify middleware is registered in `Program.cs`
3. Check if storefront path is excluded from middleware
4. Verify frontend sends `X-Affiliate-Ref` header:
```javascript
// In storefront bootstrap
const ref = new URLSearchParams(window.location.search).get('ref');
if (ref) {
  axios.defaults.headers.common['X-Affiliate-Ref'] = ref;
}
```

**Manual Fix** (if order already exists):
```sql
-- Find AffiliateAccountId for ZEZO
SELECT Id FROM "AffiliateAccounts" WHERE ReferralCode = 'ZEZO906409';

-- Stamp the order (DANGEROUS - breaks audit trail)
UPDATE "Orders"
SET AffiliateAccountId = '<affiliate_account_id>',
    AffiliateReferralCode = 'ZEZO906409'
WHERE OrderNumber = '<order_number>';
```

---

### Scenario C: AffiliateAttribution Not Created

**SQL Evidence**:
```sql
-- STEP 3 returns no rows
SELECT * FROM "AffiliateAttributions"
WHERE ReferralCode ILIKE '%ZEZO%';
```

**Root Cause**: Checkout transaction failed or rolled back after order creation.

**Possible Causes**:
- Exception thrown after order creation but before attribution creation
- Database constraint violation
- Transaction timeout

**Fix**:
1. Check application logs for `[AF-11/15] AffiliateAttribution created inline` entries
2. Check for exceptions in `CheckoutService.CreateOrderAsync()` around line 2000-2030
3. Verify database constraints on `AffiliateAttributions` table

**Manual Fix** (create attribution retroactively):
```sql
-- Get order details
SELECT Id, TenantId, AffiliateAccountId, AffiliateReferralCode, PromotionLinkId
FROM "Orders"
WHERE OrderNumber = '<order_number>';

-- Create attribution manually
INSERT INTO "AffiliateAttributions" (
  Id, TenantId, OrderId, AffiliateAccountId, ReferralCode, Source, PromotionLinkId,
  CreatedAt, CreatedBy, IsDeleted
)
VALUES (
  gen_random_uuid(),
  '<tenant_id>',
  '<order_id>',
  '<affiliate_account_id>',
  'ZEZO906409',
  0, -- ReferralLink
  '<promotion_link_id_or_null>',
  NOW(),
  NULL,
  false
);
```

---

### Scenario D: Commission Not Recorded (Product Policy Missing)

**SQL Evidence**:
```sql
-- STEP 4 returns no rows
SELECT * FROM "AffiliateCommissionLedger"
WHERE AffiliateAccountId = '<affiliate_account_id>';

-- STEP 5 returns no rows or IsEnabled = false
SELECT * FROM "AffiliateProductPolicies"
WHERE ProductId IN (SELECT ProductId FROM "OrderItems" WHERE OrderId = '<order_id>');
```

**Root Cause**: Product not enabled for affiliate program.

**Fix**:
1. Admin must enable product in affiliate settings
2. Navigate to `/admin/products/{productId}/affiliate`
3. Enable affiliate program and set commission rate

**SQL Fix**:
```sql
-- Enable product for affiliate program
INSERT INTO "AffiliateProductPolicies" (
  Id, TenantId, ProductId, CommissionRate, MaxCommissionPerItem, IsEnabled,
  CreatedAt, CreatedBy, IsDeleted
)
VALUES (
  gen_random_uuid(),
  '<tenant_id>',
  '<product_id>',
  0.10, -- 10% commission
  NULL, -- No max cap
  true,
  NOW(),
  NULL,
  false
)
ON CONFLICT (TenantId, ProductId) DO UPDATE
SET IsEnabled = true, CommissionRate = 0.10;
```

**Trigger Commission Recording** (if attribution exists but commission missing):
```sql
-- Find the order
SELECT Id FROM "Orders" WHERE OrderNumber = '<order_number>';

-- Manually trigger commission recording by publishing event
-- This requires application restart or manual event publishing via RabbitMQ
```

---

### Scenario E: Commission Recorded but MongoDB Not Updated

**SQL Evidence**:
```sql
-- STEP 4 returns rows (commission exists in PostgreSQL)
SELECT * FROM "AffiliateCommissionLedger"
WHERE AffiliateAccountId = '<affiliate_account_id>';
```

**MongoDB Evidence**:
```javascript
// QUERY 1 returns no document OR TotalEarned = 0
db.affiliate_dashboards.find({ affiliateAccountId: "<affiliate_account_id>" })
```

**Root Cause**: `AffiliateCommissionEarnedProjectionConsumer` failed or never ran.

**Possible Causes**:
- RabbitMQ connection lost
- Consumer not registered in DI container
- MongoDB connection failed
- Event not published after commission creation
- Consumer threw exception and message was dead-lettered

**Fix**:
1. Check application logs for `[Affiliate:Projection:Earned]` entries
2. Check RabbitMQ management UI for unprocessed messages in queue
3. Check RabbitMQ dead-letter queue for failed messages
4. Verify MongoDB connection string in `appsettings.json`
5. Verify consumer is registered in `Program.cs`:
```csharp
services.AddScoped<AffiliateCommissionEarnedProjectionConsumer>();
```

**Manual Fix** (create dashboard document):
```javascript
// Get AffiliateAccount details from SQL first
db.affiliate_dashboards.insertOne({
  _id: UUID(),
  affiliateAccountId: "<affiliate_account_id>",
  tenantId: "<tenant_id>",
  displayName: "ZEZO",
  referralCode: "ZEZO906409",
  totalEarned: NumberDecimal("50.00"), // Sum from SQL
  totalReversed: NumberDecimal("0"),
  netEarnings: NumberDecimal("50.00"),
  totalOrders: 1,
  totalApproved: NumberDecimal("0"),
  totalPaid: NumberDecimal("0"),
  lastCommissionAtUtc: new Date(),
  updatedAtUtc: new Date(),
  processedEventIds: []
})
```

---

### Scenario F: Dashboard Exists but Admin Query Filters It Out

**SQL Evidence**:
```sql
-- STEP 7 returns rows
SELECT * FROM "AffiliateAccounts"
WHERE ReferralCode ILIKE '%ZEZO%' AND IsDeleted = false;
```

**MongoDB Evidence**:
```javascript
// Dashboard exists with TotalEarned > 0
db.affiliate_dashboards.find({ referralCode: /ZEZO/i })
```

**Root Cause**: Admin query has incorrect filter or join logic.

**Possible Causes**:
- TenantId mismatch between PostgreSQL and MongoDB
- AffiliateAccountId mismatch (Guid format issue)
- Admin query filters by Status and ZEZO is not Active
- MongoDB collection name mismatch

**Fix**:
1. Verify `AdminAffiliateController.ListAffiliates()` line 67-152
2. Check MongoDB collection name is `affiliate_dashboards`
3. Verify TenantId and AffiliateAccountId match between SQL and MongoDB
4. Check if admin is filtering by Status (default should show all)

**Debug Query**:
```csharp
// In AdminAffiliateController.ListAffiliates()
// Add logging before MongoDB query
_logger.LogInformation(
    "Querying MongoDB for affiliateIds: {Ids}",
    string.Join(", ", affiliateIds));

// After MongoDB query
_logger.LogInformation(
    "MongoDB returned {Count} dashboard docs",
    dashboardDocs.Count);
```

---

## Verification Steps

After applying any fix, verify the complete pipeline:

### 1. Verify PostgreSQL Data
```sql
-- Should return 1 row with all fields populated
SELECT 
    o.OrderNumber,
    o.AffiliateAccountId,
    aa.Id AS AttributionId,
    COUNT(acl.Id) AS CommissionEntries,
    SUM(acl.CommissionAmount) AS TotalCommission
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa ON o.Id = aa.OrderId
INNER JOIN "AffiliateCommissionLedger" acl ON aa.Id = acl.AttributionId
WHERE o.OrderNumber = '<order_number>'
GROUP BY o.OrderNumber, o.AffiliateAccountId, aa.Id;
```

### 2. Verify MongoDB Data
```javascript
// Should return 1 document with TotalEarned > 0
db.affiliate_dashboards.findOne({
  affiliateAccountId: "<affiliate_account_id>"
})
```

### 3. Verify Admin API
```bash
# Call the admin API directly
curl -X GET "https://your-api.com/api/admin/affiliates?page=1&pageSize=20" \
  -H "Authorization: Bearer <admin_token>" \
  -H "X-Tenant-Id: <tenant_id>"
```

### 4. Verify Frontend
- Navigate to `/admin/affiliates`
- Search for "ZEZO"
- Verify affiliate appears with correct TotalEarned and TotalOrders

---

## Regression Tests

Add these tests to prevent future occurrences:

### Test 1: End-to-End Attribution Flow
```csharp
[Fact]
public async Task CompleteOrder_WithReferralCode_CreatesAttributionAndCommission()
{
    // Arrange: Create affiliate, product policy, and referral link
    var affiliate = await CreateTestAffiliate("TESTREF");
    var product = await CreateTestProduct();
    await EnableAffiliatePolicy(product.Id, 0.10m);
    
    // Act: Place order with referral code
    var order = await PlaceOrderWithReferral("TESTREF", product.Id);
    await MarkOrderAsDelivered(order.Id);
    await Task.Delay(2000); // Wait for async consumers
    
    // Assert: Verify complete pipeline
    var attribution = await GetAttribution(order.Id);
    Assert.NotNull(attribution);
    
    var commission = await GetCommissionLedger(order.Id);
    Assert.NotEmpty(commission);
    
    var dashboard = await GetMongoDbDashboard(affiliate.Id);
    Assert.NotNull(dashboard);
    Assert.True(dashboard.TotalEarned > 0);
    Assert.Equal(1, dashboard.TotalOrders);
}
```

### Test 2: Admin Query Returns Affiliate
```csharp
[Fact]
public async Task AdminAffiliates_WithCommission_ReturnsAffiliate()
{
    // Arrange: Create affiliate with commission
    var affiliate = await CreateAffiliateWithCommission();
    
    // Act: Call admin API
    var response = await AdminClient.GetAsync("/api/admin/affiliates");
    var result = await response.Content.ReadAsAsync<AdminAffiliateListResponse>();
    
    // Assert: Affiliate appears in list
    Assert.Contains(result.Affiliates, a => a.AffiliateAccountId == affiliate.Id);
    Assert.True(result.Affiliates.First(a => a.AffiliateAccountId == affiliate.Id).TotalEarned > 0);
}
```

---

## Monitoring & Alerts

Add these metrics to prevent silent failures:

### 1. Commission Recording Lag
```csharp
// Alert if commission not recorded within 5 minutes of order completion
var lag = DateTime.UtcNow - order.CompletedAtUtc;
if (lag > TimeSpan.FromMinutes(5) && !HasCommission(order.Id))
{
    _logger.LogError("Commission recording lag detected for Order {OrderId}", order.Id);
    _telemetry.RecordLag(order.TenantId, lag.TotalSeconds);
}
```

### 2. MongoDB Projection Lag
```csharp
// Alert if MongoDB dashboard not updated within 2 minutes of commission creation
var projectionLag = DateTime.UtcNow - commissionEntry.RecordedAtUtc;
if (projectionLag > TimeSpan.FromMinutes(2) && !DashboardHasCommission(affiliate.Id))
{
    _logger.LogError("MongoDB projection lag detected for Affiliate {AffiliateId}", affiliate.Id);
}
```

### 3. Missing Attribution Rate
```csharp
// Alert if >5% of orders with AffiliateAccountId have no AffiliateAttribution
var ordersWithAffiliate = await _dbContext.Orders
    .Where(o => o.AffiliateAccountId != null)
    .CountAsync();
    
var ordersWithAttribution = await _dbContext.AffiliateAttributions
    .CountAsync();
    
var missingRate = 1.0 - (ordersWithAttribution / (double)ordersWithAffiliate);
if (missingRate > 0.05)
{
    _logger.LogError("High missing attribution rate: {Rate:P}", missingRate);
}
```

---

## Next Steps

1. **Run SQL diagnostic queries** (`af18-diagnostic-queries.sql`) to identify broken step
2. **Run MongoDB diagnostic queries** (`af18-mongodb-diagnostic.js`) to verify projection
3. **Check application logs** for errors in the identified step
4. **Apply appropriate fix** from the scenarios above
5. **Verify end-to-end** using verification steps
6. **Add regression tests** to prevent recurrence
7. **Set up monitoring** to catch future failures early

---

## Files Modified/Created

- `af18-diagnostic-queries.sql` - PostgreSQL diagnostic queries
- `af18-mongodb-diagnostic.js` - MongoDB diagnostic queries
- `AF-18_ATTRIBUTION_PIPELINE_DIAGNOSTIC.md` - This document

## Related Files (Read-Only)

- `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs` - Step 1: Referral capture
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` - Step 2-3: Order stamping & attribution
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs` - Step 5: Commission recording
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs` - Step 6: MongoDB projection
- `PointOfSale.API/Controllers/AdminAffiliateController.cs` - Step 7: Admin query
- `PointOfSale.Core/Entities/Order.cs` - Order entity with affiliate fields
- `PointOfSale.Core/Entities/Affiliate/AffiliateAttribution.cs` - Attribution entity
- `PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs` - Commission ledger entity
- `PointOfSale.Infrastructure/MongoDB/Documents/Affiliate/AffiliateDocuments.cs` - MongoDB documents

---

**CRITICAL**: Do NOT guess the root cause. Run the diagnostic queries first to identify the exact broken step, then apply the appropriate fix with verification.
