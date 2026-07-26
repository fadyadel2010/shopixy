-- Migration: S78_PosPurchaseAccountingMutationVersion
-- Purpose:
--   Add a monotonic purchase mutation version so purchase stock/accounting
--   reversal/repost work can reject stale outbox messages after concurrent edits.

ALTER TABLE "PurchaseInvoices"
    ADD COLUMN IF NOT EXISTS "AccountingMutationVersion" integer NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260508170000_S78_PosPurchaseAccountingMutationVersion', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260508170000_S78_PosPurchaseAccountingMutationVersion'
);
