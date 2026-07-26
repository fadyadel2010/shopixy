# AF-38E: Unified Monetary Flow - Quick Reference

**Status**: ✅ Complete | **Date**: 2026-03-01 | **Priority**: CRITICAL

---

## The Problem

**Before**: Two independent monetary paths
- Legacy Settlement → `CommissionLedger.Paid` only (no monetary ledger)
- Withdrawal Engine → `BalanceLedger.Withdrawal` (monetary ledger)

**Result**: Inconsistent financial state, no single source of truth

---

## The Solution

**After**: Single unified monetary path
- ALL Payouts → `BalanceLedger.Withdrawal` → Single source of truth

---

## Key Principle

```
CommissionLedger.Paid = Lifecycle state (NOT monetary deduction)
BalanceLedger.Withdrawal = Actual money leaving wallet

Available Balance = ONLY derived from BalanceLedger
```

---

## What Changed

### 1. Legacy Settlement Flow

**File**: `AffiliateService.cs` → `RecordCommissionPaidAsync()`

**Before:**
```csharp
// Create CommissionLedger.Paid only
var paid = AffiliateCommissionLedger.CreatePaid(approved);
_context.Add(paid);
```

**After:**
```csharp
// STEP 1: Create BalanceLedger.Withdrawal (authoritative)
var withdrawal = AffiliateBalanceLedger.CreateWithdrawal(...);
_context.Add(withdrawal);

// STEP 2: Mark CommissionLedger.Paid (lifecycle only)
var paid = AffiliateCommissionLedger.CreatePaid(approved);
_context.Add(paid);
```

---

### 2. Defensive Guard

**File**: `AffiliatePayoutService.cs` → `GetAvailableBalanceAsync()`

**Added:**
```csharp
// Detect Paid entries without BalanceLedger entries
if (hasPaidCommissions && withdrawalCount == 0)
{
    _logger.LogWarning("[AF-38E] INCONSISTENT STATE DETECTED");
}
```

---

### 3. Migration Script

**File**: `migrations/AF38E_UnifiedMonetaryFlow.sql`

**Purpose**: Backfill `BalanceLedger.Withdrawal` for historical `CommissionLedger.Paid`

**What It Does:**
1. Find `Paid` entries without `BalanceLedger`
2. Create `BalanceLedger.Withdrawal` for each order
3. Verify no orphaned entries remain

---

## Balance Formula (Final)

```
Available = 
    SUM(CommissionLedger.Approved)
  + SUM(BalanceLedger.Amount)
  - SUM(PayoutRequests.Pending)
```

**Note**: No `CommissionLedger.Paid` in formula

---

## Test Cases

| Scenario | Expected |
|----------|----------|
| Legacy Settlement | ✅ Creates BalanceLedger + CommissionLedger.Paid |
| Withdrawal Engine | ✅ Creates BalanceLedger only |
| Migration | ✅ Backfills historical data |
| Defensive Guard | ✅ Logs warning for inconsistent state |

---

## Deployment

### Steps
1. Deploy code
2. Run migration: `psql -f migrations/AF38E_UnifiedMonetaryFlow.sql`
3. Verify: Check for orphaned Paid entries (should be 0)
4. Monitor: Watch for `[AF-38E]` log entries

### Rollback
```bash
psql -f migrations/AF38E_Rollback.sql
```

---

## Verification

### SQL Check
```sql
-- Should return 0 rows
SELECT COUNT(DISTINCT cl."OrderId")
FROM "AffiliateCommissionLedger" cl
WHERE cl."EntryType" = 3
  AND NOT EXISTS (
      SELECT 1 FROM "AffiliateBalanceLedger" bl
      WHERE bl."ReferenceId" = cl."OrderId"
        AND bl."EntryType" = 0
  );
```

---

## Monitoring

### Log Messages

**Success:**
```
[AF-38E:Paid] BalanceLedger withdrawal created | Order {guid}
```

**Warning:**
```
[AF-38E] INCONSISTENT STATE DETECTED | AffiliateId={guid}
```

**Migration:**
```
AF-38E: ✓ Unified Monetary Flow Migration Complete
```

---

## Impact

- ✅ **Single source of truth**: All monetary movements in BalanceLedger
- ✅ **Consistent state**: No ambiguous settlement paths
- ✅ **Audit-grade**: Complete monetary trail
- ❌ **Breaking changes**: None

---

## Related Docs

- `AF-38E_UNIFIED_MONETARY_FLOW.md` - Full technical details
- `AF-38E_IMPLEMENTATION_SUMMARY.md` - Implementation summary
- `AF-38C_DUAL_LEDGER_ARCHITECTURE.md` - Architecture reference

---

**Quick Answer**: Legacy settlement now creates BalanceLedger entries. All payouts unified under single monetary ledger.
