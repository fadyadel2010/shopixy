-- ═══════════════════════════════════════════════════════════════════════════════
-- UNIFIED FINANCIAL ENGINE - PHASE 1: CORE LEDGER ENGINE
-- Generated: 2026-02-06
-- Target: PostgreSQL via PgAdmin4
-- ═══════════════════════════════════════════════════════════════════════════════

-- ═══════════════════════════════════════════════════════════════════════════════
-- CLEANUP: Drop any partially created objects from failed migration
-- ═══════════════════════════════════════════════════════════════════════════════
DROP TRIGGER IF EXISTS trg_prevent_ledger_journal_modification ON "LedgerJournalEntries";
DROP TRIGGER IF EXISTS trg_prevent_ledger_line_modification ON "LedgerLines";
DROP FUNCTION IF EXISTS prevent_ledger_journal_modification();
DROP FUNCTION IF EXISTS prevent_ledger_line_modification();
DROP TABLE IF EXISTS "LedgerLines" CASCADE;
DROP TABLE IF EXISTS "LedgerJournalEntries" CASCADE;
DROP TABLE IF EXISTS "LedgerAccounts" CASCADE;
DROP TABLE IF EXISTS "LedgerJournalNumberSequences" CASCADE;

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: LedgerAccounts (Chart of Accounts for unified ledger)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE "LedgerAccounts" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "AccountCode" character varying(20) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "Description" character varying(500),
    "AccountType" integer NOT NULL,
    "ParentAccountId" uuid,
    "Level" integer NOT NULL DEFAULT 1,
    "CurrentBalance" numeric(18,4) NOT NULL DEFAULT 0,
    "IsSystem" boolean NOT NULL DEFAULT FALSE,
    "IsClearing" boolean NOT NULL DEFAULT FALSE,
    "PreventNegative" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "ClearingSourceType" character varying(50),
    "ClearingSourceCode" character varying(50),
    "ExpectedSettlementDays" integer,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_LedgerAccounts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LedgerAccounts_Parent" FOREIGN KEY ("ParentAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: LedgerJournalEntries (Immutable financial transaction records)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE "LedgerJournalEntries" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "JournalNumber" character varying(50) NOT NULL,
    "JournalDate" timestamp with time zone NOT NULL,
    "PostedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "SourceType" character varying(50) NOT NULL,
    "SourceId" uuid NOT NULL,
    "SourceReference" character varying(100),
    "Description" character varying(500) NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "Status" integer NOT NULL DEFAULT 1,
    "ReversedByJournalId" uuid,
    "ReversesJournalId" uuid,
    "ReversalReason" character varying(500),
    "CreatedByUserId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_LedgerJournalEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LedgerJournal_ReversedBy" FOREIGN KEY ("ReversedByJournalId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE RESTRICT
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: LedgerJournalNumberSequences (Per-tenant sequential numbering)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE "LedgerJournalNumberSequences" (
    "TenantId" uuid NOT NULL,
    "CurrentYear" integer NOT NULL,
    "NextNumber" integer NOT NULL DEFAULT 1,
    CONSTRAINT "PK_LedgerJournalSeq" PRIMARY KEY ("TenantId")
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: LedgerLines (Debit/Credit lines within journals)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE "LedgerLines" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "JournalEntryId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "DebitAmount" numeric(18,4) NOT NULL DEFAULT 0,
    "CreditAmount" numeric(18,4) NOT NULL DEFAULT 0,
    "BalanceAfter" numeric(18,4) NOT NULL,
    "LineDescription" character varying(200),
    "LineNumber" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_LedgerLines" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_LedgerLines_Positive" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0),
    CONSTRAINT "CK_LedgerLines_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0)),
    CONSTRAINT "FK_LedgerLines_Account" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_LedgerLines_Journal" FOREIGN KEY ("JournalEntryId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE CASCADE
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- INDEXES: LedgerAccounts
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE INDEX "IX_LedgerAcct_Parent" ON "LedgerAccounts" ("ParentAccountId");
CREATE INDEX "IX_LedgerAcct_Tenant" ON "LedgerAccounts" ("TenantId");
CREATE UNIQUE INDEX "IX_LedgerAcct_TenantCode" ON "LedgerAccounts" ("TenantId", "AccountCode");
CREATE INDEX "IX_LedgerAcct_TenantType" ON "LedgerAccounts" ("TenantId", "AccountType");
CREATE INDEX "IX_LedgerAcct_Clearing" ON "LedgerAccounts" ("TenantId", "IsClearing") WHERE "IsClearing" = true;

-- ═══════════════════════════════════════════════════════════════════════════════
-- INDEXES: LedgerJournalEntries
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE INDEX "IX_LedgerJournal_Posted" ON "LedgerJournalEntries" ("PostedAtUtc");
CREATE UNIQUE INDEX "IX_LedgerJournal_Reversed" ON "LedgerJournalEntries" ("ReversedByJournalId");
CREATE INDEX "IX_LedgerJournal_Tenant" ON "LedgerJournalEntries" ("TenantId");
CREATE UNIQUE INDEX "IX_LedgerJournal_Idempotency" ON "LedgerJournalEntries" ("TenantId", "IdempotencyKey");
CREATE INDEX "IX_LedgerJournal_Date" ON "LedgerJournalEntries" ("TenantId", "JournalDate");
CREATE UNIQUE INDEX "IX_LedgerJournal_Number" ON "LedgerJournalEntries" ("TenantId", "JournalNumber");
CREATE INDEX "IX_LedgerJournal_Source" ON "LedgerJournalEntries" ("TenantId", "SourceType", "SourceId");
CREATE INDEX "IX_LedgerJournal_Status" ON "LedgerJournalEntries" ("TenantId", "Status");

-- ═══════════════════════════════════════════════════════════════════════════════
-- INDEXES: LedgerLines
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE INDEX "IX_LedgerLines_AcctDate" ON "LedgerLines" ("AccountId", "CreatedAtUtc");
CREATE INDEX "IX_LedgerLines_Journal" ON "LedgerLines" ("JournalEntryId");
CREATE INDEX "IX_LedgerLines_Tenant" ON "LedgerLines" ("TenantId");
CREATE INDEX "IX_LedgerLines_TenantAcct" ON "LedgerLines" ("TenantId", "AccountId");

-- ═══════════════════════════════════════════════════════════════════════════════
-- IMMUTABILITY TRIGGERS
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE OR REPLACE FUNCTION prevent_ledger_journal_modification()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD."JournalNumber" != NEW."JournalNumber" OR
           OLD."JournalDate" != NEW."JournalDate" OR
           OLD."SourceType" != NEW."SourceType" OR
           OLD."SourceId" != NEW."SourceId" OR
           OLD."Description" != NEW."Description" OR
           OLD."IdempotencyKey" != NEW."IdempotencyKey" OR
           OLD."PostedAtUtc" != NEW."PostedAtUtc" THEN
            RAISE EXCEPTION 'Journal entries are immutable. Only status and reversal fields can be updated.';
        END IF;
        RETURN NEW;
    END IF;
    
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Journal entries cannot be deleted. Use reversal instead.';
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION prevent_ledger_line_modification()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Ledger lines are immutable and cannot be updated.';
    END IF;
    
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Ledger lines cannot be deleted. Use journal reversal instead.';
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_journal_immutable
    BEFORE UPDATE OR DELETE ON "LedgerJournalEntries"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_ledger_journal_modification();

CREATE TRIGGER trg_line_immutable
    BEFORE UPDATE OR DELETE ON "LedgerLines"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_ledger_line_modification();

-- ═══════════════════════════════════════════════════════════════════════════════
-- COMMENTS
-- ═══════════════════════════════════════════════════════════════════════════════
COMMENT ON TABLE "LedgerAccounts" IS 'Unified Ledger - Chart of Accounts';
COMMENT ON TABLE "LedgerJournalEntries" IS 'Unified Ledger - Immutable journal entries';
COMMENT ON TABLE "LedgerLines" IS 'Unified Ledger - Debit/Credit lines';
COMMENT ON TABLE "LedgerJournalNumberSequences" IS 'Unified Ledger - Journal number sequences';

-- ═══════════════════════════════════════════════════════════════════════════════
-- EF MIGRATIONS HISTORY
-- ═══════════════════════════════════════════════════════════════════════════════
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine';
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260206165348_AddCoreLedgerEngine', '9.0.1');
