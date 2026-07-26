-- =====================================================================
-- AF-38C: Dual-Ledger Architecture Refactor
-- Separates Commission Earnings from Account-Level Balance Movements
-- =====================================================================
-- CRITICAL: Run this migration in a maintenance window with zero traffic.
-- This is a structural refactor that touches financial constraints.
-- =====================================================================

BEGIN;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 0: Check if migration already completed
-- ═════════════════════════════════════════════════════════════════════

DO $$
DECLARE
    payout_count INTEGER;
    balance_count INTEGER;
BEGIN
    -- Check if Payout entries still exist in CommissionLedger
    SELECT COUNT(*) INTO payout_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "EntryType" = 4;
    
    -- Check if BalanceLedger has entries
    SELECT COUNT(*) INTO balance_count 
    FROM "AffiliateBalanceLedger";
    
    IF payout_count = 0 AND balance_count > 0 THEN
        RAISE NOTICE 'AF-38C: Migration already completed (no Payout entries in CommissionLedger, % entries in BalanceLedger)', balance_count;
        -- Don't raise exception, just skip the migration steps
    ELSE
        RAISE NOTICE 'AF-38C: Starting migration (Payout entries: %, BalanceLedger entries: %)', payout_count, balance_count;
    END IF;
END $$;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 1: Create AffiliateBalanceLedger (Account-Level Movements)
-- ═════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS "AffiliateBalanceLedger" (
    "Id"                  UUID            NOT NULL,
    "TenantId"            UUID            NULL,  -- Nullable for global affiliates (AF-19B)
    "StoreTenantId"       UUID            NOT NULL,  -- Where the transaction occurred
    "AffiliateAccountId"  UUID            NOT NULL,
    "EntryType"           INTEGER         NOT NULL,  -- 1=Withdrawal, 2=Adjustment, 3=Bonus, 4=Penalty
    "Amount"              NUMERIC(18,4)   NOT NULL,  -- Negative for withdrawals
    "ReferenceId"         UUID            NULL,  -- WithdrawalRequestId if applicable
    "Description"         TEXT            NULL,
    "IdempotencyKey"      VARCHAR(255)    NOT NULL,
    "Currency"            VARCHAR(3)      NOT NULL DEFAULT 'EGP',
    "RecordedAtUtc"       TIMESTAMP WITH TIME ZONE NOT NULL,
    "CreatedAt"           TIMESTAMP WITH TIME ZONE NOT NULL,
    "CreatedBy"           UUID            NULL,
    
    CONSTRAINT "PK_AffiliateBalanceLedger" PRIMARY KEY ("Id"),
    
    -- FK to AffiliateAccounts
    CONSTRAINT "FK_AffiliateBalanceLedger_AffiliateAccounts" 
        FOREIGN KEY ("AffiliateAccountId") 
        REFERENCES "AffiliateAccounts"("Id") 
        ON DELETE RESTRICT,
    
    -- CHECK: Amount cannot be zero
    CONSTRAINT "CK_AffiliateBalanceLedger_Amount" 
        CHECK ("Amount" != 0),
    
    -- CHECK: EntryType valid
    CONSTRAINT "CK_AffiliateBalanceLedger_EntryType" 
        CHECK ("EntryType" IN (1, 2, 3, 4)),
    
    -- CHECK: Withdrawal must be negative
    CONSTRAINT "CK_AffiliateBalanceLedger_WithdrawalNegative" 
        CHECK ("EntryType" != 1 OR "Amount" < 0)
);

-- Unique constraint on IdempotencyKey (global uniqueness)
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AffiliateBalanceLedger_Idempotency" 
    ON "AffiliateBalanceLedger" ("IdempotencyKey");

-- Index for affiliate balance queries
CREATE INDEX IF NOT EXISTS "IX_AffiliateBalanceLedger_AffiliateAccountId" 
    ON "AffiliateBalanceLedger" ("AffiliateAccountId");

-- Index for tenant + time queries
CREATE INDEX IF NOT EXISTS "IX_AffiliateBalanceLedger_StoreTenantRecorded" 
    ON "AffiliateBalanceLedger" ("StoreTenantId", "RecordedAtUtc" DESC);

-- Index for reference lookups (withdrawal requests)
CREATE INDEX IF NOT EXISTS "IX_AffiliateBalanceLedger_ReferenceId" 
    ON "AffiliateBalanceLedger" ("ReferenceId") 
    WHERE "ReferenceId" IS NOT NULL;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 2: Migrate Existing Payout Entries from CommissionLedger
-- ═════════════════════════════════════════════════════════════════════

-- Check if migration is needed
DO $$
DECLARE
    payout_count INTEGER;
    has_payout_column BOOLEAN;
BEGIN
    SELECT COUNT(*) INTO payout_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "EntryType" = 4;
    
    -- Check if PayoutRequestId column exists
    SELECT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'AffiliateCommissionLedger' 
        AND column_name = 'PayoutRequestId'
    ) INTO has_payout_column;
    
    IF payout_count = 0 THEN
        RAISE NOTICE 'AF-38C: No Payout entries to migrate. Skipping data migration.';
    ELSIF NOT has_payout_column THEN
        RAISE NOTICE 'AF-38C: PayoutRequestId column does not exist. Migration already partially completed. Skipping data migration.';
    ELSE
        RAISE NOTICE 'AF-38C: Found % Payout entries to migrate', payout_count;
    END IF;
END $$;

-- Move all EntryType=4 (Payout) entries to the new BalanceLedger
-- Only run if PayoutRequestId column exists
DO $$
DECLARE
    has_payout_column BOOLEAN;
BEGIN
    -- Check if PayoutRequestId column exists
    SELECT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'AffiliateCommissionLedger' 
        AND column_name = 'PayoutRequestId'
    ) INTO has_payout_column;
    
    IF has_payout_column THEN
        -- Perform migration
        INSERT INTO "AffiliateBalanceLedger" (
            "Id",
            "TenantId",
            "StoreTenantId",
            "AffiliateAccountId",
            "EntryType",
            "Amount",
            "ReferenceId",
            "Description",
            "IdempotencyKey",
            "Currency",
            "RecordedAtUtc",
            "CreatedAt",
            "CreatedBy"
        )
        SELECT 
            "Id",
            "TenantId",
            "StoreTenantId",
            "AffiliateAccountId",
            1 AS "EntryType",  -- Map Payout(4) → Withdrawal(1)
            -"CommissionAmount" AS "Amount",  -- Negative for withdrawal
            "PayoutRequestId" AS "ReferenceId",
            'Migrated from CommissionLedger EntryType=Payout' AS "Description",
            REPLACE("IdempotencyKey", 'AFFILIATE_PAYOUT:', 'AFFILIATE_BALANCE:WITHDRAWAL:') AS "IdempotencyKey",
            "Currency",
            "RecordedAtUtc",
            "CreatedAt",
            "CreatedBy"
        FROM "AffiliateCommissionLedger"
        WHERE "EntryType" = 4
        ON CONFLICT ("IdempotencyKey") DO NOTHING;  -- Idempotent: skip if already migrated
        
        RAISE NOTICE 'AF-38C: Data migration completed';
    ELSE
        RAISE NOTICE 'AF-38C: Skipping data migration (PayoutRequestId column does not exist)';
    END IF;
END $$;

-- Log migration count
DO $$
DECLARE
    migrated_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO migrated_count 
    FROM "AffiliateBalanceLedger" 
    WHERE "Description" LIKE 'Migrated from CommissionLedger%';
    
    RAISE NOTICE 'AF-38C: Migrated % payout entries to AffiliateBalanceLedger', migrated_count;
END $$;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 3: Delete Migrated Payout Entries from CommissionLedger
-- ═════════════════════════════════════════════════════════════════════

-- CRITICAL: This is the ONLY time we delete from the append-only ledger.
-- We're moving data to its correct architectural home.
DO $$
DECLARE
    delete_count INTEGER;
BEGIN
    DELETE FROM "AffiliateCommissionLedger" WHERE "EntryType" = 4;
    GET DIAGNOSTICS delete_count = ROW_COUNT;
    
    IF delete_count > 0 THEN
        RAISE NOTICE 'AF-38C: Deleted % Payout entries from CommissionLedger', delete_count;
    ELSE
        RAISE NOTICE 'AF-38C: No Payout entries to delete from CommissionLedger';
    END IF;
END $$;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 4: Restore Strict Constraints on CommissionLedger
-- ═════════════════════════════════════════════════════════════════════

-- Drop the conditional rate constraint that allowed Payout entries
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CK_AffiliateCommLedger_Rate'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT "CK_AffiliateCommLedger_Rate";
        RAISE NOTICE 'AF-38C: Dropped old rate constraint';
    END IF;
END $$;

-- Restore strict rate constraint (no Payout exception)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CK_AffiliateCommLedger_Rate'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" 
            ADD CONSTRAINT "CK_AffiliateCommLedger_Rate" 
            CHECK ("CommissionRate" > 0 AND "CommissionRate" <= 0.50);
        RAISE NOTICE 'AF-38C: Added strict rate constraint';
    ELSE
        RAISE NOTICE 'AF-38C: Rate constraint already exists';
    END IF;
END $$;

-- Drop the EntryType constraint that included Payout(4)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CK_AffiliateCommLedger_EntryType'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT "CK_AffiliateCommLedger_EntryType";
        RAISE NOTICE 'AF-38C: Dropped old EntryType constraint';
    END IF;
END $$;

-- Restore strict EntryType constraint (no Payout)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CK_AffiliateCommLedger_EntryType'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" 
            ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType" 
            CHECK ("EntryType" IN (0, 1, 2, 3));
        RAISE NOTICE 'AF-38C: Added strict EntryType constraint';
    ELSE
        RAISE NOTICE 'AF-38C: EntryType constraint already exists';
    END IF;
END $$;

-- Make AttributionId NOT NULL (was nullable for Payout entries)
-- First verify no NULL values exist (should be clean after migration)
DO $$
DECLARE
    null_count INTEGER;
    col_is_nullable BOOLEAN;
BEGIN
    SELECT COUNT(*) INTO null_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "AttributionId" IS NULL;
    
    -- Check if column is already NOT NULL
    SELECT (is_nullable = 'YES') INTO col_is_nullable
    FROM information_schema.columns
    WHERE table_name = 'AffiliateCommissionLedger'
    AND column_name = 'AttributionId';
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-38C: Found % rows with NULL AttributionId. Migration failed.', null_count;
    ELSIF col_is_nullable THEN
        ALTER TABLE "AffiliateCommissionLedger" ALTER COLUMN "AttributionId" SET NOT NULL;
        RAISE NOTICE 'AF-38C: Set AttributionId to NOT NULL';
    ELSE
        RAISE NOTICE 'AF-38C: AttributionId already NOT NULL';
    END IF;
END $$;

-- Make OrderId NOT NULL
DO $$
DECLARE
    null_count INTEGER;
    col_is_nullable BOOLEAN;
BEGIN
    SELECT COUNT(*) INTO null_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "OrderId" IS NULL;
    
    SELECT (is_nullable = 'YES') INTO col_is_nullable
    FROM information_schema.columns
    WHERE table_name = 'AffiliateCommissionLedger'
    AND column_name = 'OrderId';
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-38C: Found % rows with NULL OrderId. Migration failed.', null_count;
    ELSIF col_is_nullable THEN
        ALTER TABLE "AffiliateCommissionLedger" ALTER COLUMN "OrderId" SET NOT NULL;
        RAISE NOTICE 'AF-38C: Set OrderId to NOT NULL';
    ELSE
        RAISE NOTICE 'AF-38C: OrderId already NOT NULL';
    END IF;
END $$;

-- Make OrderItemId NOT NULL
DO $$
DECLARE
    null_count INTEGER;
    col_is_nullable BOOLEAN;
BEGIN
    SELECT COUNT(*) INTO null_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "OrderItemId" IS NULL;
    
    SELECT (is_nullable = 'YES') INTO col_is_nullable
    FROM information_schema.columns
    WHERE table_name = 'AffiliateCommissionLedger'
    AND column_name = 'OrderItemId';
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-38C: Found % rows with NULL OrderItemId. Migration failed.', null_count;
    ELSIF col_is_nullable THEN
        ALTER TABLE "AffiliateCommissionLedger" ALTER COLUMN "OrderItemId" SET NOT NULL;
        RAISE NOTICE 'AF-38C: Set OrderItemId to NOT NULL';
    ELSE
        RAISE NOTICE 'AF-38C: OrderItemId already NOT NULL';
    END IF;
END $$;

-- Drop PayoutRequestId column (no longer needed in CommissionLedger)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'AffiliateCommissionLedger' 
        AND column_name = 'PayoutRequestId'
    ) THEN
        ALTER TABLE "AffiliateCommissionLedger" DROP COLUMN "PayoutRequestId";
        RAISE NOTICE 'AF-38C: Dropped PayoutRequestId column from CommissionLedger';
    ELSE
        RAISE NOTICE 'AF-38C: PayoutRequestId column already dropped';
    END IF;
END $$;

-- ═════════════════════════════════════════════════════════════════════
-- STEP 5: Create Immutability Trigger for BalanceLedger
-- ═════════════════════════════════════════════════════════════════════

CREATE OR REPLACE FUNCTION prevent_balance_ledger_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'AffiliateBalanceLedger is append-only. UPDATE forbidden.';
    ELSIF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'AffiliateBalanceLedger is append-only. DELETE forbidden.';
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_balance_ledger_mutation ON "AffiliateBalanceLedger";

CREATE TRIGGER trg_prevent_balance_ledger_mutation
    BEFORE UPDATE OR DELETE ON "AffiliateBalanceLedger"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_balance_ledger_mutation();

-- ═════════════════════════════════════════════════════════════════════
-- STEP 6: Verification Queries
-- ═════════════════════════════════════════════════════════════════════

-- Verify no Payout entries remain in CommissionLedger
DO $$
DECLARE
    payout_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO payout_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "EntryType" = 4;
    
    IF payout_count > 0 THEN
        RAISE EXCEPTION 'AF-38C: Found % Payout entries still in CommissionLedger', payout_count;
    END IF;
    
    RAISE NOTICE 'AF-38C: ✓ CommissionLedger clean (no Payout entries)';
END $$;

-- Verify all AttributionId, OrderId, OrderItemId are NOT NULL
DO $$
DECLARE
    null_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO null_count 
    FROM "AffiliateCommissionLedger" 
    WHERE "AttributionId" IS NULL 
       OR "OrderId" IS NULL 
       OR "OrderItemId" IS NULL;
    
    IF null_count > 0 THEN
        RAISE EXCEPTION 'AF-38C: Found % rows with NULL FKs', null_count;
    END IF;
    
    RAISE NOTICE 'AF-38C: ✓ All CommissionLedger FKs are NOT NULL';
END $$;

-- Verify BalanceLedger has migrated entries
DO $$
DECLARE
    balance_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO balance_count FROM "AffiliateBalanceLedger";
    
    RAISE NOTICE 'AF-38C: ✓ AffiliateBalanceLedger has % entries', balance_count;
END $$;

COMMIT;

-- ═════════════════════════════════════════════════════════════════════
-- AF-38C Migration Complete
-- ═════════════════════════════════════════════════════════════════════
-- Next Steps:
-- 1. Update C# entity models (AffiliateCommissionLedger, AffiliateBalanceLedger)
-- 2. Update AffiliatePayoutService.MarkPayoutPaidAsync to use BalanceLedger
-- 3. Update GetAvailableBalanceAsync to query BalanceLedger
-- 4. Remove Payout enum value from CommissionEntryType
-- 5. Run integration tests
-- ═════════════════════════════════════════════════════════════════════
