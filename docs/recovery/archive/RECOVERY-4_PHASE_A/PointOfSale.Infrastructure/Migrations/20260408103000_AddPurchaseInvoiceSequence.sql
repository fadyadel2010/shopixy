BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
DECLARE 
    max_purchase_number bigint;
    migration_exists boolean;
BEGIN
    -- Check if this migration has already been applied
    SELECT EXISTS (
        SELECT 1 
        FROM "__EFMigrationsHistory" 
        WHERE "MigrationId" = '20260408103000_AddPurchaseInvoiceSequence'
    ) INTO migration_exists;

    -- Only run if migration hasn't been recorded yet
    IF NOT migration_exists THEN
        CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000;

        SELECT COALESCE(MAX((regexp_match("InvoiceNumber", '^PUR-\d{8}-(\d+)$'))[1]::bigint), 0)
        INTO max_purchase_number
        FROM "PurchaseInvoices"
        WHERE "InvoiceNumber" ~ '^PUR-\d{8}-\d+$';

        IF max_purchase_number < 1000 THEN
            PERFORM setval('purchase_invoice_seq', 1000, false);
        ELSE
            PERFORM setval('purchase_invoice_seq', max_purchase_number, true);
        END IF;
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260408103000_AddPurchaseInvoiceSequence', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260408103000_AddPurchaseInvoiceSequence'
);

COMMIT;
