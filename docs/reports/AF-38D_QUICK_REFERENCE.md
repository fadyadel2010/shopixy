# AF-38D: Double Accounting Fix - Quick Reference

**Status**: ✅ Complete | **Date**: 2026-03-01 | **Priority**: CRITICAL

---

## The Bug

**Problem**: Same payout deducted twice in balance calculation

**Example**:
- Approved: 270 EGP
- Withdrawal: -150 EGP
- **Before**: Available = 0 EGP ❌
- **After**: Available = 120 EGP ✅

---

## The Fix

### Old Formula (WRONG)
```
Available = Approved - CommissionPaid - Withdrawals - Pending
```
❌ Deducts both `CommissionPaid` AND `Withdrawals`

### New Formula (CORRECT)
```
Available = Approved + Adjustments + Withdrawals - Pending
```
✅ Only deducts actual money movements (`Withdrawals`)

---

## Key Principle

| Ledger Entry | Meaning | Affects Balance? |
|--------------|---------|------------------|
| `CommissionLedger.Approved` | Money earned | ✅ YES (+) |
| `CommissionLedger.Paid` | State transition | ❌ NO |
| `BalanceLedger.Withdrawal` | Money withdrawn | ✅ YES (-) |
| `BalanceLedger.Bonus` | Money added | ✅ YES (+) |
| `BalanceLedger.Penalty` | Money deducted | ✅ YES (-) |
| `PayoutRequest.Pending` | Money reserved | ✅ YES (-) |

**Rule**: Only `BalanceLedger` affects available balance. `CommissionLedger.Paid` is lifecycle-only.

---

## What Changed

### File: `AffiliatePayoutService.cs`

1. **Removed**: `totalCommissionPaid` query
2. **Fixed**: Balance calculation formula
3. **Updated**: `TotalPaid` definition (now only withdrawals)
4. **Added**: Mixed settlement detection warning

---

## Test Cases

| Scenario | Approved | Paid | Withdrawal | Expected |
|----------|----------|------|------------|----------|
| Legacy only | 200 | 200 | 0 | 200 ✅ |
| Withdrawal only | 300 | 0 | -100 | 200 ✅ |
| Mixed (bug case) | 270 | 150 | -150 | 120 ✅ |
| With bonus | 300 | 0 | -100 | 230 ✅ (bonus +30) |

---

## Deployment

### Pre-Deploy
- ✅ Code compiles
- ✅ No schema changes
- ✅ No migration needed

### Deploy
1. Deploy code
2. Restart app
3. Monitor logs

### Verify
```sql
-- Check affiliate balance
SELECT 
    SUM(CASE WHEN "EntryType" = 2 THEN "CommissionAmount" ELSE 0 END) as approved,
    (SELECT SUM("Amount") FROM "AffiliateBalanceLedger" 
     WHERE "AffiliateAccountId" = @affiliateId) as movements
FROM "AffiliateCommissionLedger"
WHERE "AffiliateAccountId" = @affiliateId;
```

---

## Monitoring

### Log to Watch
```
[AF-38D] Mixed settlement detected | AffiliateId={guid}
```
Indicates affiliate using both legacy and new settlement methods.

---

## Impact

- ✅ **Performance**: Improved (one less query)
- ✅ **Accuracy**: Fixed (no double deduction)
- ✅ **UX**: Fixed (withdrawal button works)
- ❌ **Breaking**: None

---

## Related Docs

- `AF-38D_DOUBLE_ACCOUNTING_FIX.md` - Full technical details
- `AF-38D_IMPLEMENTATION_SUMMARY.md` - Implementation summary
- `AF-38C_DUAL_LEDGER_ARCHITECTURE.md` - Architecture reference

---

**Quick Answer**: Removed `CommissionLedger.Paid` from balance formula. Only `BalanceLedger` affects available balance now.
