BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
DECLARE
    max_order_number bigint;
    migration_exists boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260409210000_AddTenantScopedOrderNumberIndex'
    ) INTO migration_exists;

    IF NOT migration_exists THEN
        -- Create sequence if it doesn't exist (may have been created by earlier migration)
        IF NOT EXISTS (SELECT 1 FROM pg_sequences WHERE schemaname = 'public' AND sequencename = 'order_number_seq') THEN
            CREATE SEQUENCE order_number_seq
                START WITH 1000
                INCREMENT BY 1
                MINVALUE 1000;

            -- Initialize sequence from existing order numbers
            SELECT COALESCE(MAX((regexp_match("OrderNumber", '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-(\d+)$'))[1]::bigint), 0)
            INTO max_order_number
            FROM "Orders"
            WHERE "OrderNumber" ~ '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-\d+$';

            IF max_order_number < 1000 THEN
                PERFORM setval('order_number_seq', 1000, false);
            ELSE
                PERFORM setval('order_number_seq', max_order_number, true);
            END IF;
        END IF;

        -- Create temp table to track order number repairs
        CREATE TEMP TABLE "__order_number_repairs"
        (
            "OrderId" uuid PRIMARY KEY,
            "OldOrderNumber" text,
            "NewOrderNumber" text NOT NULL
        ) ON COMMIT DROP;

        -- Find and repair blank order numbers
        INSERT INTO "__order_number_repairs" ("OrderId", "OldOrderNumber", "NewOrderNumber")
        SELECT
            o."Id",
            o."OrderNumber",
            'ORD-' || to_char(
                COALESCE(
                    NULLIF(o."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    timezone('UTC', now())
                ), 'YYYYMMDD'
            ) || '-' || LPAD(nextval('order_number_seq')::text, 4, '0')
        FROM "Orders" o
        WHERE BTRIM(COALESCE(o."OrderNumber", '')) = '';

        -- Find and repair duplicate order numbers (keep first, reassign rest)
        INSERT INTO "__order_number_repairs" ("OrderId", "OldOrderNumber", "NewOrderNumber")
        SELECT
            dup."Id",
            dup."OrderNumber",
            'ORD-' || to_char(
                COALESCE(
                    NULLIF(dup."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    timezone('UTC', now())
                ), 'YYYYMMDD'
            ) || '-' || LPAD(nextval('order_number_seq')::text, 4, '0')
        FROM (
            SELECT
                o."Id",
                o."OrderNumber",
                o."CreatedAt",
                ROW_NUMBER() OVER (
                    PARTITION BY o."TenantId", o."OrderNumber"
                    ORDER BY COALESCE(NULLIF(o."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'), timezone('UTC', now())), o."Id"
                ) AS "DuplicateRank"
            FROM "Orders" o
            WHERE BTRIM(COALESCE(o."OrderNumber", '')) <> ''
        ) AS dup
        WHERE dup."DuplicateRank" > 1;

        -- Apply repairs to Orders table
        UPDATE "Orders" AS o
        SET "OrderNumber" = repair."NewOrderNumber",
            "UpdatedAt" = timezone('UTC', now())
        FROM "__order_number_repairs" AS repair
        WHERE o."Id" = repair."OrderId";

        -- Update related tables with new order numbers
        UPDATE "OrderItems" AS item
        SET "UpdatedAt" = timezone('UTC', now())
        FROM "__order_number_repairs" AS repair
        WHERE item."OrderId" = repair."OrderId";

        UPDATE "Shipments" AS shipment
        SET "UpdatedAt" = timezone('UTC', now())
        FROM "__order_number_repairs" AS repair
        WHERE shipment."OrderId" = repair."OrderId";

        -- Update PaymentIntents if table exists
        UPDATE "PaymentIntents" AS intent
        SET "UpdatedAt" = timezone('UTC', now())
        FROM "__order_number_repairs" AS repair
        WHERE intent."OrderId" = repair."OrderId"
          AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'PaymentIntents');

        UPDATE "AccountLedgers" AS ledger
        SET "DocumentNumber" = repair."NewOrderNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN ledger."Notes"
                ELSE replace(ledger."Notes", repair."OldOrderNumber", repair."NewOrderNumber")
            END
        FROM "__order_number_repairs" AS repair
        WHERE ledger."ReferenceId" = repair."OrderId";

        UPDATE "CustomerTransactions" AS customer_tx
        SET "ReferenceNumber" = repair."NewOrderNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN customer_tx."Notes"
                ELSE replace(customer_tx."Notes", repair."OldOrderNumber", repair."NewOrderNumber")
            END
        FROM "__order_number_repairs" AS repair
        WHERE customer_tx."ReferenceId" = repair."OrderId";

        UPDATE "CashboxTransactions" AS cashbox_tx
        SET "ReferenceNumber" = repair."NewOrderNumber",
            "Description" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN cashbox_tx."Description"
                ELSE replace(cashbox_tx."Description", repair."OldOrderNumber", repair."NewOrderNumber")
            END,
            "Notes" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN cashbox_tx."Notes"
                ELSE replace(cashbox_tx."Notes", repair."OldOrderNumber", repair."NewOrderNumber")
            END
        FROM "__order_number_repairs" AS repair
        WHERE cashbox_tx."ReferenceId" = repair."OrderId";

        UPDATE "AuditLogs" AS audit
        SET "ReferenceNumber" = repair."NewOrderNumber",
            "Description" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN audit."Description"
                ELSE replace(audit."Description", repair."OldOrderNumber", repair."NewOrderNumber")
            END,
            "BeforeDataJson" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN audit."BeforeDataJson"
                ELSE replace(audit."BeforeDataJson", repair."OldOrderNumber", repair."NewOrderNumber")
            END,
            "AfterDataJson" = CASE
                WHEN COALESCE(repair."OldOrderNumber", '') = '' THEN audit."AfterDataJson"
                ELSE replace(audit."AfterDataJson", repair."OldOrderNumber", repair."NewOrderNumber")
            END
        FROM "__order_number_repairs" AS repair
        WHERE audit."EntityId" = repair."OrderId"::text
          AND audit."EntityType" = 'Order';

        -- Drop the old global unique index
        DROP INDEX IF EXISTS "IX_Orders_OrderNumber";

        -- Create new tenant-scoped unique index
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_TenantId_OrderNumber"
            ON "Orders" ("TenantId", "OrderNumber")
            WHERE "IsDeleted" = FALSE;
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260409210000_AddTenantScopedOrderNumberIndex', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260409210000_AddTenantScopedOrderNumberIndex'
);

COMMIT;
