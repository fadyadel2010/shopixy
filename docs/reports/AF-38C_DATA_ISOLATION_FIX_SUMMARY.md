# 🚨 AF-38C: Admin Affiliate Detail Withdrawal Isolation Bug - FIXED

**Status**: ✅ COMPLETE  
**Severity**: HIGH PRIORITY - Data Isolation Breach  
**Date**: 2026-03-01  
**Quality**: Audit-Grade Isolation Restored

---

## 🎯 Executive Summary

Successfully fixed a critical data isolation breach in the Admin Affiliate Detail page where the Withdrawals tab was showing withdrawal requests from ALL affiliates in the store instead of filtering by the specific affiliate being viewed.

### Impact
- **Before**: Viewing affiliate "cocacola" showed withdrawals from affiliate "soso"
- **After**: Each affiliate sees ONLY their own withdrawals
- **Scope**: Admin affiliate detail page (`/admin/affiliates/{id}`)

---

## 🔍 Root Cause

### Technical Issue
The `WithdrawalsTab` component was calling a store-level endpoint that returned ALL withdrawals for the store, without filtering by the specific affiliate ID.

**Broken Code Path**:
```
Frontend: WithdrawalsTab
  ↓
API: adminAffiliateApi.listPayoutRequests(status, page, pageSize)
  ↓
HTTP: GET /admin/affiliates/payouts
  ↓
Backend: ListPayoutRequestsAsync(storeTenantId, status, page, pageSize)
  ↓
Query: WHERE StoreTenantId = @tenantId  ❌ Missing AffiliateAccountId filter
```

### Isolation Violations
1. ❌ Query filtered only by `StoreTenantId`
2. ❌ Missing `AffiliateAccountId` filter
3. ❌ No service-level defensive guard
4. ❌ No audit logging
5. ❌ Inconsistent with Orders tab pattern

---

## ✅ Solution Implemented

### Architecture
Created a new affiliate-scoped endpoint that mirrors the Orders tab pattern:

**Fixed Code Path**:
```
Frontend: WithdrawalsTab(affiliateId)
  ↓
API: adminAffiliateApi.getAffiliateWithdrawals(affiliateId, status, page, pageSize)
  ↓
HTTP: GET /admin/affiliates/{id}/withdrawals
  ↓
Backend: ListPayoutRequestsForAffiliateAsync(storeTenantId, affiliateAccountId, status, page, pageSize)
  ↓
Query: WHERE StoreTenantId = @tenantId AND AffiliateAccountId = @affiliateId  ✅
  ↓
Guard: Assert all results match affiliateId  ✅
  ↓
Log: [DATA-ISOLATION] audit trail  ✅
```

---

## 📝 Changes Made

### 1. Backend - Service Interface
**File**: `PointOfSale.Core/Interfaces/Affiliate/IAffiliatePayoutService.cs`

Added new method:
```csharp
/// <summary>
/// AF-38C: Admin: List payout requests for a SPECIFIC affiliate.
/// DATA ISOLATION: Filters by BOTH storeTenantId AND affiliateAccountId.
/// </summary>
Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsForAffiliateAsync(
    Guid storeTenantId,
    Guid affiliateAccountId,
    PayoutRequestStatus? status = null,
    int page = 1,
    int pageSize = 20,
    CancellationToken ct = default);
```

### 2. Backend - Service Implementation
**File**: `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs`

Implemented with dual filtering + defensive guard:
```csharp
public async Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsForAffiliateAsync(
    Guid storeTenantId,
    Guid affiliateAccountId,
    PayoutRequestStatus? status = null,
    int page = 1,
    int pageSize = 20,
    CancellationToken ct = default)
{
    // AF-38C: CRITICAL - Filter by BOTH StoreTenantId AND AffiliateAccountId
    var query = _db.AffiliatePayoutRequests
        .IgnoreQueryFilters()
        .Where(r => 
            r.StoreTenantId == storeTenantId && 
            r.AffiliateAccountId == affiliateAccountId);

    if (status.HasValue)
        query = query.Where(r => r.Status == status.Value);

    var total = await query.CountAsync(ct);
    var items = await query
        .OrderByDescending(r => r.RequestedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);
    
    // AF-38C: Defensive guard - verify all results match affiliate
    if (items.Any(r => r.AffiliateAccountId != affiliateAccountId))
    {
        _logger.LogError(
            "[DATA-ISOLATION-BREACH] Cross-affiliate data detected | " +
            "Expected: {ExpectedAffiliateId} | Query returned mixed affiliates",
            affiliateAccountId);
        throw new SecurityException("Cross-affiliate data access detected");
    }

    return (items, total);
}
```

### 3. Backend - Controller Endpoint
**File**: `PointOfSale.API/Controllers/AdminAffiliateController.cs`

Added new endpoint:
```csharp
/// <summary>
/// AF-38C: GET /api/admin/affiliates/{id}/withdrawals?status=Pending&page=1&pageSize=20
/// Lists withdrawal requests for a SPECIFIC affiliate.
/// DATA ISOLATION: Filters by BOTH StoreTenantId AND AffiliateAccountId.
/// </summary>
[HttpGet("{id:guid}/withdrawals")]
public async Task<ActionResult> GetAffiliateWithdrawals(
    Guid id,
    [FromQuery] string? status = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken ct = default)
{
    var tenantId = GetTenantId();
    
    // Verify affiliate exists
    var exists = await _dbContext.AffiliateAccounts
        .IgnoreQueryFilters()
        .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);
    
    if (!exists)
        return NotFound(new { message = "Affiliate not found" });
    
    PayoutRequestStatus? statusFilter = null;
    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayoutRequestStatus>(status, true, out var parsed))
        statusFilter = parsed;
    
    // AF-38C: Call new service method with BOTH filters
    var (items, totalCount) = await _payoutService.ListPayoutRequestsForAffiliateAsync(
        tenantId, id, statusFilter, page, pageSize, ct);
    
    // Load affiliate name
    var affiliateName = await _dbContext.AffiliateAccounts
        .IgnoreQueryFilters()
        .Where(a => a.Id == id)
        .Select(a => a.DisplayName)
        .FirstOrDefaultAsync(ct) ?? "Unknown";
    
    _logger.LogInformation(
        "[DATA-ISOLATION] AdminAffiliateDetail withdrawals loaded | " +
        "TenantId: {TenantId} | AffiliateId: {AffiliateId} | Count: {Count}",
        tenantId, id, items.Count);
    
    return Ok(new
    {
        items = items.Select(r => new
        {
            r.Id,
            r.AffiliateAccountId,
            affiliateName,
            r.RequestedAmount,
            r.RemainingAmount,
            r.ApprovedAmount,
            status = r.Status.ToString(),
            r.RequestedAtUtc,
            r.ApprovedAtUtc,
            r.PaidAtUtc,
            r.PaymentReference,
            r.AdminNotes,
            r.Currency
        }),
        page,
        pageSize,
        totalCount,
        totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    });
}
```

### 4. Frontend - API Service
**File**: `PointOfSale.Web/src/services/adminAffiliateApi.js`

Added new method:
```javascript
/** 
 * AF-38C: Get withdrawal requests for a SPECIFIC affiliate 
 * DATA ISOLATION: Scoped to single affiliate
 */
getAffiliateWithdrawals: (affiliateId, status = '', page = 1, pageSize = 20) =>
    api.get(`/admin/affiliates/${affiliateId}/withdrawals`, {
        params: { ...(status && { status }), page, pageSize }
    }).then(r => r.data),
```

### 5. Frontend - Component Fix
**File**: `PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliateDetail.jsx`

Updated `WithdrawalsTab` component:
```javascript
// Line 650: Added affiliateId prop
function WithdrawalsTab({ affiliateId, formatCurrency, onToast }) {
    // ...
    
    // Line 665: Fixed API call
    const fetchData = useCallback(() => {
        setLoading(true)
        // AF-38C: FIX - Use affiliate-scoped endpoint
        adminAffiliateApi.getAffiliateWithdrawals(affiliateId, statusFilter, page, 20)
            .then(d => setData(d))
            .catch(() => setData(null))
            .finally(() => setLoading(false))
    }, [affiliateId, statusFilter, page])  // Added affiliateId dependency
    
    // ...
}

// Line 1147: Pass affiliateId prop
{activeTab === 'withdrawals' && <WithdrawalsTab affiliateId={id} formatCurrency={formatCurrency} onToast={showToast} />}
```

---

## 🔒 Security Hardening

### 1. Dual Filtering
```csharp
WHERE r.StoreTenantId == storeTenantId && r.AffiliateAccountId == affiliateAccountId
```
Both conditions are mandatory - no single-filter bypass possible.

### 2. Defensive Guard
```csharp
if (items.Any(r => r.AffiliateAccountId != affiliateAccountId))
{
    throw new SecurityException("Cross-affiliate data access detected");
}
```
Fails fast if isolation is somehow breached.

### 3. Audit Logging
```csharp
_logger.LogInformation(
    "[DATA-ISOLATION] AdminAffiliateDetail withdrawals loaded | " +
    "TenantId: {TenantId} | AffiliateId: {AffiliateId} | Count: {Count}",
    tenantId, id, items.Count);
```
Every withdrawal load is logged for audit trail.

---

## 📊 Consistency with Orders Tab

### Before Fix
| Tab | Endpoint | Filters | Status |
|-----|----------|---------|--------|
| Orders | `/admin/affiliates/{id}/orders` | `TenantId + AffiliateId` | ✅ Correct |
| Withdrawals | `/admin/affiliates/payouts` | `TenantId` only | ❌ Broken |

### After Fix
| Tab | Endpoint | Filters | Status |
|-----|----------|---------|--------|
| Orders | `/admin/affiliates/{id}/orders` | `TenantId + AffiliateId` | ✅ Correct |
| Withdrawals | `/admin/affiliates/{id}/withdrawals` | `TenantId + AffiliateId` | ✅ Fixed |

**Pattern Consistency**: Both tabs now follow identical isolation logic.

---

## 🧪 Testing

### Manual Test
1. Create two affiliates: "cocacola" and "soso"
2. Create withdrawal request for "soso" (100 EGP)
3. Create withdrawal request for "cocacola" (200 EGP)
4. Navigate to `/admin/affiliates/{cocacola-id}`
5. Click Withdrawals tab
6. **Expected**: See ONLY cocacola's 200 EGP withdrawal
7. **Actual**: ✅ Confirmed - only cocacola's withdrawal shown

### SQL Verification
```sql
-- Verify no cross-affiliate leakage
SELECT 
    r."AffiliateAccountId",
    a."DisplayName",
    r."RequestedAmount",
    r."Status"
FROM "AffiliatePayoutRequests" r
JOIN "AffiliateAccounts" a ON r."AffiliateAccountId" = a."Id"
WHERE r."StoreTenantId" = @tenantId
  AND r."AffiliateAccountId" = @affiliateId;

-- Should return ONLY rows for the specified affiliate
```

### Integration Test (Recommended)
```csharp
[Fact]
public async Task GetAffiliateWithdrawals_OnlyReturnsAffiliateOwnWithdrawals()
{
    // Arrange
    var affiliateA = CreateAffiliate("A");
    var affiliateB = CreateAffiliate("B");
    var withdrawalA = CreateWithdrawal(affiliateA.Id, 100);
    var withdrawalB = CreateWithdrawal(affiliateB.Id, 200);
    
    // Act
    var result = await _controller.GetAffiliateWithdrawals(affiliateA.Id);
    
    // Assert
    var data = Assert.IsType<OkObjectResult>(result).Value;
    var items = GetItems(data);
    Assert.Single(items);
    Assert.All(items, item => Assert.Equal(affiliateA.Id, item.AffiliateAccountId));
}
```

---

## ✅ Verification Checklist

- [x] Root cause identified and documented
- [x] New backend endpoint created (`/admin/affiliates/{id}/withdrawals`)
- [x] New service method with dual filtering
- [x] Defensive guard added (throws on cross-affiliate data)
- [x] Audit logging added (`[DATA-ISOLATION]` prefix)
- [x] Frontend API service updated
- [x] Frontend component fixed
- [x] Follows same pattern as Orders tab
- [x] All code compiles without errors
- [x] Manual testing completed
- [x] Documentation created

---

## 🚀 Deployment Status

**Backend**: ✅ Ready  
**Frontend**: ✅ Ready  
**Database**: ✅ No migration needed  
**Breaking Changes**: ❌ None (additive only)

### Deployment Steps
1. Deploy backend (controller + service)
2. Deploy frontend (API service + component)
3. Verify in staging
4. Monitor logs for `[DATA-ISOLATION]` entries
5. Deploy to production

---

## 📈 Impact

### Security
- **Before**: Cross-affiliate data leakage
- **After**: Audit-grade isolation

### Performance
- **Impact**: Minimal (added one WHERE clause)
- **Query**: Indexed on `(StoreTenantId, AffiliateAccountId, Status)`

### User Experience
- **Before**: Confusing (seeing other affiliates' withdrawals)
- **After**: Correct (only own withdrawals)

---

## 📚 Related Files

### Modified
- `PointOfSale.Core/Interfaces/Affiliate/IAffiliatePayoutService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs`
- `PointOfSale.API/Controllers/AdminAffiliateController.cs`
- `PointOfSale.Web/src/services/adminAffiliateApi.js`
- `PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliateDetail.jsx`

### Documentation
- `AF-38C_DATA_ISOLATION_FIX.md` (detailed analysis)
- `AF-38C_DATA_ISOLATION_FIX_SUMMARY.md` (this file)

---

## 🎯 Lessons Learned

1. **Always filter by entity ID**: Store-level endpoints are dangerous for detail pages
2. **Follow existing patterns**: Orders tab had correct isolation - should have mirrored it
3. **Add defensive guards**: Service-level validation catches bugs early
4. **Audit logging**: `[DATA-ISOLATION]` prefix makes security audits easy
5. **Test cross-entity scenarios**: Always test with multiple entities

---

**Fix Status**: ✅ COMPLETE  
**Quality Level**: Audit-Grade Data Isolation  
**Next Steps**: Deploy to staging → Verify → Deploy to production

---

**End of Summary**
