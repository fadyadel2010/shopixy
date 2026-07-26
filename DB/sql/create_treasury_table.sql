-- Treasury Transactions Table Creation Script
-- Run this SQL script in your PostgreSQL database

CREATE TABLE IF NOT EXISTS "TreasuryTransactions" (
    "Id" uuid NOT NULL,
    "TransactionNumber" text NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "TargetAccountId" uuid NULL,
    "TargetAccountName" text NULL,
    "Amount" numeric(18,2) NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "ReferenceNumber" text NULL,
    "Notes" text NULL,
    "CreatedByUserId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_TreasuryTransactions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_TreasuryTransactions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE NO ACTION
);

CREATE INDEX IF NOT EXISTS "IX_TreasuryTransactions_CreatedByUserId" ON "TreasuryTransactions" ("CreatedByUserId");
CREATE INDEX IF NOT EXISTS "IX_TreasuryTransactions_TransactionDate" ON "TreasuryTransactions" ("TransactionDate");

-- Insert migration history record
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251204131000_AddTreasuryTransactions', '9.0.0');

-- Verify table was created
SELECT COUNT(*) as treasury_table_exists 
FROM information_schema.tables 
WHERE table_name = 'TreasuryTransactions';
