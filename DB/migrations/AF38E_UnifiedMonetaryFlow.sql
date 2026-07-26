-- ═══════════════════════════════════════════════════════════════════════════
-- AF-38E: Unified Monetary Flow Enforcement
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- PURPOSE:
-- Backfill BalanceLedger.Withdrawal entries for historical CommissionLedger.Paid
-- entries that were created before AF-38E (legacy settlement without monetary ledger).
--
-- ARCHITECTURAL PRINCIPLE:
-- - ALL monetary outflows MUST be recorded in AffiliateBalanceLedger
-- - CommissionLedger.Paid is lifecycle state only (NOT monetary deduction)
-- - BalanceLedger is the SINGLE source of monetary truth
--
-- WHAT THIS MIGRATION DOES:
-- 1. Identifies CommissionLedger.Paid entries without corresponding BalanceLedger entries
-- 2. Groups them by OrderId (legacy settlement was per-order)
-- 3. Creates BalanceLedger.Withdrawal entries for each order
-- 4. Ensures idempotency (skips if BalanceLedger entry already exists)
-- 5. Verifies data integrity after migration
--
-- SAFETY:
-- - Fully idempotent (can be run multiple times)
-- - Transaction-wrapped (all-or-nothing)
-- - Verification queries included
-- - Rollback script provided
--
-- ═══════════════════════════════════════════════════════════════════════════

DO $$
DECLARE
    v_paid_without_balance_count INT;
    v_orders_to_backfill INT;
    v_backfilled_count INT := 0;
    v_skipped_count INT := 0;
    v_order_record RECORD;
BEGIN
    RAISE NOTICE 'AF-38E: Starting Unified Monetary Flow Migration';
    RAISE NOTICE '═══════════════════════════════════════════════════════════';

    -- ═══════════════════════════════════════════════════════════════════════
    -- PHASE 1: AUDIT CURRENT STATE
    -- ═══════════════════════════════════════════════════════════════════════
    
    -- Count CommissionLedger.Paid entries without corresponding BalanceLedger entries
    SELECT COUNT(DISTINCT cl."OrderId")
    INTO v_paid_without_balance_count
    FROM "AffiliateCommissionLedger" cl
    WHERE cl."EntryType" = 3  -- Paid
      AND NOT EXISTS (
          SELECT 1
          FROM "AffiliateBalanceLedger" bl
          WHERE bl."ReferenceId" = cl."OrderId"
            AND bl."EntryType" = 1  -- Withdrawal
            AND bl."AffiliateAccountId" = cl."AffiliateAccountId"
      );

    RAISE NOTICE 'AF-38E: Found % orders with Paid entries but no BalanceLedger withdrawal',
        v_paid_without_balance_count;

    IF v_paid_without_balance_count = 0 THEN
        RAISE NOTICE 'AF-38E: No backfill needed. All Paid entries have corresponding BalanceLedger entries.';
        RAISE NOTICE 'AF-38E: Migration complete (nothing to do).';
        RETURN;
    END IF;

    -- ═══════════════════════════════════════════════════════════════════════
    -- PHASE 2: BACKFILL BALANCELEDGER ENTRIES
    -- ═══════════════════════════════════════════════════════════════════════

    RAISE NOTICE 'AF-38E: Starting backfill process...';

    -- Loop through each order that needs backfilling
    FOR v_order_record IN
        SELECT 
            cl."OrderId",
            cl."AffiliateAccountId",
            cl."StoreTenantId",
            SUM(cl."CommissionAmount") as total_amount,
            MAX(cl."Currency") as currency,
            MIN(cl."RecordedAtUtc") as earliest_paid_date
        FROM "AffiliateCommissionLedger" cl
        WHERE cl."EntryType" = 3  -- Paid
          AND NOT EXISTS (
              SELECT 1
              FROM "AffiliateBalanceLedger" bl
              WHERE bl."ReferenceId" = cl."OrderId"
                AND bl."EntryType" = 1  -- Withdrawal
                AND bl."AffiliateAccountId" = cl."AffiliateAccountId"
          )
        GROUP BY cl."OrderId", cl."AffiliateAccountId", cl."StoreTenantId"
    LOOP
        -- Double-check that BalanceLedger entry doesn't exist (race condition protection)
        IF EXISTS (
            SELECT 1
            FROM "AffiliateBalanceLedger"
            WHERE "ReferenceId" = v_order_record."OrderId"
              AND "EntryType" = 1  -- Withdrawal
              AND "AffiliateAccountId" = v_order_record."AffiliateAccountId"
        ) THEN
            v_skipped_count := v_skipped_count + 1;
            RAISE NOTICE 'AF-38E: Skipped Order % (BalanceLedger entry already exists)',
                v_order_record."OrderId";
            CONTINUE;
        END IF;

        -- Create BalanceLedger.Withdrawal entry
        INSERT INTO "AffiliateBalanceLedger" (
            "Id",
            "StoreTenantId",
            "TenantId",
            "AffiliateAccountId",
            "EntryType",
            "Amount",
            "Currency",
            "Description",
            "ReferenceId",
            "RecordedAtUtc",
            "CreatedAt",
            "IdempotencyKey"
        )
        VALUES (
            gen_random_uuid(),
            v_order_record."StoreTenantId",
            NULL,  -- Global affiliates have NULL TenantId
            v_order_record."AffiliateAccountId",
            1,  -- Withdrawal
            -v_order_record.total_amount,  -- Negative amount (money leaving)
            v_order_record.currency,
            'AF-38E: Backfilled legacy settlement - Order #' || v_order_record."OrderId"::text,
            v_order_record."OrderId",
            v_order_record.earliest_paid_date,  -- Use original payment date
            v_order_record.earliest_paid_date,
            'AFFILIATE_BALANCE:WITHDRAWAL:' || v_order_record."OrderId"::text
        );

        v_backfilled_count := v_backfilled_count + 1;

        IF v_backfilled_count % 100 = 0 THEN
            RAISE NOTICE 'AF-38E: Backfilled % orders...', v_backfilled_count;
        END IF;
    END LOOP;

    RAISE NOTICE 'AF-38E: Backfill complete';
    RAISE NOTICE 'AF-38E: Backfilled: % orders', v_backfilled_count;
    RAISE NOTICE 'AF-38E: Skipped: % orders (already had BalanceLedger entries)', v_skipped_count;

    -- ═══════════════════════════════════════════════════════════════════════
    -- PHASE 3: VERIFICATION
    -- ═══════════════════════════════════════════════════════════════════════

    RAISE NOTICE 'AF-38E: Running verification queries...';

    -- Verify: No Paid entries without BalanceLedger entries
    SELECT COUNT(DISTINCT cl."OrderId")
    INTO v_paid_without_balance_count
    FROM "AffiliateCommissionLedger" cl
    WHERE cl."EntryType" = 3  -- Paid
      AND NOT EXISTS (
          SELECT 1
          FROM "AffiliateBalanceLedger" bl
          WHERE bl."ReferenceId" = cl."OrderId"
            AND bl."EntryType" = 1  -- Withdrawal
            AND bl."AffiliateAccountId" = cl."AffiliateAccountId"
      );

    IF v_paid_without_balance_count > 0 THEN
        RAISE EXCEPTION 'AF-38E: VERIFICATION FAILED - Still have % orders with Paid entries but no BalanceLedger',
            v_paid_without_balance_count;
    END IF;

    RAISE NOTICE 'AF-38E: ✓ Verification passed - All Paid entries have corresponding BalanceLedger entries';

    -- ═══════════════════════════════════════════════════════════════════════
    -- PHASE 4: SUMMARY
    -- ═══════════════════════════════════════════════════════════════════════

    RAISE NOTICE '═══════════════════════════════════════════════════════════';
    RAISE NOTICE 'AF-38E: Migration Summary';
    RAISE NOTICE '═══════════════════════════════════════════════════════════';
    RAISE NOTICE 'Orders backfilled: %', v_backfilled_count;
    RAISE NOTICE 'Orders skipped: %', v_skipped_count;
    RAISE NOTICE 'Total processed: %', v_backfilled_count + v_skipped_count;
    RAISE NOTICE '═══════════════════════════════════════════════════════════';
    RAISE NOTICE 'AF-38E: ✓ Unified Monetary Flow Migration Complete';
    RAISE NOTICE '═══════════════════════════════════════════════════════════';

END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- POST-MIGRATION VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════════════

-- Query 1: Verify no orphaned Paid entries
SELECT 
    'Orphaned Paid Entries' as check_name,
    COUNT(DISTINCT cl."OrderId") as count,
    CASE 
        WHEN COUNT(DISTINCT cl."OrderId") = 0 THEN '✓ PASS'
        ELSE '✗ FAIL'
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

-- Query 2: Count backfilled entries
SELECT 
    'Backfilled Entries' as check_name,
    COUNT(*) as count
FROM "AffiliateBalanceLedger"
WHERE "Description" LIKE 'AF-38E: Backfilled legacy settlement%';

-- Query 3: Verify balance integrity per affiliate
SELECT 
    a."Id" as affiliate_id,
    a."DisplayName" as affiliate_name,
    COALESCE(SUM(CASE WHEN cl."EntryType" = 2 THEN cl."CommissionAmount" ELSE 0 END), 0) as approved_commissions,
    COALESCE(SUM(CASE WHEN cl."EntryType" = 3 THEN cl."CommissionAmount" ELSE 0 END), 0) as paid_commissions_lifecycle,
    COALESCE((SELECT SUM(bl."Amount") FROM "AffiliateBalanceLedger" bl 
              WHERE bl."AffiliateAccountId" = a."Id"), 0) as balance_ledger_total,
    COALESCE((SELECT COUNT(*) FROM "AffiliateBalanceLedger" bl 
              WHERE bl."AffiliateAccountId" = a."Id" AND bl."EntryType" = 1), 0) as withdrawal_count
FROM "AffiliateAccounts" a
LEFT JOIN "AffiliateCommissionLedger" cl ON a."Id" = cl."AffiliateAccountId"
WHERE a."IsDeleted" = false
GROUP BY a."Id", a."DisplayName"
HAVING COALESCE(SUM(CASE WHEN cl."EntryType" = 3 THEN cl."CommissionAmount" ELSE 0 END), 0) > 0
ORDER BY a."DisplayName";

-- Query 4: Detect any remaining inconsistencies
SELECT 
    'Consistency Check' as check_name,
    CASE 
        WHEN COUNT(*) = 0 THEN '✓ PASS - All affiliates have consistent ledgers'
        ELSE '✗ FAIL - ' || COUNT(*) || ' affiliates have inconsistent ledgers'
    END as status
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
