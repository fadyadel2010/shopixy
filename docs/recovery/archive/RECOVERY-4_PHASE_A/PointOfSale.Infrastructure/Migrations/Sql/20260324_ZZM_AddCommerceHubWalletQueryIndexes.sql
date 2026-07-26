BEGIN;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "CreatedAtUtc" DESC);

COMMIT;
