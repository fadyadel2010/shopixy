BEGIN;

ALTER TABLE "CommerceHubFinancialTransactions"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubFinancialTransactions_TransactionType";

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD CONSTRAINT "CK_CommerceHubFinancialTransactions_TransactionType"
        CHECK ("TransactionType" IN (0, 1, 2, 3, 4, 5));

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_EntityType_EntityId_TransactionType_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "TransactionType", "CreatedAtUtc" DESC);

COMMIT;
