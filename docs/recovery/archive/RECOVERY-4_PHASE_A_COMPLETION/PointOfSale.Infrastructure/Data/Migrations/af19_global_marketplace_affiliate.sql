-- ============================================================================
-- AF-19: GLOBAL MARKETPLACE AFFILIATE REFACTOR
-- Phase: Database Migration (Safe, Zero-Downtime)
-- Date: 2026-02-24
-- ============================================================================
-- 
-- OBJECTIVE: Transform tenant-bound affiliate model into global marketplace model
-- 
-- INVARIANTS AFTER MIGRATION:
-- ✓ AffiliateAccount.TenantId becomes legacy (ignored, not dropped)
-- ✓ ReferralCode is globally unique (not per-tenant)
-- ✓ Commission ledger records StoreTenantId (where sale happened)
-- ✓ Existing affiliates continue working (backward compatible)
-- ✓ Zero downtime migration
-- ============================================================================

BEGIN;

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 1.1: Add StoreTenantId to Commission Ledger
-- ═══════════════════════════════════════════════════════════════════

-- Step 1: Add column as nullable
ALTER TABLE "AffiliateCommissionLedger" 
ADD COLUMN IF NOT EXISTS "StoreTenantId" uuid NULL;

-- Step 2: Backfill from Orders.TenantId
UPDATE "AffiliateCommissionLedger" acl
SET "StoreTenantId" = o."TenantId"
FROM "Orders" o
WHERE acl."OrderId" = o."Id"
  AND acl."StoreTenantId" IS NULL;

-- Step 3: Make required (safe after backfill)
ALTER TABLE "AffiliateCommissionLedger" 
ALTER COLUMN "StoreTenantId" SET NOT NULL;

-- Step 4: Add index for store-level queries
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_StoreTenantId"
    ON "AffiliateCommissionLedger"("StoreTenantId");

-- Step 5: Add composite index for affiliate cross-store queries
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_Affiliate_Store"
    ON "AffiliateCommissionLedger"("AffiliateAccountId", "StoreTenantId");

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 1.2: Make ReferralCode Globally Unique
-- ═══════════════════════════════════════════════════════════════════

-- Step 1: Check for collisions (should be none if codes are well-distributed)
DO $$
DECLARE
    collision_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO collision_count
    FROM (
        SELECT "ReferralCode", COUNT(*) as cnt
        FROM "AffiliateAccounts"
        WHERE NOT "IsDeleted"
        GROUP BY "ReferralCode"
        HAVING COUNT(*) > 1
    ) collisions;
    
    IF collision_count > 0 THEN
        RAISE EXCEPTION 'AF-19 MIGRATION BLOCKED: % ReferralCode collisions detected. Run collision resolution script first.', collision_count;
    END IF;
    
    RAISE NOTICE 'AF-19: No ReferralCode collisions detected. Proceeding with global uniqueness.';
END $$;

-- Step 2: Drop tenant-scoped unique constraint
ALTER TABLE "AffiliateAccounts" 
DROP CONSTRAINT IF EXISTS "UQ_AffiliateAccounts_Tenant_ReferralCode";

-- Step 3: Add global unique constraint (idempotent)
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
-- PHASE 1.3: Keep TenantId Column BUT Mark as Legacy
-- ═══════════════════════════════════════════════════════════════════

-- DO NOT DROP TenantId yet. It becomes legacy/ignored.
-- Domain model will stop using it, but column remains for:
-- 1. Backward compatibility during migration
-- 2. Audit trail (where affiliate originally registered)
-- 3. Safe rollback path if needed

COMMENT ON COLUMN "AffiliateAccounts"."TenantId" IS 
'AF-19: LEGACY FIELD - No longer enforced. Affiliates are now global. Kept for audit trail and rollback safety.';

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 1.4: Update AffiliatePromotionLinks for Global Model
-- ═══════════════════════════════════════════════════════════════════

-- PromotionLinks already have globally unique ReferralCode (AF-15)
-- No schema changes needed, but add comment for clarity

COMMENT ON TABLE "AffiliatePromotionLinks" IS 
'AF-19: Store-scoped promotion links. TenantId = StoreId where product is promoted. AffiliateUserId is global.';

-- ═══════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════

-- Verify StoreTenantId backfill
DO $$
DECLARE
    null_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO null_count
    FROM "AffiliateCommissionLedger"
    WHERE "StoreTenantId" IS NULL;
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-19 VERIFICATION FAILED: % commission entries have NULL StoreTenantId', null_count;
    END IF;
    
    RAISE NOTICE 'AF-19: StoreTenantId backfill verified. All % entries have StoreTenantId.', 
        (SELECT COUNT(*) FROM "AffiliateCommissionLedger");
END $$;

-- Verify global ReferralCode uniqueness
DO $$
DECLARE
    unique_count INTEGER;
    total_count INTEGER;
BEGIN
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
    
    RAISE NOTICE 'AF-19: Global ReferralCode uniqueness verified. % unique codes.', unique_count;
END $$;

COMMIT;

-- ═══════════════════════════════════════════════════════════════════
-- ROLLBACK SCRIPT (if needed)
-- ═══════════════════════════════════════════════════════════════════
-- 
-- BEGIN;
-- 
-- -- Restore tenant-scoped uniqueness
-- ALTER TABLE "AffiliateAccounts" 
-- DROP CONSTRAINT IF EXISTS "UQ_AffiliateAccounts_ReferralCode";
-- 
-- ALTER TABLE "AffiliateAccounts" 
-- ADD CONSTRAINT "UQ_AffiliateAccounts_Tenant_ReferralCode" 
-- UNIQUE ("TenantId", "ReferralCode");
-- 
-- -- Remove StoreTenantId (data loss!)
-- ALTER TABLE "AffiliateCommissionLedger" 
-- DROP COLUMN IF EXISTS "StoreTenantId";
-- 
-- COMMIT;
-- ═══════════════════════════════════════════════════════════════════
