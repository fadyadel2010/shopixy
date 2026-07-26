BEGIN;

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(200);

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD COLUMN IF NOT EXISTS "CorrelationId" character varying(200);

UPDATE "CommerceHubFinancialTransactions"
SET "IdempotencyKey" = CONCAT(
        'legacy:',
        CASE
            WHEN "EntityType" = 0 THEN 'platform'
            ELSE REPLACE(COALESCE("EntityId"::text, 'missing'), '-', '')
        END,
        ':',
        "TransactionType"::text,
        ':',
        "Direction"::text,
        ':',
        "ReferenceType"::text,
        ':',
        REPLACE("ReferenceId"::text, '-', '')
    )
WHERE "IdempotencyKey" IS NULL OR BTRIM("IdempotencyKey") = '';

UPDATE "CommerceHubFinancialTransactions"
SET "CorrelationId" = CONCAT(
        CASE "ReferenceType"
            WHEN 0 THEN 'Order'
            WHEN 1 THEN 'Refund'
            WHEN 2 THEN 'Payout'
            WHEN 3 THEN 'Settlement'
            WHEN 4 THEN 'Adjustment'
            ELSE 'Reference'
        END,
        ':',
        REPLACE("ReferenceId"::text, '-', '')
    )
WHERE "CorrelationId" IS NULL OR BTRIM("CorrelationId") = '';

ALTER TABLE "CommerceHubFinancialTransactions"
    ALTER COLUMN "IdempotencyKey" SET NOT NULL;

ALTER TABLE "CommerceHubFinancialTransactions"
    ALTER COLUMN "CorrelationId" SET NOT NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubFinancialTransactions"
        GROUP BY "IdempotencyKey"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'CommerceHubFinancialTransactions contains duplicate IdempotencyKey values. Resolve duplicates before applying ledger hardening.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "CommerceHubFinancialTransactions"
        WHERE "EntityId" IS NOT NULL
        GROUP BY "ReferenceId", "TransactionType", "EntityId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'CommerceHubFinancialTransactions contains duplicate vendor ledger rows for (ReferenceId, TransactionType, EntityId). Resolve duplicates before enabling the unique constraint.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "CommerceHubFinancialTransactions"
        WHERE "EntityId" IS NULL
        GROUP BY "ReferenceId", "TransactionType"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'CommerceHubFinancialTransactions contains duplicate platform ledger rows for (ReferenceId, TransactionType). Resolve duplicates before enabling the unique constraint.';
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_CorrelationId"
    ON "CommerceHubFinancialTransactions" ("CorrelationId");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialTransactions_IdempotencyKey"
    ON "CommerceHubFinancialTransactions" ("IdempotencyKey");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId"
    ON "CommerceHubFinancialTransactions" ("ReferenceId", "TransactionType", "EntityId")
    WHERE "EntityId" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform"
    ON "CommerceHubFinancialTransactions" ("ReferenceId", "TransactionType")
    WHERE "EntityId" IS NULL;

COMMIT;
