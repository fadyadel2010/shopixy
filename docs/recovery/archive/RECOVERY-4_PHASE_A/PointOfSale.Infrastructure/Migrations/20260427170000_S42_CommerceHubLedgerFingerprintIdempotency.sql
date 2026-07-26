-- S42: CommerceHub ledger idempotency fingerprint hardening
-- Replaces coarse reference/type uniqueness with full financial-fingerprint
-- uniqueness so legitimate opposite-direction corrections are not blocked while
-- exact duplicate ledger rows remain impossible.

DROP INDEX IF EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId";
DROP INDEX IF EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHFinancialTx_Fingerprint_Vendor"
ON "CommerceHubFinancialTransactions"
(
    "ReferenceType",
    "ReferenceId",
    "TransactionType",
    "EntityId",
    "Direction",
    "FinancialBucket",
    "Amount",
    "Currency"
)
WHERE "EntityId" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHFinancialTx_Fingerprint_Platform"
ON "CommerceHubFinancialTransactions"
(
    "ReferenceType",
    "ReferenceId",
    "TransactionType",
    "Direction",
    "FinancialBucket",
    "Amount",
    "Currency"
)
WHERE "EntityId" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260427170000_S42_CommerceHubLedgerFingerprintIdempotency', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260427170000_S42_CommerceHubLedgerFingerprintIdempotency'
);
