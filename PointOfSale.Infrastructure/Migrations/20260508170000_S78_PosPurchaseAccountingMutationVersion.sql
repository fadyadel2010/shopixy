BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
BEGIN
    IF to_regclass('public."PurchaseInvoices"') IS NULL THEN
        RETURN;
    END IF;

    ALTER TABLE "PurchaseInvoices"
        ADD COLUMN IF NOT EXISTS "AccountingMutationVersion" integer NOT NULL DEFAULT 0;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260508170000_S78_PosPurchaseAccountingMutationVersion', '9.0.1'
WHERE to_regclass('public."PurchaseInvoices"') IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20260508170000_S78_PosPurchaseAccountingMutationVersion'
  );

COMMIT;
