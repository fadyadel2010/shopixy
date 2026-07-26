-- ═══════════════════════════════════════════════════════════════════════════
-- AF-38E: Unified Monetary Flow - ROLLBACK
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- PURPOSE:
-- Rollback AF-38E migration by removing backfilled BalanceLedger entries.
--
-- WARNING:
-- - Only removes entries created by AF-38E migration (identified by description)
-- - Does NOT remove BalanceLedger entries created by actual withdrawal operations
-- - Safe to run if you need to revert the migration
--
-- ═══════════════════════════════════════════════════════════════════════════

DO $$
DECLARE
    v_deleted_count INT;
BEGIN
    RAISE NOTICE 'AF-38E: Starting Rollback';
    RAISE NOTICE '═══════════════════════════════════════════════════════════';

    -- Delete only backfilled entries (identified by description)
    DELETE FROM "AffiliateBalanceLedger"
    WHERE "Description" LIKE 'AF-38E: Backfilled legacy settlement%';

    GET DIAGNOSTICS v_deleted_count = ROW_COUNT;

    RAISE NOTICE 'AF-38E: Deleted % backfilled BalanceLedger entries', v_deleted_count;
    RAISE NOTICE '═══════════════════════════════════════════════════════════';
    RAISE NOTICE 'AF-38E: ✓ Rollback Complete';
    RAISE NOTICE '═══════════════════════════════════════════════════════════';

END $$;

-- Verification: Check that backfilled entries are gone
SELECT 
    'Backfilled Entries Remaining' as check_name,
    COUNT(*) as count,
    CASE 
        WHEN COUNT(*) = 0 THEN '✓ PASS - All backfilled entries removed'
        ELSE '✗ FAIL - Still have backfilled entries'
    END as status
FROM "AffiliateBalanceLedger"
WHERE "Description" LIKE 'AF-38E: Backfilled legacy settlement%';
