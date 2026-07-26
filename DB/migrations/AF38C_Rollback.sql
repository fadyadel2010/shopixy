-- =====================================================================
-- AF-38C: Dual-Ledger Architecture Rollback Script
-- =====================================================================
-- WARNING: This rollback script should ONLY be used if the migration
-- failed or if you need to revert to the hybrid model.
-- =====================================================================

BEGIN;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 1: Restore Payout Entries to CommissionLedger
-- ═════════════════════════════════════════════════════════════════════

-- Move Withdrawal entries back to CommissionLedger as Payout entries
INSERT INTO "AffiliateCommissionLedger" (
    "Id",
    "TenantId",
    "StoreTenantId",
    "AttributionId",
    "AffiliateAccountId",
    "OrderId",
    "OrderItemId",
    "BaseAmount",
    "CommissionRate",
    "CommissionAmount",
    "Currency",
    "EntryType",
    "OriginalEntryId",
    "IdempotencyKey",
    "RecordedAtUtc",
    "CreatedAt",
    "CreatedBy",
    "PayoutRequestId"
)
SELECT 
    "Id",
    "TenantId",
    "StoreTenantId",
    NULL AS "AttributionId",  -- Was NULL for Payout entries
    "AffiliateAccountId",
    NULL AS "OrderId",  -- Was NULL for Payout entries
    NULL AS "OrderItemId",  -- Was NULL for Payout entries
    ABS("Amount") AS "BaseAmount",
    0 AS "CommissionRate",
    ABS("Amount") AS "CommissionAmount",
    "Currency",
    4 AS "EntryType",  -- Payout
    NULL AS "OriginalEntryId",
    REPLACE("IdempotencyKey", 'AFFILIATE_BALANCE:WITHDRAWAL:', 'AFFILIATE_PAYOUT:') AS "IdempotencyKey",
    "RecordedAtUtc",
    "CreatedAt",
    "CreatedBy",
    "ReferenceId" AS "PayoutRequestId"
FROM "AffiliateBalanceLedger"
WHERE "EntryType" = 1  -- Withdrawal
  AND "Description" LIKE 'Migrated from CommissionLedger%'
ON CONFLICT ("IdempotencyKey") DO NOTHING;

-- Log restoration count
DO $$
DECLARE
    restored_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO restored_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "EntryType" = 4;
    
    RAISE NOTICE 'AF-38C Rollback: Restored % Payout entries to CommissionLedger', restored_count;
END $$;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 2: Delete Migrated Entries from BalanceLedger
-- ═════════════════════════════════════════════════════════════════════

-- Drop the immutability trigger temporarily
DROP TRIGGER IF EXISTS trg_prevent_balance_ledger_mutation ON "AffiliateBalanceLedger";

-- Delete migrated entries
DELETE FROM "AffiliateBalanceLedger" 
WHERE "Description" LIKE 'Migrated from CommissionLedger%';

-- Recreate the trigger
CREATE TRIGGER trg_prevent_balance_ledger_mutation
    BEFORE UPDATE OR DELETE ON "AffiliateBalanceLedger"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_balance_ledger_mutation();

-- ═════════════════════════════════════════════════════════════════════
-- STEP 3: Restore Nullable Constraints on CommissionLedger
-- ═════════════════════════════════════════════════════════════════════

-- Make AttributionId nullable again
ALTER TABLE "AffiliateCommissionLedger" 
    ALTER COLUMN "AttributionId" DROP NOT NULL;

-- Make OrderId nullable again
ALTER TABLE "AffiliateCommissionLedger" 
    ALTER COLUMN "OrderId" DROP NOT NULL;

-- Make OrderItemId nullable again
ALTER TABLE "AffiliateCommissionLedger" 
    ALTER COLUMN "OrderItemId" DROP NOT NULL;

-- Add back PayoutRequestId column
ALTER TABLE "AffiliateCommissionLedger" 
    ADD COLUMN IF NOT EXISTS "PayoutRequestId" UUID NULL;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 4: Restore Conditional Constraints
-- ═════════════════════════════════════════════════════════════════════

-- Drop strict rate constraint
ALTER TABLE "AffiliateCommissionLedger" 
    DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_Rate";

-- Restore conditional rate constraint (allow Payout with rate 0)
ALTER TABLE "AffiliateCommissionLedger" 
    ADD CONSTRAINT "CK_AffiliateCommLedger_Rate" 
    CHECK (("CommissionRate" > 0 AND "CommissionRate" <= 0.50) OR ("EntryType" = 4 AND "CommissionRate" = 0));

-- Drop strict EntryType constraint
ALTER TABLE "AffiliateCommissionLedger" 
    DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_EntryType";

-- Restore EntryType constraint with Payout(4)
ALTER TABLE "AffiliateCommissionLedger" 
    ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType" 
    CHECK ("EntryType" IN (0, 1, 2, 3, 4));

-- Drop strict reversal reference constraint
ALTER TABLE "AffiliateCommissionLedger" 
    DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_ReversalRef";

-- Restore conditional reversal reference constraint (allow Payout with NULL OriginalEntryId)
ALTER TABLE "AffiliateCommissionLedger" 
    ADD CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" 
    CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" IN (0, 4) AND "OriginalEntryId" IS NULL));

-- ═════════════════════════════════════════════════════════════════════
-- STEP 5: Verification
-- ═════════════════════════════════════════════════════════════════════

DO $$
DECLARE
    payout_count INTEGER;
    balance_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO payout_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "EntryType" = 4;
    
    SELECT COUNT(*) INTO balance_count 
    FROM "AffiliateBalanceLedger" 
    WHERE "Description" LIKE 'Migrated from CommissionLedger%';
    
    IF payout_count > 0 AND balance_count = 0 THEN
        RAISE NOTICE 'AF-38C Rollback: ✓ Successfully rolled back (% Payout entries restored)', payout_count;
    ELSE
        RAISE WARNING 'AF-38C Rollback: Verification failed (Payout: %, BalanceLedger: %)', payout_count, balance_count;
    END IF;
END $$;

COMMIT;

-- ═════════════════════════════════════════════════════════════════════
-- AF-38C Rollback Complete
-- ═════════════════════════════════════════════════════════════════════
-- IMPORTANT: After rollback, you must also revert the C# code changes:
-- 1. Restore AffiliateCommissionLedger.PayoutRequestId property
-- 2. Restore nullable FKs (AttributionId?, OrderId?, OrderItemId?)
-- 3. Restore CreatePayout() factory method
-- 4. Restore CommissionEntryType.Payout enum value
-- 5. Revert AffiliatePayoutService to use CommissionLedger
-- ═════════════════════════════════════════════════════════════════════
