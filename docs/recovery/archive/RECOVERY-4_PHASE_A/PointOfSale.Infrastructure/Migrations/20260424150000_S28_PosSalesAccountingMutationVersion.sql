-- ============================================================================
-- Migration: S28_PosSalesAccountingMutationVersion
-- Description: Add mutation version to POS sales invoices so stale accounting
--              outbox messages cannot post after a newer sale edit/delete.
-- Date: 2026-04-24
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

ALTER TABLE "SalesInvoices"
    ADD COLUMN IF NOT EXISTS "AccountingMutationVersion" integer NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424150000_S28_PosSalesAccountingMutationVersion', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424150000_S28_PosSalesAccountingMutationVersion'
);

COMMIT;
