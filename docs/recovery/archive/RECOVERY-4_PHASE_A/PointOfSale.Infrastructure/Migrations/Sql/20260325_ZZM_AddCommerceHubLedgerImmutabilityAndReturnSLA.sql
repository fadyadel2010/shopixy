-- =============================================================================
-- AUDIT IMPROVEMENT: Ledger Immutability Trigger
-- 
-- Prevents UPDATE and DELETE operations on the CommerceHubFinancialTransactions
-- table at the database level, ensuring append-only behavior is enforced even
-- if application-level protections are bypassed.
--
-- This is defense-in-depth: the application already uses private setters and
-- factory-only creation, but this trigger makes it impossible to mutate
-- ledger entries even via raw SQL or external database tools.
--
-- EXCEPTION: A maintenance flag can be set to allow emergency corrections
-- by the DBA (requires explicit session-level variable).
-- =============================================================================

-- Prevent UPDATE on financial ledger entries
CREATE OR REPLACE FUNCTION fn_commercehub_ledger_prevent_update()
RETURNS TRIGGER AS $$
BEGIN
    -- Allow if explicit maintenance mode is enabled for this session
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: UPDATE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Prevent DELETE on financial ledger entries
CREATE OR REPLACE FUNCTION fn_commercehub_ledger_prevent_delete()
RETURNS TRIGGER AS $$
BEGIN
    -- Allow if explicit maintenance mode is enabled for this session
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN OLD;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: DELETE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Drop existing triggers if they exist (idempotent)
DROP TRIGGER IF EXISTS trg_commercehub_ledger_prevent_update ON "CommerceHubFinancialTransactions";
DROP TRIGGER IF EXISTS trg_commercehub_ledger_prevent_delete ON "CommerceHubFinancialTransactions";

-- Create BEFORE UPDATE trigger
CREATE TRIGGER trg_commercehub_ledger_prevent_update
    BEFORE UPDATE ON "CommerceHubFinancialTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_ledger_prevent_update();

-- Create BEFORE DELETE trigger  
CREATE TRIGGER trg_commercehub_ledger_prevent_delete
    BEFORE DELETE ON "CommerceHubFinancialTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_ledger_prevent_delete();

-- =============================================================================
-- AUDIT IMPROVEMENT: Return SLA Tracking Columns
--
-- Adds SLA tracking metadata to CommerceHubReturnRequests to support:
--   1. Time-in-state monitoring for each return phase
--   2. SLA breach detection for the stale return escalation job
--   3. Dashboard metrics for return logistics performance
-- =============================================================================

-- Escalation tracking columns
ALTER TABLE "CommerceHubReturnRequests"
    ADD COLUMN IF NOT EXISTS "EscalatedAtUtc" TIMESTAMP WITHOUT TIME ZONE,
    ADD COLUMN IF NOT EXISTS "EscalationLevel" INTEGER DEFAULT 0 NOT NULL,
    ADD COLUMN IF NOT EXISTS "EscalationReason" TEXT;

-- SLA tracking: expected completion date based on trigger type
ALTER TABLE "CommerceHubReturnRequests"
    ADD COLUMN IF NOT EXISTS "ExpectedCompletionAtUtc" TIMESTAMP WITHOUT TIME ZONE;

-- Index for stale return detection queries
CREATE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_EscalationLevel_Status"
    ON "CommerceHubReturnRequests" ("EscalationLevel", "Status")
    WHERE "IsDeleted" = FALSE;

-- Index for SLA monitoring queries
CREATE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_ExpectedCompletionAtUtc"
    ON "CommerceHubReturnRequests" ("ExpectedCompletionAtUtc")
    WHERE "IsDeleted" = FALSE AND "ExpectedCompletionAtUtc" IS NOT NULL;

-- =============================================================================
-- AUDIT IMPROVEMENT: Ledger Immutability Audit Log
-- 
-- Adds an immutability attribute to financial audit log entries for flagging
-- which entries were created by the immutability enforcement system.
-- =============================================================================

ALTER TABLE "CommerceHubFinancialAuditLogs"
    ADD COLUMN IF NOT EXISTS "IsSystemGenerated" BOOLEAN DEFAULT FALSE NOT NULL;
