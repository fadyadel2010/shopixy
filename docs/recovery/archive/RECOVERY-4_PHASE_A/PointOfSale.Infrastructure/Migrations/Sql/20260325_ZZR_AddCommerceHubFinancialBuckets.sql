BEGIN;

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD COLUMN IF NOT EXISTS "FinancialBucket" integer NULL;

-- Controlled schema backfill for existing immutable ledger rows.
-- This keeps append-only protections intact for normal sessions while allowing
-- this one migration to classify historical entries safely.
SELECT set_config('app.ledger_maintenance_mode', 'true', true);

-- Legacy databases may still have the original single immutability trigger
-- that does not honor maintenance mode. Drop it if present before backfilling.
DROP TRIGGER IF EXISTS trg_commercehub_financial_transactions_immutable
    ON "CommerceHubFinancialTransactions";

UPDATE "CommerceHubFinancialTransactions"
SET "FinancialBucket" = CASE
    WHEN "TransactionType" = 0 AND "EntityType" = 0 THEN 0
    WHEN "TransactionType" = 0 AND "EntityType" = 1 THEN 2
    WHEN "TransactionType" = 1 THEN 1
    WHEN "TransactionType" = 2 THEN 2
    WHEN "TransactionType" = 3 THEN 2
    WHEN "TransactionType" = 4 AND "EntityType" = 0 AND "Direction" = 0 THEN 0
    WHEN "TransactionType" = 4 AND "EntityType" = 0 AND "Direction" = 1 THEN 2
    WHEN "TransactionType" = 4 AND "EntityType" = 1 AND "Direction" = 0 THEN 2
    WHEN "TransactionType" = 4 AND "EntityType" = 1 AND "Direction" = 1 THEN 1
    WHEN "TransactionType" = 5 AND "EntityType" = 0 THEN 0
    WHEN "TransactionType" = 5 AND "EntityType" = 1 THEN 1
    ELSE 0
END
WHERE "FinancialBucket" IS NULL;

ALTER TABLE "CommerceHubFinancialTransactions"
    ALTER COLUMN "FinancialBucket" SET NOT NULL;

ALTER TABLE "CommerceHubFinancialTransactions"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubFinancialTransactions_FinancialBucket";

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD CONSTRAINT "CK_CommerceHubFinancialTransactions_FinancialBucket"
        CHECK ("FinancialBucket" IN (0, 1, 2));

DROP INDEX IF EXISTS "IX_CommerceHubFinancialTransactions_EntityType_FinancialBucket_";
DROP INDEX IF EXISTS "IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc";

CREATE INDEX IF NOT EXISTS "IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("EntityType", "FinancialBucket", "CreatedAtUtc" DESC);

-- Reinstall the hardened immutability triggers so databases that skipped the
-- newer trigger migration still end this migration in a protected state.
CREATE OR REPLACE FUNCTION fn_commercehub_ledger_prevent_update()
RETURNS TRIGGER AS $$
BEGIN
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: UPDATE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION fn_commercehub_ledger_prevent_delete()
RETURNS TRIGGER AS $$
BEGIN
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN OLD;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: DELETE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_commercehub_ledger_prevent_update ON "CommerceHubFinancialTransactions";
DROP TRIGGER IF EXISTS trg_commercehub_ledger_prevent_delete ON "CommerceHubFinancialTransactions";

CREATE TRIGGER trg_commercehub_ledger_prevent_update
    BEFORE UPDATE ON "CommerceHubFinancialTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_ledger_prevent_update();

CREATE TRIGGER trg_commercehub_ledger_prevent_delete
    BEFORE DELETE ON "CommerceHubFinancialTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_ledger_prevent_delete();

COMMIT;
