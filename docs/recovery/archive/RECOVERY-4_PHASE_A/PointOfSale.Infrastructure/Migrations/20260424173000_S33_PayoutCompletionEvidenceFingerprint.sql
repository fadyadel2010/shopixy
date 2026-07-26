-- Migration: S33_PayoutCompletionEvidenceFingerprint
-- Purpose: lock completed payout transfer evidence so terminal retries cannot drift from the recorded external transfer.

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "CompletedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "CompletionEvidenceFingerprint" character varying(128) NULL;

UPDATE "CommerceHubPayouts"
SET "CompletedAtUtc" = COALESCE("CompletedAtUtc", "ProcessedAtUtc", "UpdatedAt", "CreatedAt")
WHERE "Status" = 2
  AND "CompletedAtUtc" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424173000_S33_PayoutCompletionEvidenceFingerprint', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424173000_S33_PayoutCompletionEvidenceFingerprint'
);
