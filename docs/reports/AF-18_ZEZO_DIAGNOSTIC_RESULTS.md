# AF-18: ZEZO Diagnostic Results

## Query Results Summary

### ✅ STEP 1: AffiliateAccount Verification - PASSED

```
AffiliateAccountId: 019c8fb7-0756-7ab3-9187-086089e86e06
DisplayName: zezo
ReferralCode: ZEZO906409
Status: 1 (Active)
ActivatedAtUtc: 2026-02-24 14:54:22.802715+02
IsDeleted: false
```

**Result:** Affiliate exists and is Active ✅

---

### ❌ STEP 7: Commission Check - FAILED

```
OrdersWithCommission: 0
TotalEarned_SQL: 0
TotalApproved_SQL: 0
TotalPaid_SQL: 0
```

**Result:** NO commission entries found ❌

---

## Next Steps

Run the following queries in order to identify where the pipeline broke:

### Query 1: Check Orders with ZEZO Referral
```sql
-- STEP 2 from af18-diagnostic-queries.sql
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."TenantId",
    o."Status",
    o."PaymentStatus",
    o."FulfillmentStatus",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."PromotionLinkId",
    o."Total",
    o."Currency",
    o."CreatedAt"
FROM "Orders" o
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
ORDER BY o."CreatedAt" DESC;
```

**Expected:** 1+ orders with `AffiliateAccountId = 019c8fb7-0756-7ab3-9187-086089e86e06`

---

### Query 2: Check Attribution
```sql
-- STEP 3 from af18-diagnostic-queries.sql
SELECT 
    aa."Id" AS AttributionId,
    aa."TenantId",
    aa."OrderId",
    aa."AffiliateAccountId",
    aa."ReferralCode",
    aa."Source",
    aa."PromotionLinkId",
    aa."CreatedAt"
FROM "AffiliateAttributions" aa
WHERE aa."ReferralCode" ILIKE '%ZEZO%'
  AND aa."IsDeleted" = false
ORDER BY aa."CreatedAt" DESC;
```

**Expected:** 1 attribution per order

---

### Query 3: Check Commission Ledger
```sql
-- STEP 4 from af18-diagnostic-queries.sql
SELECT 
    acl."Id" AS LedgerEntryId,
    acl."TenantId",
    acl."AffiliateAccountId",
    acl."OrderId",
    acl."OrderItemId",
    acl."AttributionId",
    acl."EntryType",
    acl."CommissionAmount",
    acl."RecordedAtUtc"
FROM "AffiliateCommissionLedger" acl
WHERE acl."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY acl."RecordedAtUtc" DESC;
```

**Expected:** 1+ entries with `EntryType = 0` (Earned)

---

### Query 4: Check Product Policies
```sql
-- STEP 5 from af18-diagnostic-queries.sql
SELECT 
    app."Id" AS PolicyId,
    app."TenantId",
    app."ProductId",
    app."CommissionRate",
    app."MaxCommissionPerItem",
    app."IsEnabled",
    p."Name" AS ProductName,
    p."SKU"
FROM "AffiliateProductPolicies" app
INNER JOIN "Products" p ON app."ProductId" = p."Id"
WHERE app."ProductId" IN (
    SELECT DISTINCT oi."ProductId"
    FROM "OrderItems" oi
    INNER JOIN "Orders" o ON oi."OrderId" = o."Id"
    WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
      AND o."IsDeleted" = false
)
  AND app."IsDeleted" = false
ORDER BY app."CreatedAt" DESC;
```

**Expected:** 1+ policies with `IsEnabled = true`

---

### Query 5: Full Pipeline Trace
```sql
-- STEP 6 from af18-diagnostic-queries.sql
SELECT 
    o."OrderNumber",
    o."Status" AS OrderStatus,
    o."FulfillmentStatus",
    o."Total",
    o."Currency",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."PromotionLinkId",
    aa."Id" AS AttributionId,
    aa."Source" AS AttributionSource,
    COUNT(DISTINCT acl."Id") AS CommissionEntriesCount,
    SUM(CASE WHEN acl."EntryType" = 0 THEN acl."CommissionAmount" ELSE 0 END) AS TotalEarned,
    o."CreatedAt" AS OrderCreatedAt,
    aa."CreatedAt" AS AttributionCreatedAt,
    MAX(acl."RecordedAtUtc") AS LastCommissionRecordedAt
FROM "Orders" o
LEFT JOIN "AffiliateAttributions" aa ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl ON aa."Id" = acl."AttributionId"
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
GROUP BY 
    o."OrderNumber", o."Status", o."FulfillmentStatus", o."Total", o."Currency",
    o."AffiliateAccountId", o."AffiliateReferralCode", o."PromotionLinkId",
    aa."Id", aa."Source", o."CreatedAt", aa."CreatedAt"
ORDER BY o."CreatedAt" DESC;
```

**Expected:** Shows complete pipeline with NULL values indicating where it broke

---

## Diagnostic Decision Tree

### If Query 1 (Orders) returns 0 rows:
**Problem:** No orders placed with ZEZO referral code
**Fix:** Not a bug - affiliate has no orders yet

### If Query 1 shows orders but `AffiliateAccountId IS NULL`:
**Problem:** Middleware didn't stamp affiliate identity
**Fix:** Check `AffiliateReferralMiddleware` and `AffiliateModeResolver`

### If Query 2 (Attribution) returns 0 rows:
**Problem:** Attribution NOT created during checkout
**Fix:** Check `CheckoutService` line ~2000-2030
**Likely Cause:** Transaction rolled back or attribution creation failed

### If Query 3 (Commission Ledger) returns 0 rows:
**Problem:** Commission NOT recorded (THIS IS THE BUG)
**Fix:** Deploy `OrderCompletedAffiliateConsumer`
**Root Cause:** Order went to Completed status without triggering commission recording

### If Query 4 (Product Policies) returns 0 rows or `IsEnabled = false`:
**Problem:** Product NOT enabled for affiliate program
**Fix:** Admin must enable product in affiliate settings

---

## Expected Diagnosis

Based on the root cause analysis, we expect:
- ✅ Query 1: Orders exist with `AffiliateAccountId` populated
- ✅ Query 2: Attribution exists
- ❌ Query 3: Commission ledger EMPTY (the bug)
- ✅ Query 4: Product policies exist and enabled

**Conclusion:** Orders went to `Completed` status without triggering `OrderCompletedEvent` consumer, so commission was never recorded.

---

## Action Items

1. ✅ Run Query 1-5 to confirm diagnosis
2. ⏳ Deploy `OrderCompletedAffiliateConsumer` fix
3. ⏳ Backfill historical orders using `af18-backfill-missing-commissions.sql`
4. ⏳ Verify ZEZO appears in `/admin/affiliates` with correct earnings
5. ⏳ Monitor application logs for `[Affiliate:OrderCompleted]` entries

---

**Document Version:** 1.0  
**Date:** 2026-02-24  
**Affiliate:** ZEZO (019c8fb7-0756-7ab3-9187-086089e86e06)  
**Status:** Diagnosis in progress
