-- ============================================================================
-- AF-19B: REMOVE AFFILIATE TENANT BINDING (Complete Global Model)
-- Phase: Remove ALL tenant associations from affiliates
-- Date: 2026-02-24
-- ============================================================================
-- 
-- OBJECTIVE: Make affiliates truly global - no tenant binding whatsoever
-- 
-- CHANGES:
-- ✓ Set AffiliateAccount.TenantId to NULL (or sentinel GUID)
-- ✓ Set AffiliateCommissionLedger.TenantId to NULL (keep column for rollback)
-- ✓ Update idempotency keys to not use TenantId
-- ============================================================================

BEGIN;

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 0: Temporarily disable immutability trigger
-- ═══════════════════════════════════════════════════════════════════

DROP TRIGGER IF EXISTS trg_affiliate_commission_ledger_immutable 
    ON "AffiliateCommissionLedger";

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 1: Drop Foreign Key Constraints
-- ═══════════════════════════════════════════════════════════════════

-- Drop FK constraint from AffiliateAccounts to Tenants
ALTER TABLE "AffiliateAccounts" 
DROP CONSTRAINT IF EXISTS "FK_AffiliateAccounts_Tenants";

-- Drop FK constraint from AffiliateUsers to Tenants
ALTER TABLE "AffiliateUsers" 
DROP CONSTRAINT IF EXISTS "FK_AffiliateUsers_Tenants";

-- Drop FK constraint from AffiliateCommissionLedger to Tenants (if exists)
ALTER TABLE "AffiliateCommissionLedger" 
DROP CONSTRAINT IF EXISTS "FK_AffiliateCommissionLedger_Tenants";

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 2: Nullify AffiliateAccount.TenantId
-- ═══════════════════════════════════════════════════════════════════

-- Make TenantId nullable
ALTER TABLE "AffiliateAccounts" 
ALTER COLUMN "TenantId" DROP NOT NULL;

-- Set all existing affiliates to NULL (truly global)
UPDATE "AffiliateAccounts"
SET "TenantId" = NULL
WHERE "TenantId" IS NOT NULL;

COMMENT ON COLUMN "AffiliateAccounts"."TenantId" IS 
'AF-19B: DEPRECATED - Always NULL. Affiliates are global entities. Column kept for rollback safety only.';

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 3: Nullify AffiliateUsers.TenantId
-- ═══════════════════════════════════════════════════════════════════

-- Make TenantId nullable
ALTER TABLE "AffiliateUsers" 
ALTER COLUMN "TenantId" DROP NOT NULL;

-- Set all existing users to NULL (truly global)
UPDATE "AffiliateUsers"
SET "TenantId" = NULL
WHERE "TenantId" IS NOT NULL;

COMMENT ON COLUMN "AffiliateUsers"."TenantId" IS 
'AF-19B: DEPRECATED - Always NULL. Affiliates are global entities. Column kept for rollback safety only.';

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 4: Nullify AffiliateCommissionLedger.TenantId
-- ═══════════════════════════════════════════════════════════════════

-- Make TenantId nullable
ALTER TABLE "AffiliateCommissionLedger" 
ALTER COLUMN "TenantId" DROP NOT NULL;

-- Set all existing commission entries to NULL
-- StoreTenantId is the source of truth
UPDATE "AffiliateCommissionLedger"
SET "TenantId" = NULL
WHERE "TenantId" IS NOT NULL;

COMMENT ON COLUMN "AffiliateCommissionLedger"."TenantId" IS 
'AF-19B: DEPRECATED - Always NULL. Use StoreTenantId instead. Column kept for rollback safety only.';

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 5: Update Idempotency Keys (Remove TenantId)
-- ═══════════════════════════════════════════════════════════════════

-- Update existing idempotency keys to remove TenantId
-- Old format: AFFILIATE_COMM:EARNED:{TenantId}:{OrderItemId}
-- New format: AFFILIATE_COMM:EARNED:{OrderItemId}

UPDATE "AffiliateCommissionLedger"
SET "IdempotencyKey" = 
    CASE "EntryType"
        WHEN 0 THEN 'AFFILIATE_COMM:EARNED:' || "OrderItemId"::text
        WHEN 1 THEN 'AFFILIATE_COMM:REVERSED:' || "OrderItemId"::text
        WHEN 2 THEN 'AFFILIATE_COMM:APPROVED:' || "OrderItemId"::text
        WHEN 3 THEN 'AFFILIATE_COMM:PAID:' || "OrderItemId"::text
    END
WHERE "IdempotencyKey" LIKE 'AFFILIATE_COMM:%:%:%';

-- Drop old unique constraint (if exists)
ALTER TABLE "AffiliateCommissionLedger"
DROP CONSTRAINT IF EXISTS "UQ_AffiliateCommLedger_Idempotency";

-- Create new unique constraint without TenantId
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AffiliateCommLedger_Idempotency_Global"
    ON "AffiliateCommissionLedger"("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;

-- ═══════════════════════════════════════════════════════════════════
-- VERIFICATION
-- ═══════════════════════════════════════════════════════════════════

DO $$
DECLARE
    affiliate_with_tenant INTEGER;
    user_with_tenant INTEGER;
    commission_with_tenant INTEGER;
BEGIN
    -- Verify all affiliates have NULL TenantId
    SELECT COUNT(*) INTO affiliate_with_tenant
    FROM "AffiliateAccounts"
    WHERE "TenantId" IS NOT NULL;
    
    IF affiliate_with_tenant > 0 THEN
        RAISE EXCEPTION 'AF-19B VERIFICATION FAILED: % affiliates still have TenantId', affiliate_with_tenant;
    END IF;
    
    -- Verify all users have NULL TenantId
    SELECT COUNT(*) INTO user_with_tenant
    FROM "AffiliateUsers"
    WHERE "TenantId" IS NOT NULL;
    
    IF user_with_tenant > 0 THEN
        RAISE EXCEPTION 'AF-19B VERIFICATION FAILED: % users still have TenantId', user_with_tenant;
    END IF;
    
    -- Verify all commissions have NULL TenantId
    SELECT COUNT(*) INTO commission_with_tenant
    FROM "AffiliateCommissionLedger"
    WHERE "TenantId" IS NOT NULL;
    
    IF commission_with_tenant > 0 THEN
        RAISE EXCEPTION 'AF-19B VERIFICATION FAILED: % commission entries still have TenantId', commission_with_tenant;
    END IF;
    
    RAISE NOTICE 'AF-19B: All affiliates, users, and commissions are now truly global (TenantId = NULL)';
END $$;$
DECLARE
    affiliate_with_tenant INTEGER;
    commission_with_tenant INTEGER;
BEGIN
    -- Verify all affiliates have NULL TenantId
    SELECT COUNT(*) INTO affiliate_with_tenant
    FROM "AffiliateAccounts"
    WHERE "TenantId" IS NOT NULL;
    
    IF affiliate_with_tenant > 0 THEN
        RAISE EXCEPTION 'AF-19B VERIFICATION FAILED: % affiliates still have TenantId', affiliate_with_tenant;
    END IF;
    
    -- Verify all commissions have NULL TenantId
    SELECT COUNT(*) INTO commission_with_tenant
    FROM "AffiliateCommissionLedger"
    WHERE "TenantId" IS NOT NULL;
    
    IF commission_with_tenant > 0 THEN
        RAISE EXCEPTION 'AF-19B VERIFICATION FAILED: % commission entries still have TenantId', commission_with_tenant;
    END IF;
    
    RAISE NOTICE 'AF-19B: All affiliates and commissions are now truly global (TenantId = NULL)';
END $$;

-- ═══════════════════════════════════════════════════════════════════
-- PHASE 6: Re-enable immutability trigger
-- ═══════════════════════════════════════════════════════════════════

CREATE TRIGGER trg_affiliate_commission_ledger_immutable
    BEFORE UPDATE OR DELETE ON "AffiliateCommissionLedger"
    FOR EACH ROW
    EXECUTE FUNCTION fn_affiliate_commission_ledger_immutable();

COMMIT;

-- ═══════════════════════════════════════════════════════════════════
-- SUMMARY
-- ═══════════════════════════════════════════════════════════════════
-- 
-- After this migration:
-- ✓ AffiliateAccount.TenantId = NULL (global)
-- ✓ AffiliateCommissionLedger.TenantId = NULL (use StoreTenantId)
-- ✓ Idempotency keys no longer include TenantId
-- ✓ Affiliates are truly global entities
-- ✓ Commission queries use StoreTenantId only
-- ═══════════════════════════════════════════════════════════════════
