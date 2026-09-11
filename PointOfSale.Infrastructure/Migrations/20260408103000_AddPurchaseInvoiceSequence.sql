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
    current_last_value bigint;
    current_is_called boolean;
    current_next_value bigint;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260408103000_AddPurchaseInvoiceSequence'
    ) THEN
        CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000
            CACHE 1;

        IF to_regclass('public."PurchaseInvoices"') IS NULL THEN
            RETURN;
        END IF;

        SELECT COALESCE(MAX((regexp_match("InvoiceNumber", '^PUR-\d{8}-(\d+)$'))[1]::bigint), 0)
        INTO max_purchase_number
        FROM "PurchaseInvoices"
        WHERE "InvoiceNumber" ~ '^PUR-\d{8}-\d+$';

        SELECT last_value, is_called
        INTO current_last_value, current_is_called
        FROM purchase_invoice_seq;

        current_next_value := CASE
            WHEN current_is_called THEN current_last_value + 1
            ELSE current_last_value
        END;

        IF max_purchase_number < 1000 THEN
            IF current_next_value < 1000 THEN
                PERFORM setval('purchase_invoice_seq', 1000, false);
            END IF;
        ELSIF current_next_value <= max_purchase_number THEN
            PERFORM setval('purchase_invoice_seq', max_purchase_number, true);
        END IF;
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260408103000_AddPurchaseInvoiceSequence', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260408103000_AddPurchaseInvoiceSequence'
);

COMMIT;
