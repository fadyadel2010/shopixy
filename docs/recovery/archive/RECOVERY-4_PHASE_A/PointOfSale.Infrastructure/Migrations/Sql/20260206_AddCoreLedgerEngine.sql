START TRANSACTION;
ALTER TABLE "AbandonedCarts" DROP COLUMN "CreatedAt";

ALTER TABLE "AbandonedCarts" DROP COLUMN "CreatedBy";

ALTER TABLE "AbandonedCarts" DROP COLUMN "IsDeleted";

ALTER TABLE "AbandonedCarts" DROP COLUMN "UpdatedAt";

ALTER TABLE "AbandonedCarts" DROP COLUMN "UpdatedBy";

CREATE TABLE "Accounts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "AccountCode" character varying(20) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "Description" character varying(500),
    "AccountType" integer NOT NULL,
    "ParentAccountId" uuid,
    "Level" integer NOT NULL DEFAULT 1,
    "CurrentBalance" numeric(18,4) NOT NULL,
    "IsSystem" boolean NOT NULL DEFAULT FALSE,
    "IsClearing" boolean NOT NULL DEFAULT FALSE,
    "PreventNegative" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "ClearingSourceType" character varying(50),
    "ClearingSourceCode" character varying(50),
    "ExpectedSettlementDays" integer,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_Accounts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Accounts_Accounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JournalEntries" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "JournalNumber" character varying(50) NOT NULL,
    "JournalDate" timestamp with time zone NOT NULL,
    "PostedAtUtc" timestamp with time zone NOT NULL,
    "SourceType" character varying(50) NOT NULL,
    "SourceId" uuid NOT NULL,
    "SourceReference" character varying(100),
    "Description" character varying(500) NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "Status" integer NOT NULL,
    "ReversedByJournalId" uuid,
    "ReversesJournalId" uuid,
    "ReversalReason" character varying(500),
    "CreatedByUserId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_JournalEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_JournalEntries_JournalEntries_ReversedByJournalId" FOREIGN KEY ("ReversedByJournalId") REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JournalNumberSequences" (
    "TenantId" uuid NOT NULL,
    "CurrentYear" integer NOT NULL,
    "NextNumber" integer NOT NULL DEFAULT 1,
    CONSTRAINT "PK_JournalNumberSequences" PRIMARY KEY ("TenantId")
);

CREATE TABLE "LedgerEntries" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "JournalEntryId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "DebitAmount" numeric(18,4) NOT NULL,
    "CreditAmount" numeric(18,4) NOT NULL,
    "BalanceAfter" numeric(18,4) NOT NULL,
    "LineDescription" character varying(200),
    "LineNumber" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_LedgerEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_LedgerEntries_PositiveAmounts" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0),
    CONSTRAINT "CK_LedgerEntries_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0)),
    CONSTRAINT "FK_LedgerEntries_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_LedgerEntries_JournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Accounts_ParentAccountId" ON "Accounts" ("ParentAccountId");

CREATE INDEX "IX_Accounts_TenantId" ON "Accounts" ("TenantId");

CREATE UNIQUE INDEX "IX_Accounts_TenantId_AccountCode" ON "Accounts" ("TenantId", "AccountCode");

CREATE INDEX "IX_Accounts_TenantId_AccountType" ON "Accounts" ("TenantId", "AccountType");

CREATE INDEX "IX_Accounts_TenantId_IsClearing" ON "Accounts" ("TenantId", "IsClearing") WHERE "IsClearing" = true;

CREATE INDEX "IX_JournalEntries_PostedAtUtc" ON "JournalEntries" ("PostedAtUtc");

CREATE UNIQUE INDEX "IX_JournalEntries_ReversedByJournalId" ON "JournalEntries" ("ReversedByJournalId");

CREATE INDEX "IX_JournalEntries_TenantId" ON "JournalEntries" ("TenantId");

CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_IdempotencyKey" ON "JournalEntries" ("TenantId", "IdempotencyKey");

CREATE INDEX "IX_JournalEntries_TenantId_JournalDate" ON "JournalEntries" ("TenantId", "JournalDate");

CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_JournalNumber" ON "JournalEntries" ("TenantId", "JournalNumber");

CREATE INDEX "IX_JournalEntries_TenantId_Source" ON "JournalEntries" ("TenantId", "SourceType", "SourceId");

CREATE INDEX "IX_JournalEntries_TenantId_Status" ON "JournalEntries" ("TenantId", "Status");

CREATE INDEX "IX_LedgerEntries_AccountId_CreatedAtUtc" ON "LedgerEntries" ("AccountId", "CreatedAtUtc");

CREATE INDEX "IX_LedgerEntries_JournalEntryId" ON "LedgerEntries" ("JournalEntryId");

CREATE INDEX "IX_LedgerEntries_TenantId" ON "LedgerEntries" ("TenantId");

CREATE INDEX "IX_LedgerEntries_TenantId_AccountId" ON "LedgerEntries" ("TenantId", "AccountId");


                CREATE OR REPLACE FUNCTION prevent_journal_modification()
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
            


                DROP TRIGGER IF EXISTS trg_prevent_journal_modification ON "JournalEntries";
                CREATE TRIGGER trg_prevent_journal_modification
                    BEFORE UPDATE OR DELETE ON "JournalEntries"
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_journal_modification();
            


                DROP TRIGGER IF EXISTS trg_prevent_ledger_modification ON "LedgerEntries";
                CREATE TRIGGER trg_prevent_ledger_modification
                    BEFORE UPDATE OR DELETE ON "LedgerEntries"
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_ledger_modification();
            

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260206165348_AddCoreLedgerEngine', '9.0.1');

COMMIT;

