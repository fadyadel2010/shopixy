# AF-38F: Quick Reference Guide

**For**: Developers, QA Engineers, DevOps  
**Last Updated**: 2026-03-01

---

## 🎯 WHAT IS AF-38F?

Auto-synchronization layer that keeps Withdrawal Requests in sync with Legacy Settlement operations.

**In Plain English:**
- Admin pays affiliate via "Settlement" tab → System automatically updates related withdrawal requests
- No more workflow drift between settlement methods
- Affiliate portal always shows accurate status

---

## 🔑 KEY CONCEPTS

### 1. Orchestration-Only

AF-38F **ONLY** updates `AffiliatePayoutRequest` state. It **NEVER**:
- Creates or modifies ledger entries
- Performs financial calculations
- Affects available balance

### 2. FIFO Reconciliation

Oldest withdrawal requests are settled first:
```
Request A (Feb 1) → Request B (Feb 15) → Request C (Feb 28)
Settlement: 180 EGP
Result: A fully paid, B fully paid, C untouched
```

### 3. Transaction Safety

Reconciliation runs inside same transaction as monetary deduction:
```
BEGIN TRANSACTION
  1. Create BalanceLedger.Withdrawal
  2. Auto-reconcile withdrawal requests ← AF-38F
  3. Mark CommissionLedger as Paid
COMMIT
```

---

## 📋 HOW IT WORKS

### Trigger

AF-38F activates when:
- Admin uses "Pay All" or "Pay Selected" in Settlement tab
- `RecordCommissionPaidAsync()` is called
- Money leaves affiliate wallet via Legacy Settlement

### Process

1. **Fetch Open Requests**
   ```sql
   SELECT * FROM AffiliatePayoutRequests
   WHERE AffiliateAccountId = @id
     AND Status IN (Pending, Approved)
     AND RemainingAmount > 0
   ORDER BY RequestedAtUtc ASC  -- FIFO
   ```

2. **Apply Settlement Amount**
   ```csharp
   foreach (var request in openRequests)
   {
       var applied = request.Reconcile(remainingAmount, paymentRef);
       remainingAmount -= applied;
   }
   ```

3. **Update Request State**
   - Reduce `RemainingAmount`
   - Mark as `Paid` if `RemainingAmount` reaches 0
   - Preserve payment reference for audit

4. **Log Actions**
   ```
   [AF-38F:Reconcile] Request fully paid | RequestId=<guid>
   [AF-38F:Reconcile] Partial reconciliation | NewRemaining=80
   ```

---

## 🧪 TESTING CHECKLIST

### Scenario 1: Full Settlement
- [ ] Create withdrawal request for 200 EGP
- [ ] Settle 200 EGP via Legacy Settlement
- [ ] Verify: Status = Paid, RemainingAmount = 0

### Scenario 2: Partial Settlement
- [ ] Create withdrawal request for 200 EGP
- [ ] Settle 120 EGP via Legacy Settlement
- [ ] Verify: Status = Approved, RemainingAmount = 80

### Scenario 3: Multiple Requests (FIFO)
- [ ] Create Request A (100 EGP) on Feb 1
- [ ] Create Request B (150 EGP) on Feb 15
- [ ] Settle 180 EGP via Legacy Settlement
- [ ] Verify: A = Paid, B = Paid (80 applied)

### Scenario 4: No Open Requests
- [ ] Settle 120 EGP with no open requests
- [ ] Verify: No errors, log shows "No open requests found"

### Scenario 5: Concurrent Settlement
- [ ] Two admins settle same order simultaneously
- [ ] Verify: Only one succeeds, other gets idempotent skip

---

## 🔍 DEBUGGING

### Check Reconciliation Status

```sql
-- Find partially settled requests
SELECT 
    "Id",
    "RequestedAmount",
    "RemainingAmount",
    "RequestedAmount" - "RemainingAmount" as amount_settled,
    "Status"
FROM "AffiliatePayoutRequests"
WHERE "RemainingAmount" < "RequestedAmount"
  AND "RemainingAmount" > 0;
```

### Check Recent Reconciliations

```sql
-- Find auto-reconciled requests (last 7 days)
SELECT 
    "Id",
    "AffiliateAccountId",
    "RequestedAmount",
    "PaidAtUtc",
    "PaymentReference"
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4  -- Paid
  AND "PaymentReference" LIKE '%Order #%'
  AND "PaidAtUtc" >= NOW() - INTERVAL '7 days'
ORDER BY "PaidAtUtc" DESC;
```

### Check Logs

```bash
# Filter AF-38F logs
grep "\[AF-38F:Reconcile\]" application.log

# Find reconciliation errors
grep "\[AF-38F:Reconcile\].*ERROR" application.log

# Count reconciliations today
grep "\[AF-38F:Reconcile\] Reconciliation complete" application.log | grep "$(date +%Y-%m-%d)" | wc -l
```

---

## 🚨 COMMON ISSUES

### Issue 1: Request Not Reconciling

**Symptoms:**
- Settlement succeeds
- Withdrawal request still shows full amount

**Possible Causes:**
1. Request status is Rejected or already Paid
2. Request belongs to different affiliate
3. Transaction rolled back due to error

**Debug:**
```sql
-- Check request status
SELECT "Status", "RemainingAmount", "AffiliateAccountId"
FROM "AffiliatePayoutRequests"
WHERE "Id" = '<request-id>';

-- Check if BalanceLedger entry exists
SELECT * FROM "AffiliateBalanceLedger"
WHERE "AffiliateAccountId" = '<affiliate-id>'
  AND "EntryType" = 1  -- Withdrawal
ORDER BY "RecordedAtUtc" DESC;
```

### Issue 2: Negative RemainingAmount

**Symptoms:**
- `RemainingAmount` < 0

**Cause:**
- BUG in reconciliation logic

**Action:**
- Run Query 1 from `AF-38F_VERIFICATION_QUERIES.sql`
- Report to development team immediately

### Issue 3: FIFO Violation

**Symptoms:**
- Newer request paid before older request

**Cause:**
- BUG in FIFO ordering

**Action:**
- Run Query 5 from `AF-38F_VERIFICATION_QUERIES.sql`
- Report to development team immediately

---

## 📊 MONITORING

### Key Metrics

1. **Reconciliation Rate**
   ```sql
   -- % of paid requests that were auto-reconciled
   SELECT 
       COUNT(CASE WHEN "PaymentReference" LIKE '%Order #%' THEN 1 END) * 100.0 / COUNT(*) as auto_reconciled_pct
   FROM "AffiliatePayoutRequests"
   WHERE "Status" = 4
     AND "PaidAtUtc" >= NOW() - INTERVAL '30 days';
   ```

2. **Partial Settlements**
   ```sql
   -- Count of partially settled requests
   SELECT COUNT(*)
   FROM "AffiliatePayoutRequests"
   WHERE "Status" IN (1, 2)
     AND "RemainingAmount" < "RequestedAmount";
   ```

3. **Average Settlement Time**
   ```sql
   -- Days from request to full payment
   SELECT AVG(EXTRACT(EPOCH FROM ("PaidAtUtc" - "RequestedAtUtc")) / 86400) as avg_days
   FROM "AffiliatePayoutRequests"
   WHERE "Status" = 4
     AND "PaidAtUtc" >= NOW() - INTERVAL '30 days';
   ```

### Health Check

Run this daily:
```sql
-- AF-38F Health Dashboard
SELECT 
    'Negative RemainingAmount (BUG)' as check_name,
    COUNT(*)::text as result,
    CASE WHEN COUNT(*) = 0 THEN '✅ PASS' ELSE '❌ FAIL' END as status
FROM "AffiliatePayoutRequests"
WHERE "RemainingAmount" < 0

UNION ALL

SELECT 
    'Paid with Non-Zero Remaining (BUG)',
    COUNT(*)::text,
    CASE WHEN COUNT(*) = 0 THEN '✅ PASS' ELSE '❌ FAIL' END
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4 AND "RemainingAmount" != 0

UNION ALL

SELECT 
    'Auto-Reconciled Today',
    COUNT(*)::text,
    '📊 INFO'
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4
  AND "PaymentReference" LIKE '%Order #%'
  AND "PaidAtUtc" >= CURRENT_DATE;
```

---

## 🔧 CONFIGURATION

### No Configuration Required

AF-38F is always active. No feature flags or settings.

### Disable (Emergency Only)

If critical bug detected:

1. **Code Change**
   ```csharp
   // In AffiliateService.RecordCommissionPaidAsync()
   // Comment out this line:
   // await _payoutService.AutoReconcileWithdrawalRequestsAsync(...);
   ```

2. **Redeploy**

3. **Manual Reconciliation**
   - Admin must manually mark requests as Paid

---

## 📞 SUPPORT

### Questions?

- **Technical Docs**: `AF-38F_AUTO_SYNCHRONIZATION_LAYER.md`
- **Verification Queries**: `AF-38F_VERIFICATION_QUERIES.sql`
- **Implementation Details**: `AF-38F_IMPLEMENTATION_SUMMARY.md`

### Report Issues

Include:
1. Affiliate ID
2. Withdrawal Request ID
3. Settlement Order ID
4. Expected vs Actual behavior
5. Relevant logs with `[AF-38F]` prefix

---

## ✅ QUICK VALIDATION

After deployment, run these checks:

```sql
-- 1. No negative amounts
SELECT COUNT(*) FROM "AffiliatePayoutRequests" WHERE "RemainingAmount" < 0;
-- Expected: 0

-- 2. Paid requests have zero remaining
SELECT COUNT(*) FROM "AffiliatePayoutRequests" WHERE "Status" = 4 AND "RemainingAmount" != 0;
-- Expected: 0

-- 3. Auto-reconciliation working
SELECT COUNT(*) FROM "AffiliatePayoutRequests" 
WHERE "PaymentReference" LIKE '%Order #%' 
  AND "PaidAtUtc" >= NOW() - INTERVAL '1 hour';
-- Expected: > 0 (if settlements happened)
```

---

**Last Updated**: 2026-03-01  
**Version**: 1.0  
**Status**: Production Ready
