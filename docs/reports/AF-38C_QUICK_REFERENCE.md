# AF-38C: Dual-Ledger Quick Reference

## TL;DR

We split the overloaded `AffiliateCommissionLedger` into two specialized ledgers:

1. **CommissionLedger**: Commerce earnings only (per order item)
2. **BalanceLedger**: Account movements (withdrawals, adjustments, bonuses, penalties)

---

## When to Use Which Ledger

### Use CommissionLedger When:
- Recording commission earned from an order
- Approving commission for payment
- Marking commission as paid (legacy settlement)
- Reversing commission due to refund

### Use BalanceLedger When:
- Processing affiliate withdrawal payout
- Applying manual adjustment
- Awarding promotional bonus
- Deducting penalty

---

## Code Examples

### Recording a Withdrawal (Admin marks payout as Paid)

**Before (AF-38):**
```csharp
var payoutEntry = AffiliateCommissionLedger.CreatePayout(
    storeTenantId, affiliateAccountId, payoutRequestId, amount, currency);
_db.AffiliateCommissionLedger.Add(payoutEntry);
```

**After (AF-38C):**
```csharp
var withdrawalEntry = AffiliateBalanceLedger.CreateWithdrawal(
    storeTenantId, affiliateAccountId, payoutRequestId, amount, currency);
_db.AffiliateBalanceLedger.Add(withdrawalEntry);
```

### Calculating Available Balance

**Before (AF-38):**
```csharp
var totalWithdrawn = await _db.AffiliateCommissionLedger
    .Where(l => l.AffiliateAccountId == id && l.EntryType == CommissionEntryType.Payout)
    .SumAsync(l => l.CommissionAmount);
```

**After (AF-38C):**
```csharp
var totalWithdrawn = await _db.AffiliateBalanceLedger
    .Where(l => l.AffiliateAccountId == id && l.EntryType == BalanceEntryType.Withdrawal)
    .SumAsync(l => l.Amount); // Already negative
```

### Awarding a Bonus

**New in AF-38C:**
```csharp
var bonus = AffiliateBalanceLedger.CreateBonus(
    storeTenantId: storeTenantId,
    affiliateAccountId: affiliateId,
    amount: 100m,
    currency: "EGP",
    description: "Referral milestone bonus",
    referenceId: campaignId);

_db.AffiliateBalanceLedger.Add(bonus);
await _db.SaveChangesAsync();
```

### Applying a Penalty

**New in AF-38C:**
```csharp
var penalty = AffiliateBalanceLedger.CreatePenalty(
    storeTenantId: storeTenantId,
    affiliateAccountId: affiliateId,
    amount: 50m, // Will be stored as -50
    currency: "EGP",
    description: "Policy violation - fake traffic",
    referenceId: violationId);

_db.AffiliateBalanceLedger.Add(penalty);
await _db.SaveChangesAsync();
```

### Manual Adjustment

**New in AF-38C:**
```csharp
var adjustment = AffiliateBalanceLedger.CreateAdjustment(
    storeTenantId: storeTenantId,
    affiliateAccountId: affiliateId,
    amount: -25m, // Negative for deduction, positive for addition
    currency: "EGP",
    description: "Accounting correction - duplicate payout reversal",
    referenceId: correctionTicketId);

_db.AffiliateBalanceLedger.Add(adjustment);
await _db.SaveChangesAsync();
```

---

## Balance Calculation Formula

```csharp
AvailableBalance = 
    SUM(CommissionLedger WHERE EntryType=Approved)
  - SUM(CommissionLedger WHERE EntryType=Paid)
  + SUM(BalanceLedger.Amount) // Includes withdrawals (negative), bonuses (positive), etc.
  - SUM(PayoutRequests WHERE Status IN (Pending, Approved))
```

---

## Entry Types

### CommissionEntryType (CommissionLedger)
- `Earned (0)`: Commission earned from order
- `Reversed (1)`: Commission reversed due to refund
- `Approved (2)`: Commission approved for payment
- `Paid (3)`: Commission paid to affiliate
- ~~`Payout (4)`~~ **REMOVED in AF-38C**

### BalanceEntryType (BalanceLedger)
- `Withdrawal (1)`: Payout to affiliate (negative amount)
- `Adjustment (2)`: Manual correction (± amount)
- `Bonus (3)`: Promotional reward (positive amount)
- `Penalty (4)`: Violation penalty (negative amount)

---

## Database Tables

### AffiliateCommissionLedger (Strict)
- **Purpose**: Commerce-derived earnings
- **Granularity**: Per order item
- **FKs**: AttributionId, OrderId, OrderItemId (all NOT NULL)
- **Rate**: 0 < CommissionRate <= 0.50 (strict)
- **EntryTypes**: 0, 1, 2, 3 only

### AffiliateBalanceLedger (Flexible)
- **Purpose**: Account-level movements
- **Granularity**: Per affiliate account
- **FKs**: None to Attribution/Order/OrderItem
- **Amount**: Can be positive or negative (not zero)
- **EntryTypes**: 1, 2, 3, 4

---

## Migration Checklist

- [ ] Run `migrations/AF38C_DualLedgerArchitecture.sql`
- [ ] Verify no Payout entries in CommissionLedger
- [ ] Verify all FKs are NOT NULL in CommissionLedger
- [ ] Verify BalanceLedger has migrated entries
- [ ] Test withdrawal flow end-to-end
- [ ] Verify balance calculations match pre-migration
- [ ] Monitor logs for `[AF-38C:*]` entries

---

## Common Queries

### Get Affiliate Balance Breakdown
```sql
SELECT 
    'Approved Commissions' AS source,
    SUM("CommissionAmount") AS amount
FROM "AffiliateCommissionLedger"
WHERE "AffiliateAccountId" = @affiliateId
  AND "EntryType" = 2

UNION ALL

SELECT 
    'Paid Commissions' AS source,
    -SUM("CommissionAmount") AS amount
FROM "AffiliateCommissionLedger"
WHERE "AffiliateAccountId" = @affiliateId
  AND "EntryType" = 3

UNION ALL

SELECT 
    'Withdrawals' AS source,
    SUM("Amount") AS amount
FROM "AffiliateBalanceLedger"
WHERE "AffiliateAccountId" = @affiliateId
  AND "EntryType" = 1

UNION ALL

SELECT 
    'Adjustments/Bonuses/Penalties' AS source,
    SUM("Amount") AS amount
FROM "AffiliateBalanceLedger"
WHERE "AffiliateAccountId" = @affiliateId
  AND "EntryType" IN (2, 3, 4);
```

### Verify Ledger Integrity
```sql
-- Should return 0 rows (no Payout entries in CommissionLedger)
SELECT * FROM "AffiliateCommissionLedger" WHERE "EntryType" = 4;

-- Should return 0 rows (no NULL FKs in CommissionLedger)
SELECT * FROM "AffiliateCommissionLedger" 
WHERE "AttributionId" IS NULL 
   OR "OrderId" IS NULL 
   OR "OrderItemId" IS NULL;

-- Should return count > 0 (migrated withdrawals exist)
SELECT COUNT(*) FROM "AffiliateBalanceLedger" WHERE "EntryType" = 1;
```

---

## Troubleshooting

### Balance Mismatch After Migration
1. Run reconciliation query (see above)
2. Compare pre-migration and post-migration totals
3. Check for missing BalanceLedger entries
4. Apply corrective adjustment if needed

### Withdrawal Fails with "Already has BalanceLedger withdrawal entry"
- This is idempotency protection working correctly
- Check if payout request is already marked as Paid
- Verify BalanceLedger has entry with matching ReferenceId

### Commission Recording Fails with "AttributionId cannot be null"
- This is correct behavior in AF-38C
- Commission MUST have valid attribution
- Check if attribution exists for the order

---

## Support

For questions or issues:
1. Check `AF-38C_DUAL_LEDGER_ARCHITECTURE.md` for detailed documentation
2. Review migration logs for errors
3. Run diagnostic queries above
4. Contact architecture team

---

**Last Updated**: 2026-02-28  
**Version**: AF-38C
