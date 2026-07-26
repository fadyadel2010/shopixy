# AF-19B: Admin Panel Tenant Scoping Implementation

**Date**: 2026-02-24  
**Status**: In Progress - Diagnostic Phase  
**Objective**: Fix admin panel to show only current store's affiliate data, not aggregated data from all stores

---

## Problem Statement

After implementing the global affiliate model (AF-19B), the admin panel was showing orders from ALL stores when viewing an affiliate's detail page. For example:
- Admin logged into "anatico" store was seeing orders from both "mena" and "anatico" stores
- This violated the requirement that each store admin should only see their own store's data

### Example Issue
Order `ORD-8A9F-2026-00083` was made at `mena.localhost:5174/products/mena?ref=HANY450693`  
Other orders were made at `anatico.localhost:5174/products/ankeer-7000?ref=HANY450693`  
→ Both were showing in both store admin panels (incorrect behavior)

---

## Architecture Overview

### Global Affiliate Model
- **Affiliates are global entities**: No tenant binding in `AffiliateAccounts` or `AffiliateUsers`
- **Orders belong to stores**: Each order has a `TenantId` representing where the sale occurred
- **MongoDB documents track store**: `TenantId` field in MongoDB documents represents the store where the sale happened

### Admin Panel Requirements
- **List all affiliates globally**: Show all affiliates across all stores
- **Metrics are tenant-scoped**: Orders, earnings, and settlement operations filtered by current store
- **Each store manages independently**: Store A can approve/pay commissions for their orders, Store B does the same for theirs

---

## Implementation Changes

### 1. AdminAffiliateController - GetAffiliateDetail
**Before**: Aggregated metrics across ALL stores
```csharp
var dashboards = await _mongoDbContext.AffiliateDashboards.Find(
    Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
).ToListAsync(ct);

var aggregated = new {
    TotalEarned = dashboards.Sum(d => d.TotalEarned),
    // ... aggregated across all stores
};
```

**After**: Shows metrics for CURRENT STORE only
```csharp
var tenantId = GetTenantId();

var dashboard = await _mongoDbContext.AffiliateDashboards.Find(
    Builders<AffiliateDashboardDocument>.Filter.And(
        Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId)
    )
).FirstOrDefaultAsync(ct);

// Returns zeros if no dashboard exists for this store
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~172-250)

---

### 2. AdminAffiliateController - GetAffiliateOrders
**Before**: Showed orders from ALL stores
```csharp
var filters = new List<FilterDefinition<AffiliateOrderDocument>>
{
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
};
```

**After**: Filters by current tenant
```csharp
var tenantId = GetTenantId();

var filters = new List<FilterDefinition<AffiliateOrderDocument>>
{
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId)
};
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~252-330)

---

### 3. AdminAffiliateController - GetAffiliateEarnings
**Before**: Aggregated earnings across ALL stores by currency
```csharp
var docs = await _mongoDbContext.AffiliateEarningsSummaries.Find(
    Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
).ToListAsync(ct);

var aggregated = docs
    .GroupBy(d => d.Currency)
    .Select(g => new AffiliateEarningsSummaryDto {
        TotalEarned = g.Sum(d => d.TotalEarned),
        // ... aggregated
    });
```

**After**: Shows earnings for CURRENT STORE only
```csharp
var tenantId = GetTenantId();

var docs = await _mongoDbContext.AffiliateEarningsSummaries.Find(
    Builders<AffiliateEarningsSummaryDocument>.Filter.And(
        Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId)
    )
).ToListAsync(ct);
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~332-375)

---

### 4. AdminAffiliateController - ApproveCommissions
**Before**: Approved orders from ALL stores when using "Approve All"
```csharp
var earnedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Earned")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**After**: Only approves CURRENT STORE's orders
```csharp
var tenantId = GetTenantId();

var earnedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Earned")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~376-457)

---

### 5. AdminAffiliateController - RecordPayment
**Before**: Paid orders from ALL stores when using "Pay All"
```csharp
var approvedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Approved")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**After**: Only pays CURRENT STORE's orders
```csharp
var tenantId = GetTenantId();

var approvedDocs = await _mongoDbContext.AffiliateOrders.Find(
    Builders<AffiliateOrderDocument>.Filter.And(
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, "Approved")
    ))
    .Project(d => d.OrderId)
    .ToListAsync(ct);
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~458-575)

---

### 6. ListAffiliates (Already Correct)
The `ListAffiliates` endpoint was already correctly implemented:
- Shows ALL affiliates globally (no tenant filter on affiliate accounts)
- But shows metrics for CURRENT STORE only (tenant-scoped dashboard data)

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~67-170)

---

## Current Issue: "No Orders" in Admin Panel

### Symptoms
- Admin panel at `localhost:5173/admin/affiliates` shows affiliate "hany" (HANY450693)
- All metrics show `EGP0.00` and `0` orders
- Orders tab shows "No Orders - This affiliate has no commission orders yet"

### Possible Causes

#### 1. MongoDB TenantId Mismatch
The MongoDB documents (`AffiliateOrderDocument`, `AffiliateDashboardDocument`) have `TenantId` values from when orders were created. If the current admin's tenant ID doesn't match the MongoDB document's `TenantId`, the query returns no results.

**Example**:
- Order was created with `TenantId = "019c8f2a-xxxx-xxxx-xxxx-xxxxxxxxxxxx"` (mena store)
- Admin is logged in with `TenantId = "019c8f2b-yyyy-yyyy-yyyy-yyyyyyyyyyyy"` (anatico store)
- Query filters by current tenant → No match → No orders shown

#### 2. MongoDB Documents Not Created
If the affiliate commission events were published but the MongoDB consumers didn't process them, the read models might not exist.

#### 3. Wrong Tenant Context
The admin might be logged into a different store than where the orders were placed.

---

## Diagnostic Tools Created

### 1. Diagnostic API Endpoint
**Endpoint**: `GET /api/admin/affiliates/debug/{id}/mongo`

Returns raw MongoDB data for an affiliate:
```json
{
  "currentTenantId": "019c8f2a-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "dashboards": [
    {
      "tenantId": "019c8f2a-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "totalOrders": 3,
      "totalEarned": 150.00
    }
  ],
  "orders": [
    {
      "orderId": "...",
      "orderNumber": "ORD-8A9F-2026-00083",
      "tenantId": "019c8f2a-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "status": "Earned",
      "totalCommission": 50.00
    }
  ],
  "earnings": [...]
}
```

**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs` (Lines ~58-115)

### 2. PowerShell Script (Requires mongosh)
**File**: `check-mongo-tenantids.ps1`

Queries MongoDB directly to show TenantId values for affiliate documents.

**Note**: Requires `mongosh` to be installed. User's system doesn't have it.

### 3. SQL Diagnostic Queries
**File**: `debug-affiliate-data.sql`

Checks PostgreSQL data:
- Affiliate account details
- Orders with affiliate attribution
- Commission ledger entries with `StoreTenantId`

---

## Next Steps

### Immediate Actions
1. **Run diagnostic endpoint**: Navigate to `http://localhost:5173/api/admin/affiliates/debug/019c912f-012f-7795-90ce-912701633a66/mongo`
   - This will show what TenantId values exist in MongoDB
   - Compare with current admin's tenant ID

2. **Check tenant context**: Verify which store the admin is logged into
   - Browser console: `localStorage.getItem('tenantId')`
   - Browser console: `localStorage.getItem('storeSlug')`

3. **Verify MongoDB data exists**: Check if MongoDB documents were created at all
   - If no documents exist, the commission events might not have been processed

### Resolution Paths

#### Path A: TenantId Mismatch
If MongoDB has data but with different TenantId:
- **Root Cause**: Orders were placed at Store A, admin is viewing from Store B
- **Expected Behavior**: This is CORRECT - Store B admin shouldn't see Store A's orders
- **Action**: Login to the correct store's admin panel

#### Path B: MongoDB Documents Missing
If MongoDB has no documents:
- **Root Cause**: Commission events not processed or consumers failed
- **Action**: 
  1. Check MassTransit/RabbitMQ for failed messages
  2. Manually trigger event replay if needed
  3. Check consumer logs for errors

#### Path C: Wrong Store Context
If admin is in wrong store:
- **Action**: Navigate to correct store's admin panel
  - `mena.localhost:5173/admin/affiliates` for mena store
  - `anatico.localhost:5173/admin/affiliates` for anatico store

---

## Files Modified

### Backend
1. `PointOfSale.API/Controllers/AdminAffiliateController.cs`
   - Added tenant filtering to all admin endpoints
   - Added diagnostic endpoint
   - Lines modified: ~172-575

### Diagnostic Files Created
1. `check-mongo-tenantids.ps1` - PowerShell script to query MongoDB
2. `debug-affiliate-data.sql` - SQL queries for PostgreSQL data
3. `check-tenant-ids.sql` - Query to get tenant IDs for stores
4. `CheckMongoData.cs` - C# script to query MongoDB (requires dotnet-script)

---

## Testing Checklist

- [ ] Run diagnostic endpoint to see MongoDB data
- [ ] Verify current tenant ID matches MongoDB TenantId
- [ ] Test from "mena" store admin panel
- [ ] Test from "anatico" store admin panel
- [ ] Verify orders show only for correct store
- [ ] Test "Approve All" only approves current store's orders
- [ ] Test "Pay All" only pays current store's orders
- [ ] Verify cashbox balance check uses current store's cashbox

---

## MongoDB Document Structure

### AffiliateDashboardDocument
```csharp
{
    AffiliateAccountId: Guid,
    TenantId: Guid,              // Store where sales occurred
    TotalOrders: int,
    TotalEarned: decimal,
    TotalApproved: decimal,
    TotalPaid: decimal,
    // ...
}
```

### AffiliateOrderDocument
```csharp
{
    OrderId: Guid,
    OrderNumber: string,
    AffiliateAccountId: Guid,
    TenantId: Guid,              // Store where order was placed
    Status: string,              // Earned, Approved, Paid, etc.
    TotalCommission: decimal,
    // ...
}
```

### AffiliateEarningsSummaryDocument
```csharp
{
    AffiliateAccountId: Guid,
    TenantId: Guid,              // Store where earnings occurred
    Currency: string,
    TotalEarned: decimal,
    TotalApproved: decimal,
    TotalPaid: decimal,
    // ...
}
```

**Key Point**: The `TenantId` in MongoDB documents represents the store where the transaction occurred. This is the source of truth for tenant-scoped queries.

---

## Related Work

### AF-19B Migration (Not Yet Run)
**File**: `PointOfSale.Infrastructure/Data/Migrations/af19b_remove_affiliate_tenant_binding.sql`

This migration:
- Sets `AffiliateAccount.TenantId` to NULL (affiliates are global)
- Sets `AffiliateCommissionLedger.TenantId` to NULL (use `StoreTenantId` instead)
- Updates idempotency keys to remove tenant binding

**Status**: Created but NOT executed yet

**Note**: This migration affects PostgreSQL only, not MongoDB. MongoDB documents keep their `TenantId` values as they represent where sales occurred.

---

## Summary

The admin panel now correctly filters all affiliate data by the current store's tenant ID. Each store admin sees only their own orders, earnings, and can only approve/pay commissions for their own store's orders. The affiliate list shows all affiliates globally, but the financial metrics are tenant-scoped.

The current "No Orders" issue is likely due to:
1. Admin viewing from wrong store (orders exist in different store)
2. MongoDB documents not created yet (events not processed)
3. TenantId mismatch between current admin and MongoDB data

Next step is to run the diagnostic endpoint to determine the exact cause.
