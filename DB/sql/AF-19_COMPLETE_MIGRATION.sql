-- ============================================================================
-- AF-19: Complete Migration (Idempotent - Safe to Re-run)
-- This script completes any missing steps from the original migration
-- ============================================================================

BEGIN;

-- ═══════════════════════════════════════════════════════════════════
-- STEP 1: Ensure StoreTenantId column exists and is backfilled
-- ═══════════════════════════════════════════════════════════════════

-- Add column if missing (already exists, will skip)
ALTER TABLE "AffiliateCommissionLedger" 
ADD COLUMN IF NOT EXISTS "StoreTenantId" uuid NULL;

-- Backfill any NULL values
UPDATE "AffiliateCommissionLedger" acl
SET "StoreTenantId" = o."TenantId"
FROM "Orders" o
WHERE acl."OrderId" = o."Id"
  AND acl."StoreTenantId" IS NULL;

-- Make NOT NULL if not already
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AffiliateCommissionLedger' 
        AND column_name = 'StoreTenantId'
        AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" 
        ALTER COLUMN "StoreTenantId" SET NOT NULL;
        RAISE NOTICE 'AF-19: StoreTenantId set to NOT NULL.';
    ELSE
        RAISE NOTICE 'AF-19: StoreTenantId already NOT NULL - skipping.';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════
-- STEP 2: Ensure indexes exist
-- ═══════════════════════════════════════════════════════════════════

CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_StoreTenantId"
    ON "AffiliateCommissionLedger"("StoreTenantId");

CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_Affiliate_Store"
    ON "AffiliateCommissionLedger"("AffiliateAccountId", "StoreTenantId");

-- ═══════════════════════════════════════════════════════════════════
-- STEP 3: Ensure global ReferralCode uniqueness
-- ═══════════════════════════════════════════════════════════════════

-- Drop old tenant-scoped constraint if exists
ALTER TABLE "AffiliateAccounts" 
DROP CONSTRAINT IF EXISTS "UQ_AffiliateAccounts_Tenant_ReferralCode";

-- Add global unique constraint (idempotent)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'UQ_AffiliateAccounts_ReferralCode'
    ) THEN
        ALTER TABLE "AffiliateAccounts" 
        ADD CONSTRAINT "UQ_AffiliateAccounts_ReferralCode" 
        UNIQUE ("ReferralCode");
        RAISE NOTICE 'AF-19: Global ReferralCode uniqueness constraint created.';
    ELSE
        RAISE NOTICE 'AF-19: Global ReferralCode uniqueness constraint already exists - skipping.';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════
-- STEP 4: Add comments for documentation
-- ═══════════════════════════════════════════════════════════════════

COMMENT ON COLUMN "AffiliateAccounts"."TenantId" IS 
'AF-19: LEGACY FIELD - No longer enforced. Affiliates are now global. Kept for audit trail and rollback safety.';

COMMENT ON TABLE "AffiliatePromotionLinks" IS 
'AF-19: Store-scoped promotion links. TenantId = StoreId where product is promoted. AffiliateUserId is global.';

COMMENT ON COLUMN "AffiliateCommissionLedger"."StoreTenantId" IS
'AF-19: The TenantId of the STORE where the sale happened. This is the store that owes the commission.';

-- ═══════════════════════════════════════════════════════════════════
-- VERIFICATION
-- ═══════════════════════════════════════════════════════════════════

DO $$
DECLARE
    null_count INTEGER;
    collision_count INTEGER;
    unique_count INTEGER;
    total_count INTEGER;
BEGIN
    -- Verify StoreTenantId backfill
    SELECT COUNT(*) INTO null_count
    FROM "AffiliateCommissionLedger"
    WHERE "StoreTenantId" IS NULL;
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-19 VERIFICATION FAILED: % commission entries have NULL StoreTenantId', null_count;
    END IF;
    
    RAISE NOTICE 'AF-19: ✅ StoreTenantId backfill verified. All % entries have StoreTenantId.', 
        (SELECT COUNT(*) FROM "AffiliateCommissionLedger");
    
    -- Verify global ReferralCode uniqueness
    SELECT COUNT(DISTINCT "ReferralCode") INTO unique_count
    FROM "AffiliateAccounts"
    WHERE NOT "IsDeleted";
    
    SELECT COUNT(*) INTO total_count
    FROM "AffiliateAccounts"
    WHERE NOT "IsDeleted";
    
    IF unique_count != total_count THEN
        RAISE EXCEPTION 'AF-19 VERIFICATION FAILED: ReferralCode not globally unique. Unique: %, Total: %', 
            unique_count, total_count;
    END IF;
    
    RAISE NOTICE 'AF-19: ✅ Global ReferralCode uniqueness verified. % unique codes.', unique_count;
    
    RAISE NOTICE 'AF-19: ✅ Migration completed successfully!';
END $$;

COMMIT;

-- ═══════════════════════════════════════════════════════════════════
-- FINAL HEALTH CHECK
-- ═══════════════════════════════════════════════════════════════════

SELECT 
    'AF-19 Migration Status' as check_name,
    '✅ COMPLETE' as status,
    COUNT(*) as total_commission_entries,
    COUNT(DISTINCT "AffiliateAccountId") as unique_affiliates,
    COUNT(DISTINCT "StoreTenantId") as unique_stores
FROM "AffiliateCommissionLedger";
