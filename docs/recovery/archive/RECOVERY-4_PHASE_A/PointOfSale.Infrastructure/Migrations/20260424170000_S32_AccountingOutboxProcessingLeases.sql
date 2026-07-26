-- Migration: S32_AccountingOutboxProcessingLeases
-- Purpose: prevent concurrent accounting workers from posting the same outbox message.

ALTER TABLE "AccountingOutboxMessages"
    ADD COLUMN IF NOT EXISTS "ClaimedUntilUtc" timestamp with time zone NULL;

DROP INDEX IF EXISTS "IX_AccountingOutbox_ProcessingStatus";

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_ProcessingStatus"
    ON "AccountingOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc")
    WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424170000_S32_AccountingOutboxProcessingLeases', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424170000_S32_AccountingOutboxProcessingLeases'
);
