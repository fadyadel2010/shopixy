-- ═══════════════════════════════════════════════════════════════════════════════
-- PHASE 3.5: PRODUCTION HARDENING - Outbox Pattern & Audit Trail
-- ═══════════════════════════════════════════════════════════════════════════════
-- 
-- Purpose: Guaranteed delivery and traceability for commerce-to-ledger integration
-- 
-- Tables:
--   1. AccountingOutboxMessages - Transactional outbox for guaranteed delivery
--   2. CommerceAccountingAudit - Full audit trail from business event to journal
--
-- ═══════════════════════════════════════════════════════════════════════════════

-- Drop existing if re-running (development only)
-- DROP TABLE IF EXISTS "CommerceAccountingAudit";
-- DROP TABLE IF EXISTS "AccountingOutboxMessages";

-- ═══════════════════════════════════════════════════════════════════════════════
-- 1. ACCOUNTING OUTBOX MESSAGES
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS "AccountingOutboxMessages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    
    -- Event Details
    "EventType" VARCHAR(50) NOT NULL,
    "BusinessEntityId" UUID NOT NULL,
    "EntityTypeName" VARCHAR(255),
    "PayloadJson" TEXT NOT NULL,
    "IdempotencyKey" VARCHAR(255) NOT NULL,
    
    -- Timing
    "OccurredAtUtc" TIMESTAMP NOT NULL,
    "ProcessedAtUtc" TIMESTAMP,
    
    -- Result
    "JournalId" UUID,
    
    -- Retry Logic
    "AttemptCount" INTEGER NOT NULL DEFAULT 0,
    "LastError" VARCHAR(2000),
    "NextRetryAtUtc" TIMESTAMP,
    "IsFailed" BOOLEAN NOT NULL DEFAULT false,
    
    -- Audit
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "IsDeleted" BOOLEAN NOT NULL DEFAULT false
);

-- Unique constraint: One message per idempotency key per tenant
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountingOutbox_TenantId_IdempotencyKey"
    ON "AccountingOutboxMessages" ("TenantId", "IdempotencyKey");

-- Index for processor: Find pending messages ready to process
CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_ProcessingStatus"
    ON "AccountingOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc")
    WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;

-- Index for monitoring: Find failed messages
CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_Failed"
    ON "AccountingOutboxMessages" ("TenantId", "IsFailed")
    WHERE "IsFailed" = true;

-- Index for chronological processing
CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_OccurredAtUtc"
    ON "AccountingOutboxMessages" ("OccurredAtUtc");

-- ═══════════════════════════════════════════════════════════════════════════════
-- 2. COMMERCE ACCOUNTING AUDIT TRAIL
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS "CommerceAccountingAudit" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    
    -- Business Event Reference
    "BusinessType" VARCHAR(50) NOT NULL,
    "BusinessId" UUID NOT NULL,
    "BusinessReference" VARCHAR(100),
    
    -- Journal Reference
    "JournalId" UUID NOT NULL,
    "JournalNumber" VARCHAR(50),
    
    -- Operation Details
    "OperationType" VARCHAR(50) NOT NULL,
    "Amount" DECIMAL(18, 4) NOT NULL,
    "PostedAtUtc" TIMESTAMP NOT NULL,
    
    -- Idempotency
    "IdempotencyKey" VARCHAR(255),
    "WasIdempotent" BOOLEAN NOT NULL DEFAULT false,
    
    -- Audit
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "IsDeleted" BOOLEAN NOT NULL DEFAULT false,
    
    -- Foreign Keys
    CONSTRAINT "FK_CommerceAudit_Journal" 
        FOREIGN KEY ("JournalId") 
        REFERENCES "LedgerJournalEntries" ("Id")
        ON DELETE RESTRICT
);

-- Index for business entity lookup: "What journals relate to this sale?"
CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_Business"
    ON "CommerceAccountingAudit" ("TenantId", "BusinessType", "BusinessId");

-- Index for journal lookup: "What business events created this journal?"
CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_JournalId"
    ON "CommerceAccountingAudit" ("JournalId");

-- Index for time-based queries: "What posted today?"
CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_TenantId_PostedAtUtc"
    ON "CommerceAccountingAudit" ("TenantId", "PostedAtUtc");

-- ═══════════════════════════════════════════════════════════════════════════════
-- 3. MONITORING VIEWS (optional but helpful)
-- ═══════════════════════════════════════════════════════════════════════════════

-- View: Pending outbox messages summary
CREATE OR REPLACE VIEW "v_AccountingOutboxPending" AS
SELECT 
    "TenantId",
    "EventType",
    COUNT(*) as "PendingCount",
    MIN("OccurredAtUtc") as "OldestPending",
    MAX("AttemptCount") as "MaxAttempts"
FROM "AccountingOutboxMessages"
WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false
GROUP BY "TenantId", "EventType";

-- View: Failed outbox messages
CREATE OR REPLACE VIEW "v_AccountingOutboxFailed" AS
SELECT 
    "Id",
    "TenantId",
    "EventType",
    "BusinessEntityId",
    "IdempotencyKey",
    "OccurredAtUtc",
    "AttemptCount",
    "LastError"
FROM "AccountingOutboxMessages"
WHERE "IsFailed" = true
ORDER BY "OccurredAtUtc";

-- View: Recent audit activity
CREATE OR REPLACE VIEW "v_AccountingAuditRecent" AS
SELECT 
    a."TenantId",
    a."BusinessType",
    a."BusinessReference",
    a."OperationType",
    a."Amount",
    a."JournalNumber",
    a."PostedAtUtc",
    a."WasIdempotent"
FROM "CommerceAccountingAudit" a
WHERE a."PostedAtUtc" > NOW() - INTERVAL '24 hours'
ORDER BY a."PostedAtUtc" DESC;

-- ═══════════════════════════════════════════════════════════════════════════════
-- 4. MIGRATION TRACKING
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS "__MigrationHistory" (
    "MigrationId" VARCHAR(150) PRIMARY KEY,
    "AppliedOn" TIMESTAMP NOT NULL DEFAULT NOW()
);

INSERT INTO "__MigrationHistory" ("MigrationId")
VALUES ('20260206_Phase35_ProductionHardening')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ═══════════════════════════════════════════════════════════════════════════════
-- VERIFICATION
-- ═══════════════════════════════════════════════════════════════════════════════

SELECT 'Phase 3.5 Migration Complete' as status;
SELECT 
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'AccountingOutboxMessages') as outbox_table,
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'CommerceAccountingAudit') as audit_table;
