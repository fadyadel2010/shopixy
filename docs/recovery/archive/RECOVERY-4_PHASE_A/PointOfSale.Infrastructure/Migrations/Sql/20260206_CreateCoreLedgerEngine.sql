-- ═══════════════════════════════════════════════════════════════════════════════
-- UNIFIED FINANCIAL ENGINE - PHASE 1: CORE LEDGER ENGINE
-- Migration: Create accounting tables with double-entry constraints
-- ═══════════════════════════════════════════════════════════════════════════════

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: Accounts (Chart of Accounts)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "Accounts" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    
    -- Identity (Immutable)
    "AccountCode" VARCHAR(20) NOT NULL,
    "Name" VARCHAR(200) NOT NULL,
    "NameAr" VARCHAR(200),
    "Description" VARCHAR(500),
    
    -- Classification (Immutable)
    "AccountType" SMALLINT NOT NULL,  -- 1=Asset, 2=Liability, 3=Equity, 4=Revenue, 5=Expense, 6=COGS
    
    -- Hierarchy
    "ParentAccountId" UUID REFERENCES "Accounts"("Id") ON DELETE RESTRICT,
    "Level" SMALLINT NOT NULL DEFAULT 1,
    
    -- Balance
    "CurrentBalance" DECIMAL(18,4) NOT NULL DEFAULT 0,
    
    -- Flags
    "IsSystem" BOOLEAN NOT NULL DEFAULT FALSE,
    "IsClearing" BOOLEAN NOT NULL DEFAULT FALSE,
    "PreventNegative" BOOLEAN NOT NULL DEFAULT FALSE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    
    -- Clearing Metadata
    "ClearingSourceType" VARCHAR(50),
    "ClearingSourceCode" VARCHAR(50),
    "ExpectedSettlementDays" SMALLINT,
    
    -- Audit
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" TIMESTAMPTZ,
    
    -- BaseEntity fields (if required by your base class)
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ,
    "CreatedBy" VARCHAR(100),
    "UpdatedBy" VARCHAR(100),
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Constraints
    CONSTRAINT "CK_Accounts_AccountType" CHECK ("AccountType" BETWEEN 1 AND 6),
    CONSTRAINT "CK_Accounts_Level" CHECK ("Level" >= 1)
);

-- Unique constraint on AccountCode per tenant
CREATE UNIQUE INDEX "IX_Accounts_TenantId_AccountCode" 
ON "Accounts" ("TenantId", "AccountCode");

-- Indexes
CREATE INDEX "IX_Accounts_TenantId" ON "Accounts" ("TenantId");
CREATE INDEX "IX_Accounts_ParentAccountId" ON "Accounts" ("ParentAccountId");
CREATE INDEX "IX_Accounts_TenantId_IsClearing" ON "Accounts" ("TenantId", "IsClearing") WHERE "IsClearing" = TRUE;
CREATE INDEX "IX_Accounts_TenantId_AccountType" ON "Accounts" ("TenantId", "AccountType");

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: JournalEntries (Immutable financial transaction records)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "JournalEntries" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    
    -- Identity
    "JournalNumber" VARCHAR(50) NOT NULL,
    "JournalDate" DATE NOT NULL,
    "PostedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Source
    "SourceType" VARCHAR(50) NOT NULL,
    "SourceId" UUID NOT NULL,
    "SourceReference" VARCHAR(100),
    
    -- Description
    "Description" VARCHAR(500) NOT NULL,
    
    -- Idempotency (CRITICAL for duplicate prevention)
    "IdempotencyKey" VARCHAR(255) NOT NULL,
    
    -- Status
    "Status" SMALLINT NOT NULL DEFAULT 1,  -- 1=Posted, 2=Reversed
    "ReversedByJournalId" UUID REFERENCES "JournalEntries"("Id") ON DELETE RESTRICT,
    "ReversesJournalId" UUID REFERENCES "JournalEntries"("Id") ON DELETE RESTRICT,
    "ReversalReason" VARCHAR(500),
    
    -- Audit
    "CreatedByUserId" UUID,
    
    -- BaseEntity fields
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ,
    "CreatedBy" VARCHAR(100),
    "UpdatedBy" VARCHAR(100),
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Constraints
    CONSTRAINT "CK_JournalEntries_Status" CHECK ("Status" IN (1, 2))
);

-- CRITICAL: Unique constraint on IdempotencyKey per tenant
CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_IdempotencyKey" 
ON "JournalEntries" ("TenantId", "IdempotencyKey");

-- Unique constraint on JournalNumber per tenant
CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_JournalNumber" 
ON "JournalEntries" ("TenantId", "JournalNumber");

-- Indexes
CREATE INDEX "IX_JournalEntries_TenantId" ON "JournalEntries" ("TenantId");
CREATE INDEX "IX_JournalEntries_TenantId_JournalDate" ON "JournalEntries" ("TenantId", "JournalDate" DESC);
CREATE INDEX "IX_JournalEntries_TenantId_Source" ON "JournalEntries" ("TenantId", "SourceType", "SourceId");
CREATE INDEX "IX_JournalEntries_TenantId_Status" ON "JournalEntries" ("TenantId", "Status");
CREATE INDEX "IX_JournalEntries_PostedAtUtc" ON "JournalEntries" ("PostedAtUtc" DESC);

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: LedgerEntries (Debit/Credit lines within journals)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "LedgerEntries" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "JournalEntryId" UUID NOT NULL REFERENCES "JournalEntries"("Id") ON DELETE CASCADE,
    
    -- Account
    "AccountId" UUID NOT NULL REFERENCES "Accounts"("Id") ON DELETE RESTRICT,
    
    -- Amounts
    "DebitAmount" DECIMAL(18,4) NOT NULL DEFAULT 0,
    "CreditAmount" DECIMAL(18,4) NOT NULL DEFAULT 0,
    "BalanceAfter" DECIMAL(18,4) NOT NULL,
    
    -- Metadata
    "LineDescription" VARCHAR(200),
    "LineNumber" SMALLINT NOT NULL,
    
    -- Audit
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- BaseEntity fields
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ,
    "CreatedBy" VARCHAR(100),
    "UpdatedBy" VARCHAR(100),
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- CRITICAL: Double-entry constraint - exactly one side must be > 0
    CONSTRAINT "CK_LedgerEntries_SingleSide" CHECK (
        ("DebitAmount" > 0 AND "CreditAmount" = 0) OR
        ("DebitAmount" = 0 AND "CreditAmount" > 0)
    ),
    
    -- Amounts must be non-negative
    CONSTRAINT "CK_LedgerEntries_PositiveAmounts" CHECK (
        "DebitAmount" >= 0 AND "CreditAmount" >= 0
    )
);

-- Indexes
CREATE INDEX "IX_LedgerEntries_TenantId" ON "LedgerEntries" ("TenantId");
CREATE INDEX "IX_LedgerEntries_JournalEntryId" ON "LedgerEntries" ("JournalEntryId");
CREATE INDEX "IX_LedgerEntries_AccountId_CreatedAtUtc" ON "LedgerEntries" ("AccountId", "CreatedAtUtc" DESC);
CREATE INDEX "IX_LedgerEntries_TenantId_AccountId" ON "LedgerEntries" ("TenantId", "AccountId");

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: JournalNumberSequences (Per-tenant sequential numbering)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "JournalNumberSequences" (
    "TenantId" UUID PRIMARY KEY,
    "CurrentYear" SMALLINT NOT NULL,
    "NextNumber" INT NOT NULL DEFAULT 1
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- IMMUTABILITY TRIGGERS: Prevent UPDATE/DELETE on journal tables
-- ═══════════════════════════════════════════════════════════════════════════════

-- Function to prevent updates on journal entries
CREATE OR REPLACE FUNCTION prevent_journal_modification()
RETURNS TRIGGER AS $$
BEGIN
    -- Allow only Status, ReversedByJournalId, ReversalReason updates (for reversal marking)
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

-- Function to prevent any modification on ledger entries
CREATE OR REPLACE FUNCTION prevent_ledger_modification()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Ledger entries are immutable and cannot be updated.';
    END IF;
    
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Ledger entries cannot be deleted. Use journal reversal instead.';
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Apply immutability trigger to JournalEntries
DROP TRIGGER IF EXISTS trg_prevent_journal_modification ON "JournalEntries";
CREATE TRIGGER trg_prevent_journal_modification
    BEFORE UPDATE OR DELETE ON "JournalEntries"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_journal_modification();

-- Apply immutability trigger to LedgerEntries
DROP TRIGGER IF EXISTS trg_prevent_ledger_modification ON "LedgerEntries";
CREATE TRIGGER trg_prevent_ledger_modification
    BEFORE UPDATE OR DELETE ON "LedgerEntries"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_ledger_modification();

-- ═══════════════════════════════════════════════════════════════════════════════
-- COMMENTS: Documentation for future developers
-- ═══════════════════════════════════════════════════════════════════════════════

COMMENT ON TABLE "Accounts" IS 'Chart of Accounts - Core ledger accounts for double-entry accounting';
COMMENT ON TABLE "JournalEntries" IS 'Immutable financial transaction records - the source of truth';
COMMENT ON TABLE "LedgerEntries" IS 'Individual debit/credit lines within journal entries';
COMMENT ON TABLE "JournalNumberSequences" IS 'Per-tenant sequential journal number generation';

COMMENT ON COLUMN "Accounts"."CurrentBalance" IS 'Denormalized balance for performance. Updated atomically with each ledger entry.';
COMMENT ON COLUMN "Accounts"."PreventNegative" IS 'If true, posting engine will reject entries that would cause negative balance.';
COMMENT ON COLUMN "Accounts"."IsClearing" IS 'If true, this is a clearing/transit account that should trend toward zero.';

COMMENT ON COLUMN "JournalEntries"."IdempotencyKey" IS 'CRITICAL: Unique key for duplicate prevention. Format: {OPERATION}:{TenantId}:{UniqueIds}';
COMMENT ON COLUMN "JournalEntries"."Status" IS '1=Posted (active), 2=Reversed (negated by reversal journal)';

COMMENT ON COLUMN "LedgerEntries"."BalanceAfter" IS 'Account balance immediately after this entry. Provides audit trail.';
COMMENT ON CONSTRAINT "CK_LedgerEntries_SingleSide" ON "LedgerEntries" IS 'Enforces that exactly one of debit/credit is > 0';
