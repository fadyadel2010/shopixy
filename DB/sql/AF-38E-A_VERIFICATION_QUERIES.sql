-- ═══════════════════════════════════════════════════════════════════════════
-- AF-38E-A: Monetary Integrity Audit - Verification Queries
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- PURPOSE:
-- Post-deployment verification queries to validate financial integrity
--
-- RUN THESE AFTER AF-38E MIGRATION
--
-- ═══════════════════════════════════════════════════════════════════════════

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 1: Historical Timestamp Integrity
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 1: Historical Timestamp Integrity' as phase,
    bl."ReferenceId" as order_id,
    bl."RecordedAtUtc" as balance_ledger_date,
    MIN(cl."RecordedAtUtc") as original_paid_date,
    CASE 
        WHEN bl."RecordedAtUtc" = MIN(cl."RecordedAtUtc") THEN '✓ PASS'
        ELSE '✗ FAIL - Timestamp mismatch'
    END as status
FROM "AffiliateBalanceLedger" bl
JOIN "AffiliateCommissionLedger" cl 
    ON cl."OrderId" = bl."ReferenceId" 
    AND cl."EntryType" = 3  -- Paid
WHERE bl."Description" LIKE 'AF-38E: Backfilled%'
GROUP BY bl."ReferenceId", bl."RecordedAtUtc"
ORDER BY status DESC, bl."RecordedAtUtc";

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 2: Orphan Detection
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 2: Orphan Detection' as phase,
    COUNT(DISTINCT cl."OrderId") as orphan_count,
    CASE 
        WHEN COUNT(DISTINCT cl."OrderId") = 0 THEN '✓ PASS - No orphans'
        ELSE '✗ FAIL - ' || COUNT(DISTINCT cl."OrderId") || ' orphaned orders'
    END as status
FROM "AffiliateCommissionLedger" cl
WHERE cl."EntryType" = 3  -- Paid
  AND NOT EXISTS (
      SELECT 1
      FROM "AffiliateBalanceLedger" bl
      WHERE bl."ReferenceId" = cl."OrderId"
        AND bl."EntryType" = 1  -- Withdrawal
        AND bl."AffiliateAccountId" = cl."AffiliateAccountId"
  );

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 3: Idempotency Check
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 3: Idempotency Check' as phase,
    conname as constraint_name,
    contype as constraint_type,
    CASE 
        WHEN conname LIKE '%Idempotency%' THEN '✓ PASS - Constraint exists'
        ELSE '✗ FAIL'
    END as status
FROM pg_constraint
WHERE conrelid = '"AffiliateBalanceLedger"'::regclass
  AND conname LIKE '%Idempotency%';

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 4: Duplicate Detection
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 4: Duplicate Detection' as phase,
    "ReferenceId" as order_id,
    COUNT(*) as withdrawal_count,
    CASE 
        WHEN COUNT(*) = 1 THEN '✓ PASS'
        ELSE '✗ FAIL - ' || COUNT(*) || ' withdrawals for same order'
    END as status
FROM "AffiliateBalanceLedger"
WHERE "EntryType" = 1  -- Withdrawal
GROUP BY "ReferenceId"
HAVING COUNT(*) > 1;

-- Expected: 0 rows (no duplicates)

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 5: Cross-Affiliate Isolation
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 5: Cross-Affiliate Isolation' as phase,
    bl."Id" as balance_ledger_id,
    bl."AffiliateAccountId" as balance_affiliate,
    cl."AffiliateAccountId" as commission_affiliate,
    bl."ReferenceId" as order_id,
    '✗ FAIL - Cross-affiliate contamination' as status
FROM "AffiliateBalanceLedger" bl
JOIN "AffiliateCommissionLedger" cl ON cl."OrderId" = bl."ReferenceId"
WHERE bl."AffiliateAccountId" != cl."AffiliateAccountId"
  AND bl."EntryType" = 1;  -- Withdrawal

-- Expected: 0 rows

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 6: Balance Integrity Per Affiliate
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 6: Balance Integrity' as phase,
    a."Id" as affiliate_id,
    a."DisplayName" as affiliate_name,
    COALESCE(SUM(CASE WHEN cl."EntryType" = 2 THEN cl."CommissionAmount" ELSE 0 END), 0) as approved,
    COALESCE(SUM(CASE WHEN cl."EntryType" = 3 THEN cl."CommissionAmount" ELSE 0 END), 0) as paid_lifecycle,
    COALESCE((SELECT SUM(bl."Amount") FROM "AffiliateBalanceLedger" bl 
              WHERE bl."AffiliateAccountId" = a."Id"), 0) as balance_total,
    COALESCE((SELECT COUNT(*) FROM "AffiliateBalanceLedger" bl 
              WHERE bl."AffiliateAccountId" = a."Id" AND bl."EntryType" = 1), 0) as withdrawal_count,
    CASE 
        WHEN COALESCE((SELECT COUNT(*) FROM "AffiliateBalanceLedger" bl 
                       WHERE bl."AffiliateAccountId" = a."Id" AND bl."EntryType" = 1), 0) >= 
             COALESCE(SUM(CASE WHEN cl."EntryType" = 3 THEN 1 ELSE 0 END), 0) / 
             NULLIF(COUNT(DISTINCT CASE WHEN cl."EntryType" = 3 THEN cl."OrderId" END), 0)
        THEN '✓ PASS'
        ELSE '⚠️ CHECK'
    END as status
FROM "AffiliateAccounts" a
LEFT JOIN "AffiliateCommissionLedger" cl ON a."Id" = cl."AffiliateAccountId"
WHERE a."IsDeleted" = false
GROUP BY a."Id", a."DisplayName"
HAVING COALESCE(SUM(CASE WHEN cl."EntryType" = 3 THEN cl."CommissionAmount" ELSE 0 END), 0) > 0
ORDER BY a."DisplayName";

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 7: Migration Backfill Count
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 7: Migration Backfill Count' as phase,
    COUNT(*) as backfilled_count,
    SUM(ABS("Amount")) as total_backfilled_amount,
    COUNT(DISTINCT "AffiliateAccountId") as affected_affiliates,
    '✓ INFO' as status
FROM "AffiliateBalanceLedger"
WHERE "Description" LIKE 'AF-38E: Backfilled%';

-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE 8: Consistency Check
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    'Phase 8: Consistency Check' as phase,
    CASE 
        WHEN COUNT(*) = 0 THEN '✓ PASS - All affiliates have consistent ledgers'
        ELSE '✗ FAIL - ' || COUNT(*) || ' affiliates have inconsistent ledgers'
    END as status,
    COUNT(*) as inconsistent_count
FROM (
    SELECT 
        cl."AffiliateAccountId",
        COUNT(DISTINCT CASE WHEN cl."EntryType" = 3 THEN cl."OrderId" END) as paid_order_count,
        (SELECT COUNT(DISTINCT bl."ReferenceId") 
         FROM "AffiliateBalanceLedger" bl 
         WHERE bl."AffiliateAccountId" = cl."AffiliateAccountId" 
           AND bl."EntryType" = 1) as withdrawal_order_count
    FROM "AffiliateCommissionLedger" cl
    GROUP BY cl."AffiliateAccountId"
    HAVING COUNT(DISTINCT CASE WHEN cl."EntryType" = 3 THEN cl."OrderId" END) > 
           (SELECT COUNT(DISTINCT bl."ReferenceId") 
            FROM "AffiliateBalanceLedger" bl 
            WHERE bl."AffiliateAccountId" = cl."AffiliateAccountId" 
              AND bl."EntryType" = 1)
) inconsistent_affiliates;

-- ═══════════════════════════════════════════════════════════════════════════
-- SUMMARY REPORT
-- ═══════════════════════════════════════════════════════════════════════════

SELECT 
    '═══════════════════════════════════════════════════════════' as separator
UNION ALL
SELECT 'AF-38E-A VERIFICATION SUMMARY'
UNION ALL
SELECT '═══════════════════════════════════════════════════════════'
UNION ALL
SELECT 'Run Date: ' || NOW()::text
UNION ALL
SELECT '═══════════════════════════════════════════════════════════'
UNION ALL
SELECT 'Total Affiliates: ' || COUNT(*)::text FROM "AffiliateAccounts" WHERE "IsDeleted" = false
UNION ALL
SELECT 'Total Paid Orders: ' || COUNT(DISTINCT "OrderId")::text FROM "AffiliateCommissionLedger" WHERE "EntryType" = 3
UNION ALL
SELECT 'Total Withdrawals: ' || COUNT(*)::text FROM "AffiliateBalanceLedger" WHERE "EntryType" = 1
UNION ALL
SELECT 'Backfilled Entries: ' || COUNT(*)::text FROM "AffiliateBalanceLedger" WHERE "Description" LIKE 'AF-38E: Backfilled%'
UNION ALL
SELECT '═══════════════════════════════════════════════════════════';
