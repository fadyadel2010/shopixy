-- ═══════════════════════════════════════════════════════════════════════════
-- AF-38F: Auto-Synchronization Layer - Verification Queries
-- ═══════════════════════════════════════════════════════════════════════════
-- Purpose: Validate AF-38F implementation correctness
-- Date: 2026-03-01
-- ═══════════════════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 1: Verify No Negative RemainingAmount
-- ───────────────────────────────────────────────────────────────────────────
-- Expected: 0 rows
-- If rows found: CRITICAL BUG - reconciliation logic error

SELECT 
    "Id",
    "AffiliateAccountId",
    "RequestedAmount",
    "RemainingAmount",
    "Status",
    "RequestedAtUtc"
FROM "AffiliatePayoutRequests"
WHERE "RemainingAmount" < 0;

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 2: Verify Paid Requests Have Zero RemainingAmount
-- ───────────────────────────────────────────────────────────────────────────
-- Expected: 0 rows
-- If rows found: Reconciliation didn't complete properly

SELECT 
    "Id",
    "AffiliateAccountId",
    "RequestedAmount",
    "RemainingAmount",
    "Status",
    "PaidAtUtc"
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4  -- Paid
  AND "RemainingAmount" != 0;

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 3: Verify Open Requests Have Positive RemainingAmount
-- ───────────────────────────────────────────────────────────────────────────
-- Expected: All rows have RemainingAmount > 0
-- If rows with RemainingAmount = 0: Should have been marked Paid

SELECT 
    "Id",
    "AffiliateAccountId",
    "RequestedAmount",
    "RemainingAmount",
    "Status",
    "RequestedAtUtc"
FROM "AffiliatePayoutRequests"
WHERE "Status" IN (1, 2)  -- Pending or Approved
  AND "RemainingAmount" <= 0;

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 4: Reconciliation Audit - Match BalanceLedger to PayoutRequest
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Verify that settlement amounts match reconciliation amounts
-- Note: This is complex because legacy settlement uses OrderId as ReferenceId

WITH SettlementAmounts AS (
    SELECT 
        "AffiliateAccountId",
        SUM(ABS("Amount")) as total_settled
    FROM "AffiliateBalanceLedger"
    WHERE "EntryType" = 1  -- Withdrawal
    GROUP BY "AffiliateAccountId"
),
RequestAmounts AS (
    SELECT 
        "AffiliateAccountId",
        SUM("RequestedAmount" - "RemainingAmount") as total_reconciled
    FROM "AffiliatePayoutRequests"
    WHERE "Status" IN (2, 4)  -- Approved or Paid (Pending not yet reconciled)
    GROUP BY "AffiliateAccountId"
)
SELECT 
    COALESCE(s."AffiliateAccountId", r."AffiliateAccountId") as affiliate_id,
    COALESCE(s.total_settled, 0) as total_settled,
    COALESCE(r.total_reconciled, 0) as total_reconciled,
    COALESCE(s.total_settled, 0) - COALESCE(r.total_reconciled, 0) as difference
FROM SettlementAmounts s
FULL OUTER JOIN RequestAmounts r ON s."AffiliateAccountId" = r."AffiliateAccountId"
WHERE COALESCE(s.total_settled, 0) != COALESCE(r.total_reconciled, 0);

-- Expected: Rows with difference > 0 indicate settlements without withdrawal requests (valid)
-- Expected: Rows with difference < 0 indicate over-reconciliation (INVALID - BUG)

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 5: FIFO Order Verification
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Verify that older requests are settled before newer ones
-- Logic: If an older request is still open, no newer request should be Paid

WITH RankedRequests AS (
    SELECT 
        "Id",
        "AffiliateAccountId",
        "RequestedAtUtc",
        "Status",
        "RemainingAmount",
        ROW_NUMBER() OVER (
            PARTITION BY "AffiliateAccountId" 
            ORDER BY "RequestedAtUtc" ASC
        ) as request_order
    FROM "AffiliatePayoutRequests"
    WHERE "Status" IN (1, 2, 4)  -- Pending, Approved, Paid
)
SELECT 
    older."AffiliateAccountId",
    older."Id" as older_request_id,
    older."RequestedAtUtc" as older_request_date,
    older."Status" as older_status,
    older."RemainingAmount" as older_remaining,
    newer."Id" as newer_request_id,
    newer."RequestedAtUtc" as newer_request_date,
    newer."Status" as newer_status
FROM RankedRequests older
JOIN RankedRequests newer 
    ON older."AffiliateAccountId" = newer."AffiliateAccountId"
    AND older.request_order < newer.request_order
WHERE older."Status" IN (1, 2)  -- Older request still open
  AND newer."Status" = 4  -- Newer request already paid
  AND older."RemainingAmount" > 0;

-- Expected: 0 rows (FIFO order maintained)
-- If rows found: FIFO violation - newer requests paid before older ones

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 6: Partial Settlement Detection
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Find requests that have been partially reconciled
-- Useful for admin visibility

SELECT 
    pr."Id",
    pr."AffiliateAccountId",
    aa."ReferralCode",
    pr."RequestedAmount",
    pr."RemainingAmount",
    pr."RequestedAmount" - pr."RemainingAmount" as amount_settled,
    ROUND(
        ((pr."RequestedAmount" - pr."RemainingAmount") / pr."RequestedAmount") * 100, 
        2
    ) as percent_settled,
    pr."Status",
    pr."RequestedAtUtc",
    pr."Currency"
FROM "AffiliatePayoutRequests" pr
JOIN "AffiliateAccounts" aa ON pr."AffiliateAccountId" = aa."Id"
WHERE pr."Status" IN (1, 2)  -- Pending or Approved
  AND pr."RemainingAmount" < pr."RequestedAmount"  -- Partially settled
  AND pr."RemainingAmount" > 0
ORDER BY pr."RequestedAtUtc" ASC;

-- Expected: Shows all partially settled requests
-- Admin can use this to track settlement progress

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 7: Reconciliation Activity Log
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Track when requests were reconciled (approximation)
-- Note: Actual reconciliation time not stored, using PaidAtUtc as proxy

SELECT 
    pr."Id",
    pr."AffiliateAccountId",
    aa."ReferralCode",
    pr."RequestedAmount",
    pr."Status",
    pr."RequestedAtUtc",
    pr."ApprovedAtUtc",
    pr."PaidAtUtc",
    pr."PaymentReference",
    EXTRACT(EPOCH FROM (pr."PaidAtUtc" - pr."RequestedAtUtc")) / 86400 as days_to_settlement
FROM "AffiliatePayoutRequests" pr
JOIN "AffiliateAccounts" aa ON pr."AffiliateAccountId" = aa."Id"
WHERE pr."Status" = 4  -- Paid
  AND pr."PaymentReference" LIKE '%Order #%'  -- Legacy settlement reconciliation
ORDER BY pr."PaidAtUtc" DESC
LIMIT 50;

-- Expected: Shows recently reconciled requests via legacy settlement
-- PaymentReference contains "Order #<orderId>" for AF-38F reconciliations

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 8: Orphaned Paid Requests (No BalanceLedger Entry)
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Detect Paid requests without corresponding BalanceLedger withdrawal
-- This should NOT happen after AF-38E + AF-38F

SELECT 
    pr."Id",
    pr."AffiliateAccountId",
    aa."ReferralCode",
    pr."RequestedAmount",
    pr."Status",
    pr."PaidAtUtc",
    pr."PaymentReference"
FROM "AffiliatePayoutRequests" pr
JOIN "AffiliateAccounts" aa ON pr."AffiliateAccountId" = aa."Id"
WHERE pr."Status" = 4  -- Paid
  AND NOT EXISTS (
      SELECT 1
      FROM "AffiliateBalanceLedger" bl
      WHERE bl."ReferenceId" = pr."Id"
        AND bl."EntryType" = 1  -- Withdrawal
        AND bl."AffiliateAccountId" = pr."AffiliateAccountId"
  );

-- Expected: 0 rows (all Paid requests have BalanceLedger entries)
-- If rows found: Indicates pre-AF-38E data or reconciliation bug

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 9: Balance Consistency Check
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Verify Available balance matches expected calculation
-- Formula: Available = Approved + Adjustments + Withdrawals - Pending

WITH AffiliateBalances AS (
    SELECT 
        cl."AffiliateAccountId",
        -- Approved commissions
        SUM(CASE WHEN cl."EntryType" = 2 THEN cl."CommissionAmount" ELSE 0 END) as total_approved,
        -- Balance adjustments (bonuses, penalties)
        COALESCE((
            SELECT SUM(bl."Amount")
            FROM "AffiliateBalanceLedger" bl
            WHERE bl."AffiliateAccountId" = cl."AffiliateAccountId"
              AND bl."EntryType" != 1  -- Not Withdrawal
        ), 0) as total_adjustments,
        -- Withdrawals (already negative)
        COALESCE((
            SELECT SUM(bl."Amount")
            FROM "AffiliateBalanceLedger" bl
            WHERE bl."AffiliateAccountId" = cl."AffiliateAccountId"
              AND bl."EntryType" = 1  -- Withdrawal
        ), 0) as total_withdrawals,
        -- Pending requests
        COALESCE((
            SELECT SUM(pr."RemainingAmount")
            FROM "AffiliatePayoutRequests" pr
            WHERE pr."AffiliateAccountId" = cl."AffiliateAccountId"
              AND pr."Status" IN (1, 2)  -- Pending or Approved
        ), 0) as total_pending
    FROM "AffiliateCommissionLedger" cl
    GROUP BY cl."AffiliateAccountId"
)
SELECT 
    ab."AffiliateAccountId",
    aa."ReferralCode",
    ab.total_approved,
    ab.total_adjustments,
    ab.total_withdrawals,
    ab.total_pending,
    -- AF-38D formula
    GREATEST(0, 
        ab.total_approved + 
        ab.total_adjustments + 
        ab.total_withdrawals + 
        (-ab.total_pending)
    ) as calculated_available
FROM AffiliateBalances ab
JOIN "AffiliateAccounts" aa ON ab."AffiliateAccountId" = aa."Id"
ORDER BY calculated_available DESC;

-- Expected: All calculated_available values should be >= 0
-- Compare with GetAvailableBalanceAsync() results for validation

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 10: Concurrent Settlement Detection
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Detect potential concurrent settlement attempts
-- Logic: Multiple BalanceLedger entries created within 1 second for same affiliate

WITH ConcurrentSettlements AS (
    SELECT 
        "AffiliateAccountId",
        DATE_TRUNC('second', "RecordedAtUtc") as recorded_second,
        COUNT(*) as concurrent_count
    FROM "AffiliateBalanceLedger"
    WHERE "EntryType" = 1  -- Withdrawal
    GROUP BY "AffiliateAccountId", DATE_TRUNC('second', "RecordedAtUtc")
    HAVING COUNT(*) > 1
)
SELECT 
    cs."AffiliateAccountId",
    aa."ReferralCode",
    cs.recorded_second,
    cs.concurrent_count,
    bl."Id",
    bl."Amount",
    bl."ReferenceId",
    bl."Description"
FROM ConcurrentSettlements cs
JOIN "AffiliateAccounts" aa ON cs."AffiliateAccountId" = aa."Id"
JOIN "AffiliateBalanceLedger" bl 
    ON cs."AffiliateAccountId" = bl."AffiliateAccountId"
    AND DATE_TRUNC('second', bl."RecordedAtUtc") = cs.recorded_second
    AND bl."EntryType" = 1
ORDER BY cs.recorded_second DESC;

-- Expected: Few or no rows (concurrent settlements are rare)
-- If many rows: May indicate race condition or high admin activity

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 11: AF-38F Effectiveness Metrics
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Measure how many requests were auto-reconciled vs manually marked

SELECT 
    CASE 
        WHEN "PaymentReference" LIKE '%Order #%' THEN 'Auto-Reconciled (AF-38F)'
        WHEN "PaymentReference" IS NULL THEN 'Manual (No Reference)'
        ELSE 'Manual (With Reference)'
    END as settlement_type,
    COUNT(*) as request_count,
    SUM("RequestedAmount") as total_amount,
    AVG("RequestedAmount") as avg_amount,
    MIN("PaidAtUtc") as first_settlement,
    MAX("PaidAtUtc") as last_settlement
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4  -- Paid
  AND "PaidAtUtc" >= NOW() - INTERVAL '30 days'
GROUP BY settlement_type
ORDER BY request_count DESC;

-- Expected: Shows distribution of settlement methods
-- High "Auto-Reconciled" count indicates AF-38F is working

-- ───────────────────────────────────────────────────────────────────────────
-- QUERY 12: Leftover Settlement Detection
-- ───────────────────────────────────────────────────────────────────────────
-- Purpose: Find cases where settlement exceeded all open requests
-- Logic: BalanceLedger withdrawals without matching PayoutRequest reconciliation

WITH SettlementsByAffiliate AS (
    SELECT 
        bl."AffiliateAccountId",
        bl."RecordedAtUtc",
        ABS(bl."Amount") as settlement_amount,
        bl."ReferenceId",
        bl."Description"
    FROM "AffiliateBalanceLedger" bl
    WHERE bl."EntryType" = 1  -- Withdrawal
      AND bl."Description" LIKE '%Legacy settlement%'
),
RequestsByAffiliate AS (
    SELECT 
        pr."AffiliateAccountId",
        pr."RequestedAtUtc",
        pr."PaidAtUtc",
        pr."RequestedAmount",
        pr."RemainingAmount"
    FROM "AffiliatePayoutRequests" pr
    WHERE pr."Status" IN (1, 2, 4)  -- All active/paid requests
)
SELECT 
    s."AffiliateAccountId",
    aa."ReferralCode",
    s."RecordedAtUtc" as settlement_date,
    s.settlement_amount,
    COALESCE(SUM(r."RequestedAmount"), 0) as total_requests_at_time,
    s.settlement_amount - COALESCE(SUM(r."RequestedAmount"), 0) as potential_leftover
FROM SettlementsByAffiliate s
JOIN "AffiliateAccounts" aa ON s."AffiliateAccountId" = aa."Id"
LEFT JOIN RequestsByAffiliate r 
    ON s."AffiliateAccountId" = r."AffiliateAccountId"
    AND r."RequestedAtUtc" <= s."RecordedAtUtc"
    AND (r."PaidAtUtc" IS NULL OR r."PaidAtUtc" >= s."RecordedAtUtc")
GROUP BY s."AffiliateAccountId", aa."ReferralCode", s."RecordedAtUtc", s.settlement_amount
HAVING s.settlement_amount > COALESCE(SUM(r."RequestedAmount"), 0)
ORDER BY s."RecordedAtUtc" DESC;

-- Expected: Shows settlements that exceeded open requests
-- potential_leftover > 0 indicates leftover amount (valid scenario)

-- ═══════════════════════════════════════════════════════════════════════════
-- SUMMARY DASHBOARD QUERY
-- ═══════════════════════════════════════════════════════════════════════════
-- Purpose: Single query for overall AF-38F health check

SELECT 
    'Total Payout Requests' as metric,
    COUNT(*)::text as value
FROM "AffiliatePayoutRequests"

UNION ALL

SELECT 
    'Paid Requests',
    COUNT(*)::text
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4

UNION ALL

SELECT 
    'Partially Settled Requests',
    COUNT(*)::text
FROM "AffiliatePayoutRequests"
WHERE "Status" IN (1, 2)
  AND "RemainingAmount" < "RequestedAmount"

UNION ALL

SELECT 
    'Auto-Reconciled (Last 30 Days)',
    COUNT(*)::text
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4
  AND "PaymentReference" LIKE '%Order #%'
  AND "PaidAtUtc" >= NOW() - INTERVAL '30 days'

UNION ALL

SELECT 
    'Negative RemainingAmount (BUG)',
    COUNT(*)::text
FROM "AffiliatePayoutRequests"
WHERE "RemainingAmount" < 0

UNION ALL

SELECT 
    'Paid with Non-Zero Remaining (BUG)',
    COUNT(*)::text
FROM "AffiliatePayoutRequests"
WHERE "Status" = 4
  AND "RemainingAmount" != 0

UNION ALL

SELECT 
    'FIFO Violations (BUG)',
    COUNT(DISTINCT older."Id")::text
FROM "AffiliatePayoutRequests" older
JOIN "AffiliatePayoutRequests" newer 
    ON older."AffiliateAccountId" = newer."AffiliateAccountId"
    AND older."RequestedAtUtc" < newer."RequestedAtUtc"
WHERE older."Status" IN (1, 2)
  AND newer."Status" = 4
  AND older."RemainingAmount" > 0

ORDER BY metric;

-- Expected: All BUG metrics should be 0
-- Auto-Reconciled count should be > 0 if AF-38F is active

-- ═══════════════════════════════════════════════════════════════════════════
-- END OF VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════════════
