# AF-18: Cross-Tenant Affiliate Commission Fix

## Problem Statement

ZEZO is registered as an affiliate in the **Mena tenant** but was trying to sell products from the **Anatico tenant**. The cross-tenant order attribution was working correctly (orders were created with ZEZO's affiliate ID), but **commissions were not being recorded**.

### Root Cause

The `RecordCommissionForOrderAsync` method in `AffiliateService.cs` had two critical bugs:

1. **Affiliate Lookup Bug** (Line 295):
   ```csharp
   var affiliate = await _context.AffiliateAccounts
       .IgnoreQueryFilters()
       .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == attribution.AffiliateAccountId && !a.IsDeleted, ct);
   ```
   - `tenantId` came from the **Order's tenant** (Anatico)
   - ZEZO's account has `TenantId = Mena`
   - Query returned NULL → commission recording skipped

2. **Commission Ledger Tenant Bug** (Line 358):
   ```csharp
   var entry = AffiliateCommissionLedger.CreateEarned(
       tenantId, // ← Order's tenant (Anatico)
       attribution.Id, affiliate.Id,
       orderId, item.Id,
       commissionableAmount, policy.CommissionRate, policy.MaxCommissionPerItem,
       order.Currency);
   ```
   - Commission ledger was created in the **Order's tenant** (Anatico)
   - But ZEZO's dashboard queries the **Mena tenant**
   - Result: Commission invisible to affiliate

## Solution

### Architecture Decision: Commission Ledger in Affiliate's Tenant

We chose to store commission ledger entries in the **affiliate's home tenant** (not the order's tenant) because:

1. **Affiliate Dashboard Simplicity**: Affiliates see ALL their commissions in one place (their home tenant)
2. **Accounting Clarity**: Commission is revenue TO the affiliate, so it belongs in their books
3. **Cross-Tenant Sales**: Affiliates can sell products from ANY tenant and see all earnings in one dashboard

### Code Changes

**File**: `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

#### Change 1: Remove TenantId Filter from Affiliate Lookup (Line 295)

```csharp
// BEFORE (BROKEN):
var affiliate = await _context.AffiliateAccounts
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == attribution.AffiliateAccountId && !a.IsDeleted, ct);

// AFTER (FIXED):
// AF-18: CROSS-TENANT FIX - Don't filter by tenantId, affiliate may be from different tenant
var affiliate = await _context.AffiliateAccounts
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(a => a.Id == attribution.AffiliateAccountId && !a.IsDeleted, ct);

if (affiliate == null || !affiliate.IsActive)
{
    _logger.LogWarning(
        "Affiliate {AffiliateId} is not active — skipping commission for Order {OrderId}",
        attribution.AffiliateAccountId, orderId);
    _telemetry.RecordSkipped(tenantId, "inactive_affiliate");
    return;
}

// AF-18: Log cross-tenant commission recording
if (affiliate.TenantId != tenantId)
{
    _logger.LogInformation(
        "[AF-18:CrossTenant:Commission] Recording commission for cross-tenant sale | " +
        "OrderId: {OrderId} | OrderTenant: {OrderTenant} | AffiliateTenant: {AffiliateTenant} | " +
        "AffiliateId: {AffiliateId} | ReferralCode: {ReferralCode}",
        orderId, tenantId, affiliate.TenantId, affiliate.Id, affiliate.ReferralCode);
}
```

#### Change 2: Use Affiliate's TenantId for Commission Ledger (Line 358)

```csharp
// BEFORE (BROKEN):
var entry = AffiliateCommissionLedger.CreateEarned(
    tenantId, // ← Order's tenant
    attribution.Id, affiliate.Id,
    orderId, item.Id,
    commissionableAmount, policy.CommissionRate, policy.MaxCommissionPerItem,
    order.Currency);

// AFTER (FIXED):
// AF-18: CRITICAL - Use affiliate's TenantId, not order's TenantId
// Commission ledger belongs to the affiliate's home tenant for dashboard queries
var entry = AffiliateCommissionLedger.CreateEarned(
    affiliate.TenantId, // ← Affiliate's tenant
    attribution.Id, affiliate.Id,
    orderId, item.Id,
    commissionableAmount, policy.CommissionRate, policy.MaxCommissionPerItem,
    order.Currency);
```

#### Change 3: Emit Event in Affiliate's Tenant (Line 410)

```csharp
// BEFORE (BROKEN):
await _eventPublisher.PublishAsync(new AffiliateCommissionEarnedEvent
{
    TenantId = tenantId, // ← Order's tenant
    OrderId = orderId,
    // ...
}, ct);

// AFTER (FIXED):
// AF-18: Emit event in affiliate's tenant (where commission ledger is stored)
await _eventPublisher.PublishAsync(new AffiliateCommissionEarnedEvent
{
    TenantId = affiliate.TenantId, // ← Affiliate's tenant
    OrderId = orderId,
    OrderNumber = order.OrderNumber,
    AffiliateAccountId = affiliate.Id,
    ReferralCode = affiliate.ReferralCode,
    TotalCommission = earnedEntries.Sum(e => e.CommissionAmount),
    Currency = order.Currency,
    ItemCount = earnedEntries.Count,
    Items = earnedEntries.Select(e =>
    {
        var item = order.Items.First(i => i.Id == e.OrderItemId);
        return new CommissionLineItem
        {
            OrderItemId = e.OrderItemId,
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            PaidAmount = e.BaseAmount,
            CommissionRate = e.CommissionRate,
            CommissionAmount = e.CommissionAmount
        };
    }).ToList(),
    CorrelationId = orderId.ToString()
}, ct);

_logger.LogInformation(
    "[AF-18] Recorded commission for Order {OrderId} in affiliate's tenant | " +
    "AffiliateTenant: {AffiliateTenant} | OrderTenant: {OrderTenant} | " +
    "Items: {Count}, Total: {Total} {Currency} " +
    "(skippedUnpaid={SkippedUnpaid}, skippedNoPolicy={SkippedNoPolicy}, idempotent={IdempotentSkips})",
    orderId, affiliate.TenantId, tenantId, earnedEntries.Count,
    earnedEntries.Sum(e => e.CommissionAmount), order.Currency,
    skippedUnpaid, skippedNoPolicy, idempotentSkips);
```

## Data Flow After Fix

### Cross-Tenant Sale Example: ZEZO (Mena) sells Anatico Product

1. **Customer visits**: `anatico.localhost:5174/products/ankeStorefronter-7000?ref=ZEZO906409`
2. **Frontend captures**: `?ref=ZEZO906409` → sessionStorage → `X-Affiliate-Ref` header
3. **Middleware activates**: Affiliate Mode with ZEZO's ID (from Mena tenant)
4. **Order created**:
   - `Order.TenantId` = Anatico (fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0)
   - `Order.AffiliateAccountId` = ZEZO's ID
   - `Order.AffiliateReferralCode` = "ZEZO906409"
5. **Order marked Delivered/Completed**:
   - `OrderCompletedEvent` published
   - `OrderCompletedAffiliateConsumer` triggers `RecordCommissionForOrderAsync()`
6. **Commission recorded**:
   - `AffiliateCommissionLedger.TenantId` = **Mena** (ZEZO's home tenant)
   - `AffiliateCommissionLedger.OrderId` = Anatico order ID
   - `AffiliateCommissionLedger.AffiliateAccountId` = ZEZO's ID
7. **Accounting journal created**:
   - `JournalEntry.TenantId` = **Mena** (affiliate's books)
   - Debit: Affiliate Payable (liability)
   - Credit: Commission Expense (expense)
8. **ZEZO's dashboard** (Mena admin):
   - Shows commission from Anatico sale
   - All commissions from ALL tenants visible in one place

## Where ZEZO Appears

- **Mena Admin** (`http://mena.localhost:5173/admin/affiliates`): ZEZO's account, dashboard, commissions, payouts
- **Anatico Admin** (`http://anatico.localhost:5173/admin/orders`): Orders show ZEZO's referral code in order details
- **NOT in Anatico Affiliates List**: ZEZO is not an Anatico affiliate, they're a Mena affiliate selling Anatico products

## Verification

Run the SQL script `af18-cross-tenant-commission-verification.sql` to verify:

1. ZEZO's account exists in Mena tenant
2. Cross-tenant orders exist (Anatico orders with ZEZO attribution)
3. Commission ledger entries are in **Mena tenant** (affiliate's home)
4. Accounting journal entries are in **Mena tenant**
5. No missing commissions for Delivered/Completed orders

## Testing

### Manual Test

1. Create order in Anatico store with `?ref=ZEZO906409`
2. Mark order as Delivered or Completed
3. Check logs for `[AF-18:CrossTenant:Commission]` message
4. Verify commission appears in Mena admin dashboard
5. Verify accounting journal created in Mena tenant

### Expected Logs

```
[AF-18:CrossTenant] Affiliate from different tenant | AffiliateId: {ZEZO_ID} | 
AffiliateTenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73 | OrderTenant: fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0 | 
Ref: ZEZO906409

[AF-18:CrossTenant:Commission] Recording commission for cross-tenant sale | 
OrderId: {ORDER_ID} | OrderTenant: fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0 | 
AffiliateTenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73 | AffiliateId: {ZEZO_ID} | 
ReferralCode: ZEZO906409

[AF-18] Recorded commission for Order {ORDER_ID} in affiliate's tenant | 
AffiliateTenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73 | 
OrderTenant: fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0 | Items: 1, Total: 44.0000 EGP
```

## Impact

- **Affiliates**: Can now sell products from ANY tenant and see all commissions in their home dashboard
- **Stores**: Can leverage affiliates from other tenants without managing separate affiliate programs
- **Accounting**: Clear separation - commission liability is in affiliate's tenant, not store's tenant
- **Scalability**: Supports marketplace scenarios where affiliates promote products across multiple stores

## Files Changed

1. `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs` (3 changes)
2. `af18-cross-tenant-commission-verification.sql` (new verification script)
3. `AF-18_CROSS_TENANT_COMMISSION_FIX.md` (this document)

## Status

✅ **COMPLETE** - Cross-tenant affiliate commission recording now works correctly.
