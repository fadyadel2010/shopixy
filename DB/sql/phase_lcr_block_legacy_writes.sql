-- ════════════════════════════════════════════════════════════════════════════
-- PHASE LCR-5: Block Legacy CashboxTransactions and VisaTransactions Writes
-- ════════════════════════════════════════════════════════════════════════════
-- 
-- Purpose: Enforce all financial transactions to go through the Ledger via Outbox pattern
-- 
-- IMPORTANT: Enforcement is controlled by LCR_ENFORCEMENT_ENABLED setting.
--            Default is 'false' for safe rollout.
--
-- To enable enforcement after testing:
--   UPDATE "SystemSettings" SET "Value" = 'true' WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
--
-- To disable enforcement if issues arise:
--   UPDATE "SystemSettings" SET "Value" = 'false' WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
--
-- ════════════════════════════════════════════════════════════════════════════

-- Step 1: Create SystemSettings table if not exists
-- (skipped if table already exists)
CREATE TABLE IF NOT EXISTS "SystemSettings" (
    "Key" text PRIMARY KEY,
    "Value" text NOT NULL,
    "Description" text,
    "UpdatedAt" timestamp with time zone DEFAULT now()
);

-- Step 2: Insert enforcement setting (disabled by default for safe rollout)
-- Only insert if the key doesn't already exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "SystemSettings" WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED') THEN
        INSERT INTO "SystemSettings" ("Id", "Key", "Value", "Description", "CreatedAt", "IsDeleted")
        VALUES (gen_random_uuid(), 'LCR_ENFORCEMENT_ENABLED', 'false', 'When true, blocks legacy CashboxTransactions and VisaTransactions inserts', now(), false);
    END IF;
END $$;

-- Step 3: Create trigger function for CashboxTransactions
CREATE OR REPLACE FUNCTION block_legacy_cashbox_insert()
RETURNS TRIGGER AS $$
DECLARE
    enforcement_enabled boolean;
BEGIN
    -- Check if enforcement is enabled
    SELECT COALESCE("Value"::boolean, false) INTO enforcement_enabled
    FROM "SystemSettings"
    WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
    
    IF enforcement_enabled THEN
        -- Allow only specific transaction types (Treasury operations):
        -- 1 = OpenDrawer
        -- 2 = CloseDrawer  
        -- 7 = CashIn (deposit)
        -- 8 = CashOut (withdrawal)
        IF NEW."TransactionType" NOT IN (1, 2, 7, 8) THEN
            RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to CashboxTransactions is blocked. Use Treasury/Ledger instead. TransactionType=%', NEW."TransactionType"
            USING ERRCODE = 'P0001';
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Step 4: Create trigger function for VisaTransactions
CREATE OR REPLACE FUNCTION block_legacy_visa_insert()
RETURNS TRIGGER AS $$
DECLARE
    enforcement_enabled boolean;
BEGIN
    -- Check if enforcement is enabled
    SELECT COALESCE("Value"::boolean, false) INTO enforcement_enabled
    FROM "SystemSettings"
    WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
    
    IF enforcement_enabled THEN
        -- Block ALL VisaTransactions inserts - all should go through Treasury/Ledger
        RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to VisaTransactions is blocked. Use Treasury/Ledger instead.'
        USING ERRCODE = 'P0001';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Step 5: Create triggers
DROP TRIGGER IF EXISTS tr_block_legacy_cashbox_insert ON "CashboxTransactions";
CREATE TRIGGER tr_block_legacy_cashbox_insert
BEFORE INSERT ON "CashboxTransactions"
FOR EACH ROW
EXECUTE FUNCTION block_legacy_cashbox_insert();

DROP TRIGGER IF EXISTS tr_block_legacy_visa_insert ON "VisaTransactions";
CREATE TRIGGER tr_block_legacy_visa_insert
BEFORE INSERT ON "VisaTransactions"
FOR EACH ROW
EXECUTE FUNCTION block_legacy_visa_insert();

-- ════════════════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES (optional - run after setup to verify)
-- ════════════════════════════════════════════════════════════════════════════

-- Check setting value:
-- SELECT * FROM "SystemSettings" WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';

-- List triggers on CashboxTransactions:
-- SELECT tgname, tgenabled FROM pg_trigger WHERE tgrelid = '"CashboxTransactions"'::regclass;

-- List triggers on VisaTransactions:
-- SELECT tgname, tgenabled FROM pg_trigger WHERE tgrelid = '"VisaTransactions"'::regclass;

-- ════════════════════════════════════════════════════════════════════════════
-- ROLLBACK SCRIPT (if needed)
-- ════════════════════════════════════════════════════════════════════════════
-- DROP TRIGGER IF EXISTS tr_block_legacy_cashbox_insert ON "CashboxTransactions";
-- DROP TRIGGER IF EXISTS tr_block_legacy_visa_insert ON "VisaTransactions";
-- DROP FUNCTION IF EXISTS block_legacy_cashbox_insert();
-- DROP FUNCTION IF EXISTS block_legacy_visa_insert();
-- DELETE FROM "SystemSettings" WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
