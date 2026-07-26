BEGIN;

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD COLUMN IF NOT EXISTS "Reason" text NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_CorrelationId"
    ON "CommerceHubFinancialTransactions" ("CorrelationId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_TransactionType"
    ON "CommerceHubFinancialTransactions" ("TransactionType");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendor"
    ON "CommerceHubFinancialTransactions" ("EntityId", "CreatedAtUtc" DESC)
    WHERE "EntityType" = 1 AND "EntityId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedBy_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "CreatedBy", "CreatedAtUtc" DESC)
    WHERE "EntityType" = 1 AND "EntityId" IS NOT NULL AND "CreatedBy" IS NOT NULL;

-- Future-ready note:
-- When ledger volume warrants it, split this table into monthly partitions by "CreatedAtUtc"
-- while keeping correlation/idempotency and vendor-time indexes aligned on each partition.

COMMIT;
