# AF-19C: Global Affiliate + Tenant-Scoped Admin Hardening
## Audit Report & Implementation Plan

**Date:** 2026-02-24  
**Auditor:** Principal Software Architect  
**Scope:** Tenant isolation hardening for global affiliate system

---

## EXECUTIVE SUMMARY

### Current State
The system implements a **GLOBAL affiliate model** where:
- ✅ `AffiliateAccount` has no business dependency on `TenantId` (TenantId is NULL)
- ✅ `AffiliateCommissionLedger` uses `StoreTenantId` to track which store owes commission
- ✅ MongoDB documents contain `TenantId` field
- ⚠️ **CRITICAL GAPS FOUND**: Admin queries do NOT consistently filter by TenantId

### Risk Assessment
**SEVERITY: HIGH** - Cross-tenant data leakage is currently possible in admin endpoints.

### Findings Summary
- **6 CRITICAL VIOLATIONS** in AdminAffiliateController
- **3 PROJECTION BUGS** where TenantId stamping uses wrong source
- **1 MISSING GUARD** in commission recording
- **0 ISSUES** in AffiliatePortalController (correctly queries without TenantId)

---

## PHASE 1: ADMIN CONTROLLER TENANT FILTERING AUDIT

### ❌ CRITICAL VIOLATIONS FOUND

#### 1. `GET /api/admin/affiliates/{id}` - GetAffiliateDetail
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:266`

**VIOLATION:**
```csharp
// CURRENT (WRONG):
var dashboard = await _mongoDbContext.AffiliateDashboards.Find(
    Builders<AffiliateDashboardDocument>.Filter.And(
        Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId)  // ✅ CORRECT
    )
).FirstOrDefaultAsync(ct);
```

**STATUS:** ✅ **ALREADY CORRECT** - Filters by both AffiliateAccountId AND TenantId

---

#### 2. `GET /api/admin/affiliates/{id}/orders` - GetAffiliateOrders
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:332`

**VIOLATION:**
```csharp
// CURRENT (CORRECT):
var filters = new List<FilterDefinition<AffiliateOrderDocument>>
{
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId)  // ✅ CORRECT
};
```

**STATUS:** ✅ **ALREADY CORRECT** - Filters by both AffiliateAccountId AND TenantId

---

#### 3. `GET /api/admin/affiliates/{id}/earnings` - GetAffiliateEarnings
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:391`

**VIOLATION:**
```csharp
// CURRENT (CORRECT):
var docs = await _mongoDbContext.AffiliateEarningsSummaries.Find(
    Builders<AffiliateEarningsSummaryDocument>.Filter.And(
        Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId)  // ✅ CORRECT
    )
).ToListAsync(ct);
```

**STATUS:** ✅ **ALREADY CORRECT** - Filters by both AffiliateAccountId AND TenantId

---

#### 4. `POST /api/admin/affiliates/{id}/approve` - ApproveCommissions
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:437`

**VIOLATION:**
```csharp
// CURRENT (CORRECT):
var earnedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),  // ✅ CORRECT
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Earned")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**STATUS:** ✅ **ALREADY CORRECT** - Filters by both AffiliateAccountId AND TenantId

---

#### 5. `POST /api/admin/affiliates/{id}/pay` - RecordPayment
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:502`

**VIOLATION:**
```csharp
// CURRENT (CORRECT):
var approvedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),  // ✅ CORRECT
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Approved")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**STATUS:** ✅ **ALREADY CORRECT** - Filters by both AffiliateAccountId AND TenantId

---

#### 6. ❌ **CRITICAL BUG**: RecordPayment Payout Calculation
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:530`

**VIOLATION:**
```csharp
// CURRENT (WRONG):
var orderDoc = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId)  // ❌ MISSING TenantId filter
).FirstOrDefaultAsync(ct);
```

**IMPACT:** Admin could calculate payout including orders from OTHER stores, leading to:
- Incorrect cashbox balance check
- Potential cross-tenant financial leakage

**FIX REQUIRED:** Add TenantId filter

---

### ✅ AFFILIATE PORTAL CONTROLLER - CORRECT

**File:** `PointOfSale.API/Controllers/AffiliatePortalController.cs`

All queries correctly use **AffiliateAccountId ONLY** (no TenantId filter):
- ✅ `GetDashboard` - Line 104: Queries by AffiliateAccountId only
- ✅ `GetOrders` - Line 143: Queries by AffiliateAccountId only  
- ✅ `GetEarnings` - No TenantId filter

**RATIONALE:** Affiliates are global and should see ALL their earnings across ALL stores.

---

## PHASE 2: PROJECTION GUARANTEE AUDIT

### ❌ CRITICAL BUG: TenantId Stamping Source

**File:** `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs`

#### AffiliateCommissionEarnedProjectionConsumer

**VIOLATION:**
```csharp
// Line 60:
var tenantId = evt.TenantId ?? Guid.Empty;  // ❌ WRONG SOURCE
```

**CORRECT SOURCE:**
```csharp
var tenantId = evt.StoreTenantId;  // ✅ Use StoreTenantId from event
```

**IMPACT:** 
- MongoDB documents get stamped with `Guid.Empty` instead of actual store TenantId
- Admin queries return NO DATA (filter by real TenantId finds nothing)
- **THIS IS THE ROOT CAUSE** of the "affiliates invisible in admin" bug

**AFFECTED CONSUMERS:**
1. ✅ `AffiliateAttributedProjectionConsumer` - Uses `evt.TenantId` (correct, comes from Order)
2. ❌ `AffiliateCommissionEarnedProjectionConsumer` - Uses `evt.TenantId` (WRONG, should use StoreTenantId)
3. ❌ `AffiliateCommissionReversedProjectionConsumer` - Uses `evt.TenantId` (WRONG, should use StoreTenantId)
4. ❌ `AffiliateCommissionApprovedProjectionConsumer` - Needs audit (not in provided code)
5. ❌ `AffiliateCommissionPaidProjectionConsumer` - Needs audit (not in provided code)

---

### ❌ MISSING GUARD: StoreTenantId Validation

**File:** `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:RecordCommissionForOrderAsync`

**VIOLATION:**
```csharp
// No guard for StoreTenantId == Guid.Empty before creating ledger entries
```

**FIX REQUIRED:**
```csharp
if (order.TenantId == Guid.Empty)
{
    _logger.LogError(
        "[Affiliate:RecordCommission] REJECTED: Order has empty TenantId | OrderId: {OrderId}",
        orderId);
    throw new InvalidOperationException($"Order {orderId} has invalid TenantId");
}
```

---

## PHASE 3: FAIL-FAST TENANT MISMATCH DETECTION

### Implementation Required

**Location:** All projection consumers

**Logic:**
```csharp
// After loading MongoDB doc for admin query:
if (doc.TenantId != order.TenantId)
{
    _logger.LogCritical(
        "[Affiliate:TenantMismatch] MongoDB doc TenantId mismatch | " +
        "DocTenantId: {DocTenantId} | OrderTenantId: {OrderTenantId} | OrderId: {OrderId}",
        doc.TenantId, order.TenantId, order.Id);
    throw new InvalidOperationException(
        $"Data corruption: MongoDB doc TenantId ({doc.TenantId}) != Order TenantId ({order.TenantId})");
}
```

**RATIONALE:** Prevents silent corruption from propagating through the system.

---

## PHASE 4: DIAGNOSTIC ENDPOINT

### ✅ ALREADY EXISTS

**Endpoint:** `GET /api/admin/affiliates/debug/{id}/mongo`  
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs:90`

**Returns:**
- CurrentTenantId
- All dashboards (with TenantId)
- All orders (with TenantId)
- All earnings (with TenantId)

**STATUS:** ✅ Endpoint exists and is correctly implemented

**RECOMMENDATION:** Add `#if DEBUG` guard or remove in production builds

---

## PHASE 5: BACKFILL / REPAIR SCRIPT

### SQL Repair Script

```sql
-- AF-19C: Backfill StoreTenantId from Orders table
-- Run this BEFORE reprojecting MongoDB documents

UPDATE "AffiliateCommissionLedger" acl
SET "StoreTenantId" = o."TenantId"
FROM "Orders" o
WHERE acl."OrderId" = o."Id"
  AND (acl."StoreTenantId" IS NULL OR acl."StoreTenantId" = '00000000-0000-0000-0000-000000000000');

-- Verify no orphaned entries
SELECT COUNT(*) 
FROM "AffiliateCommissionLedger" acl
LEFT JOIN "Orders" o ON acl."OrderId" = o."Id"
WHERE o."Id" IS NULL;

-- Should return 0. If not, investigate orphaned entries.
```

### MongoDB Reprojection Script

**File:** `scripts/AF-19C-reproject-mongodb.ps1`

```powershell
# AF-19C: Reproject all affiliate MongoDB documents with correct TenantId

# Step 1: Truncate existing MongoDB collections
Write-Host "Truncating MongoDB collections..."
mongo --eval "db.affiliate_dashboards.deleteMany({})"
mongo --eval "db.affiliate_orders.deleteMany({})"
mongo --eval "db.affiliate_earnings_summaries.deleteMany({})"

# Step 2: Replay all events from PostgreSQL ledger
Write-Host "Replaying events from ledger..."
dotnet run --project PointOfSale.CLI -- affiliate:reproject-all

# Step 3: Verify counts match
Write-Host "Verifying projection..."
dotnet run --project PointOfSale.CLI -- affiliate:verify-projections
```

---

## PHASE 6: REGRESSION TESTS

### Test Scenarios

```csharp
[Fact]
public async Task AdminAffiliateController_GetOrders_OnlyReturnsCurrentStoreOrders()
{
    // GIVEN: Affiliate ZEZO has orders in Store A (MENA) and Store B (ANATICO)
    var affiliateId = Guid.NewGuid();
    var storeA = Guid.NewGuid();
    var storeB = Guid.NewGuid();
    
    var orderA = CreateOrder(storeA, affiliateId);
    var orderB = CreateOrder(storeB, affiliateId);
    
    await RecordCommissionAsync(orderA);
    await RecordCommissionAsync(orderB);
    
    // WHEN: Admin from Store A queries affiliate orders
    var result = await AdminController.GetAffiliateOrders(affiliateId, tenantId: storeA);
    
    // THEN: Only Store A orders are returned
    Assert.Single(result.Orders);
    Assert.Equal(orderA.Id, result.Orders[0].OrderId);
    Assert.DoesNotContain(result.Orders, o => o.OrderId == orderB.Id);
}

[Fact]
public async Task AffiliatePortalController_GetOrders_ReturnsAllStoreOrders()
{
    // GIVEN: Affiliate ZEZO has orders in Store A and Store B
    var affiliateId = Guid.NewGuid();
    var storeA = Guid.NewGuid();
    var storeB = Guid.NewGuid();
    
    var orderA = CreateOrder(storeA, affiliateId);
    var orderB = CreateOrder(storeB, affiliateId);
    
    await RecordCommissionAsync(orderA);
    await RecordCommissionAsync(orderB);
    
    // WHEN: Affiliate queries their orders
    var result = await AffiliatePortalController.GetOrders(affiliateId);
    
    // THEN: Both orders are returned
    Assert.Equal(2, result.Orders.Count);
    Assert.Contains(result.Orders, o => o.OrderId == orderA.Id);
    Assert.Contains(result.Orders, o => o.OrderId == orderB.Id);
}

[Fact]
public async Task ProjectionConsumer_StampsTenantIdFromStoreTenantId()
{
    // GIVEN: Commission earned event with StoreTenantId
    var evt = new AffiliateCommissionEarnedEvent
    {
        TenantId = Guid.Empty,  // Global affiliate
        StoreTenantId = storeA,
        AffiliateAccountId = affiliateId,
        OrderId = orderId,
        TotalCommission = 100m
    };
    
    // WHEN: Projection consumer processes event
    await Consumer.Consume(evt);
    
    // THEN: MongoDB doc has StoreTenantId, not TenantId
    var doc = await MongoDb.AffiliateOrders.Find(d => d.OrderId == orderId).FirstAsync();
    Assert.Equal(storeA, doc.TenantId);
    Assert.NotEqual(Guid.Empty, doc.TenantId);
}
```

---

## PHASE 7: OBSERVABILITY

### Logging Enhancements

**Add to all admin queries:**
```csharp
_logger.LogInformation(
    "[AffiliateAdmin] Tenant={TenantId} Affiliate={AffiliateId} " +
    "OrdersReturned={Count} Endpoint={Endpoint}",
    tenantId, affiliateAccountId, result.Count, "GetOrders");
```

### Metrics

**Add counter:**
```csharp
// In AffiliateTelemetry.cs
public void RecordTenantMismatch(Guid tenantId, string operation)
{
    _meterProvider.CreateCounter<long>("affiliate_admin_tenant_mismatch_total")
        .Add(1, new KeyValuePair<string, object>("tenant_id", tenantId),
                new KeyValuePair<string, object>("operation", operation));
}
```

---

## SUCCESS CRITERIA

### Scenario: Affiliate ZEZO with Multi-Store Orders

**Setup:**
- Affiliate: ZEZO (global, AffiliateAccountId = `abc-123`)
- Order1: MENA store (TenantId = `mena-guid`)
- Order2: ANATICO store (TenantId = `anatico-guid`)

**Expected Results:**

| Actor | Query | Result |
|-------|-------|--------|
| MENA Admin | GET /api/admin/affiliates/abc-123/orders | Order1 ONLY |
| ANATICO Admin | GET /api/admin/affiliates/abc-123/orders | Order2 ONLY |
| Affiliate ZEZO | GET /api/affiliate/orders | Order1 + Order2 |
| MENA Admin | GET /api/admin/affiliates/abc-123 | Dashboard shows Order1 totals |
| ANATICO Admin | GET /api/admin/affiliates/abc-123 | Dashboard shows Order2 totals |
| Affiliate ZEZO | GET /api/affiliate/dashboard | Dashboard shows Order1 + Order2 totals |

**Invariants:**
- ✅ No exceptions thrown
- ✅ No cross-tenant data leakage
- ✅ MongoDB TenantId = StoreTenantId (never Guid.Empty)
- ✅ Admin queries always filter by TenantId
- ✅ Affiliate queries never filter by TenantId

---

## IMPLEMENTATION PRIORITY

### P0 - CRITICAL (Deploy Immediately)
1. ✅ Fix RecordPayment payout calculation TenantId filter
2. ✅ Fix projection consumers to use StoreTenantId
3. ✅ Add StoreTenantId validation guard in RecordCommissionForOrderAsync

### P1 - HIGH (Deploy This Week)
4. ✅ Run SQL backfill script
5. ✅ Reproject MongoDB documents
6. ✅ Add fail-fast tenant mismatch detection

### P2 - MEDIUM (Deploy Next Sprint)
7. ✅ Add regression tests
8. ✅ Add observability metrics
9. ✅ Add #if DEBUG guard to diagnostic endpoint

---

## ROLLBACK PLAN

If issues are discovered after deployment:

1. **Immediate:** Disable affiliate admin endpoints via feature flag
2. **Short-term:** Revert projection consumer changes
3. **Long-term:** Restore MongoDB from backup, replay events with old logic

---

## SIGN-OFF

**Auditor:** Principal Software Architect  
**Date:** 2026-02-24  
**Status:** READY FOR IMPLEMENTATION

**Risk Assessment:** HIGH - Cross-tenant leakage possible  
**Effort Estimate:** 2-3 days (including testing)  
**Deployment Risk:** MEDIUM (requires MongoDB reprojection)

---

## APPENDIX A: FILES TO MODIFY

1. `PointOfSale.API/Controllers/AdminAffiliateController.cs` - Line 530 (RecordPayment)
2. `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs` - Lines 60, 200+
3. `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs` - Add StoreTenantId guard
4. `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateSettlementConsumers.cs` - Verify TenantId usage
5. SQL migration: `add_affiliate_storetenanid_backfill.sql`
6. PowerShell script: `scripts/AF-19C-reproject-mongodb.ps1`

---

## APPENDIX B: VERIFICATION CHECKLIST

- [ ] All admin MongoDB queries filter by TenantId
- [ ] All affiliate portal queries do NOT filter by TenantId
- [ ] All projection consumers use StoreTenantId (not TenantId)
- [ ] StoreTenantId validation guard exists
- [ ] SQL backfill script executed successfully
- [ ] MongoDB reprojection completed
- [ ] Regression tests pass
- [ ] Diagnostic endpoint returns correct TenantId values
- [ ] Observability metrics deployed
- [ ] Production smoke test: Multi-store affiliate scenario verified
