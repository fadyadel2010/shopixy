-- ═══════════════════════════════════════════════════════════════════════════
-- PHASE: COD Payment Lifecycle Architecture
-- Migration Script: Existing Orders Status Alignment
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- Purpose: Migrate existing orders to the new PaymentStatus enum values
-- 
-- OLD VALUES:
--   0 = Pending
--   1 = Authorized  
--   2 = Paid (DEPRECATED) -> now means "Collected"
--   3 = PartiallyPaid
--   4 = Failed
--   5 = Refunded
--   6 = PartiallyRefunded
--   7 = AwaitingVerification
--   8 = Rejected
--
-- NEW VALUES (additions):
--   2 = Collected (replaces/maps from Paid)
--   9 = Settled (new - money in bank)
--
-- MIGRATION RULES:
-- 1. IsPaid = true AND PaymentStatus = 2 (Paid) → PaymentStatus = 2 (Collected)
--    (No change needed since Collected = 2, same as Paid - semantic only)
-- 
-- 2. For orders that have settlement records, update to Settled (9)
--    This is OPTIONAL and depends on your settlement tracking
--
-- NOTE: This is a SAFE migration - it only touches the UpdatedAt field
--       to document that orders with status 2 are now "Collected" not "Paid"
-- 
-- ═══════════════════════════════════════════════════════════════════════════

-- Step 1: Verify current status distribution
SELECT 
    'Before Migration' AS "Phase",
    "PaymentStatus",
    CASE "PaymentStatus"
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Authorized'
        WHEN 2 THEN 'Paid/Collected'
        WHEN 3 THEN 'PartiallyPaid'
        WHEN 4 THEN 'Failed'
        WHEN 5 THEN 'Refunded'
        WHEN 6 THEN 'PartiallyRefunded'
        WHEN 7 THEN 'AwaitingVerification'
        WHEN 8 THEN 'Rejected'
        WHEN 9 THEN 'Settled'
        ELSE 'Unknown'
    END AS "StatusName",
    COUNT(*) AS "OrderCount"
FROM "Orders"
GROUP BY "PaymentStatus"
ORDER BY "PaymentStatus";

-- Step 2: Document semantic change (Paid → Collected)
-- This is a NO-OP technically since both are value 2
-- But we update the timestamp to document the migration was run
DO $$
DECLARE
    v_collected_count INTEGER;
BEGIN
    -- Step 2: Update PaymentStatus = 2 (Paid) to Collected
    -- Note: This is technically a no-op since both map to value 2
    -- But this documents the semantic change and updates timestamp
    UPDATE "Orders"
    SET "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE "PaymentStatus" = 2 AND "IsPaid" = true;
    
    GET DIAGNOSTICS v_collected_count = ROW_COUNT;
    RAISE NOTICE 'Step 2: Confirmed % orders with PaymentStatus = 2 (Paid → Collected semantic change)', v_collected_count;
    
    RAISE NOTICE '============================================';
    RAISE NOTICE 'Migration Complete:';
    RAISE NOTICE '  - Orders confirmed as Collected: %', v_collected_count;
    RAISE NOTICE '';
    RAISE NOTICE 'NOTE: Settlement migration (→ Settled) requires';
    RAISE NOTICE 'manual identification of orders with completed';
    RAISE NOTICE 'COD or gateway settlements. This is done via';
    RAISE NOTICE 'the Payment Lifecycle API going forward.';
    RAISE NOTICE '============================================';
END $$;

-- Step 3: Verify final status distribution
SELECT 
    'After Migration' AS "Phase",
    "PaymentStatus",
    CASE "PaymentStatus"
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Authorized'
        WHEN 2 THEN 'Collected'
        WHEN 3 THEN 'PartiallyPaid'
        WHEN 4 THEN 'Failed'
        WHEN 5 THEN 'Refunded'
        WHEN 6 THEN 'PartiallyRefunded'
        WHEN 7 THEN 'AwaitingVerification'
        WHEN 8 THEN 'Rejected'
        WHEN 9 THEN 'Settled'
        ELSE 'Unknown'
    END AS "StatusName",
    COUNT(*) AS "OrderCount"
FROM "Orders"
GROUP BY "PaymentStatus"
ORDER BY "PaymentStatus";

-- Step 4: Summary
SELECT 
    'Migration Summary' AS "Report",
    (SELECT COUNT(*) FROM "Orders" WHERE "PaymentStatus" = 0) AS "Pending",
    (SELECT COUNT(*) FROM "Orders" WHERE "PaymentStatus" = 2) AS "Collected",
    (SELECT COUNT(*) FROM "Orders" WHERE "PaymentStatus" = 9) AS "Settled",
    (SELECT COUNT(*) FROM "Orders" WHERE "PaymentStatus" IN (5, 6)) AS "Refunded",
    (SELECT COUNT(*) FROM "Orders" WHERE "PaymentStatus" IN (4, 8)) AS "FailedOrRejected";

-- ═══════════════════════════════════════════════════════════════════════════
-- OPTIONAL: Manual Settlement Migration
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- If you have a separate settlement tracking table or records, you can
-- manually update orders to Settled status:
--
-- UPDATE "Orders"
-- SET "PaymentStatus" = 9, -- Settled
--     "UpdatedAt" = CURRENT_TIMESTAMP
-- WHERE "Id" IN (SELECT order_id FROM your_settlement_table WHERE is_settled = true)
--   AND "PaymentStatus" = 2;
--
-- Going forward, settlements will be recorded via the Payment Lifecycle API
-- which creates proper ledger journal entries.
-- ═══════════════════════════════════════════════════════════════════════════
