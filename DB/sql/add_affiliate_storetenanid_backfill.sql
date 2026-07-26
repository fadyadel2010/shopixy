-- ═══════════════════════════════════════════════════════════════════════════════
-- AF-19C: BACKFILL StoreTenantId IN AffiliateCommissionLedger
-- 
-- CRITICAL: Run this BEFORE reprojecting MongoDB documents
-- 
-- This script backfills StoreTenantId from the Orders table for any ledger entries
-- that have NULL or Guid.Empty StoreTenantId.
-- 
-- IMPORTANT: AffiliateCommissionLedger has an immutability trigger that blocks UPDATEs.
-- We temporarily disable the trigger, run the backfill, then re-enable it.
-- ═══════════════════════════════════════════════════════════════════════════════

BEGIN;

-- Step 0: Disable immutability trigger temporarily
-- CRITICAL: This is a one-time data repair operation
ALTER TABLE "AffiliateCommissionLedger" DISABLE TRIGGER trg_affiliate_commission_ledger_immutable;

-- Step 1: Backfill StoreTenantId from Orders table
DO $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE "AffiliateCommissionLedger" acl
    SET "StoreTenantId" = o."TenantId"
    FROM "Orders" o
    WHERE acl."OrderId" = o."Id"
      AND (acl."StoreTenantId" IS NULL OR acl."StoreTenantId" = '00000000-0000-0000-0000-000000000000');
    
    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RAISE NOTICE 'Backfilled StoreTenantId for % entries', updated_count;
END $$;

-- Step 2: Verify no orphaned entries (entries without matching orders)
DO $$
DECLARE
    orphaned_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO orphaned_count
    FROM "AffiliateCommissionLedger" acl
    LEFT JOIN "Orders" o ON acl."OrderId" = o."Id"
    WHERE o."Id" IS NULL;
    
    IF orphaned_count > 0 THEN
        RAISE WARNING 'Found % orphaned AffiliateCommissionLedger entries without matching Orders', orphaned_count;
        -- Don't fail - just warn. These may be test data or deleted orders.
    END IF;
END $$;

-- Step 3: Verify all entries now have StoreTenantId
DO $$
DECLARE
    missing_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO missing_count
    FROM "AffiliateCommissionLedger"
    WHERE "StoreTenantId" IS NULL OR "StoreTenantId" = '00000000-0000-0000-0000-000000000000';
    
    IF missing_count > 0 THEN
        RAISE EXCEPTION 'Backfill failed: % entries still have NULL or empty StoreTenantId', missing_count;
    END IF;
    
    RAISE NOTICE 'Backfill successful: All entries have valid StoreTenantId';
END $$;

-- Step 4: Re-enable immutability trigger
ALTER TABLE "AffiliateCommissionLedger" ENABLE TRIGGER trg_affiliate_commission_ledger_immutable;

-- Step 5: Final verification
DO $$
BEGIN
    RAISE NOTICE 'Immutability trigger re-enabled - ledger is now protected again';
END $$;

COMMIT;

-- ═══════════════════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES (run after backfill)
-- ═══════════════════════════════════════════════════════════════════════════════

-- Count entries by StoreTenantId
SELECT 
    "StoreTenantId",
    COUNT(*) as entry_count,
    SUM("CommissionAmount") as total_commission
FROM "AffiliateCommissionLedger"
GROUP BY "StoreTenantId"
ORDER BY entry_count DESC;

-- Verify StoreTenantId matches Order TenantId
SELECT 
    COUNT(*) as mismatch_count
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
WHERE acl."StoreTenantId" != o."TenantId";
-- Should return 0

-- Show sample of backfilled entries
SELECT 
    acl."Id",
    acl."OrderId",
    acl."StoreTenantId",
    o."TenantId" as order_tenant_id,
    acl."EntryType",
    acl."CommissionAmount"
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
LIMIT 10;


-- ═══════════════════════════════════════════════════════════════════════════════
-- SAFETY VERIFICATION (run after script completes)
-- ═══════════════════════════════════════════════════════════════════════════════

-- Verify trigger is re-enabled
SELECT 
    tgname as trigger_name,
    tgenabled as enabled_status,
    CASE tgenabled
        WHEN 'O' THEN 'ENABLED'
        WHEN 'D' THEN 'DISABLED'
        ELSE 'UNKNOWN'
    END as status_description
FROM pg_trigger
WHERE tgname = 'trg_affiliate_commission_ledger_immutable';
-- Should show 'ENABLED'

-- Test immutability (should fail with error)
-- DO NOT RUN IN PRODUCTION - This is just a verification test
-- UPDATE "AffiliateCommissionLedger" SET "CommissionAmount" = "CommissionAmount" WHERE "Id" = (SELECT "Id" FROM "AffiliateCommissionLedger" LIMIT 1);
-- Expected: ERROR: AffiliateCommissionLedger is append-only. UPDATE and DELETE are forbidden.
