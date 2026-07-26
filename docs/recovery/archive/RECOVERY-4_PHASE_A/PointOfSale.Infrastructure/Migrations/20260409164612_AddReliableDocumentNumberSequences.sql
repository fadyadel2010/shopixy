BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
DECLARE
    max_return_number bigint;
    max_order_number bigint;
    max_transfer_number bigint;
    migration_exists boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260409164612_AddReliableDocumentNumberSequences'
    ) INTO migration_exists;

    IF NOT migration_exists THEN
        CREATE SEQUENCE IF NOT EXISTS return_number_seq
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000;

        SELECT COALESCE(MAX((regexp_match("ReturnNumber", '^(?:PR|SR)-\d{8}-(\d+)$'))[1]::bigint), 0)
        INTO max_return_number
        FROM "Returns"
        WHERE "ReturnNumber" ~ '^(?:PR|SR)-\d{8}-\d+$';

        IF max_return_number < 1000 THEN
            PERFORM setval('return_number_seq', 1000, false);
        ELSE
            PERFORM setval('return_number_seq', max_return_number, true);
        END IF;

        CREATE SEQUENCE IF NOT EXISTS order_number_seq
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000;

        SELECT COALESCE(MAX((regexp_match("OrderNumber", '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-(\d+)$'))[1]::bigint), 0)
        INTO max_order_number
        FROM "Orders"
        WHERE "OrderNumber" ~ '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-\d+$';

        IF max_order_number < 1000 THEN
            PERFORM setval('order_number_seq', 1000, false);
        ELSE
            PERFORM setval('order_number_seq', max_order_number, true);
        END IF;

        CREATE SEQUENCE IF NOT EXISTS stock_transfer_number_seq
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000;

        SELECT COALESCE(MAX((regexp_match("TransferNumber", '^TRF-\d{8}-(\d+)$'))[1]::bigint), 0)
        INTO max_transfer_number
        FROM "StockTransfers"
        WHERE "TransferNumber" ~ '^TRF-\d{8}-\d+$';

        IF max_transfer_number < 1000 THEN
            PERFORM setval('stock_transfer_number_seq', 1000, false);
        ELSE
            PERFORM setval('stock_transfer_number_seq', max_transfer_number, true);
        END IF;

        CREATE TEMP TABLE "__return_number_repairs"
        (
            "ReturnId" uuid PRIMARY KEY,
            "OldReturnNumber" text,
            "NewReturnNumber" text NOT NULL
        ) ON COMMIT DROP;

        INSERT INTO "__return_number_repairs" ("ReturnId", "OldReturnNumber", "NewReturnNumber")
        WITH blank_returns AS (
            SELECT
                r."Id",
                r."ReturnNumber",
                r."ReturnType",
                COALESCE(
                    NULLIF(r."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    r."ReturnDate",
                    timezone('UTC', now())) AS "EffectiveTimestamp"
            FROM "Returns" r
            WHERE BTRIM(COALESCE(r."ReturnNumber", '')) = ''
        ),
        duplicate_returns AS (
            SELECT
                r."Id",
                r."ReturnNumber",
                r."ReturnType",
                COALESCE(
                    NULLIF(r."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    r."ReturnDate",
                    timezone('UTC', now())) AS "EffectiveTimestamp",
                ROW_NUMBER() OVER (
                    PARTITION BY r."TenantId", r."ReturnNumber"
                    ORDER BY COALESCE(NULLIF(r."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'), r."ReturnDate"), r."Id") AS "DuplicateRank"
            FROM "Returns" r
            WHERE BTRIM(COALESCE(r."ReturnNumber", '')) <> ''
        )
        SELECT
            repair_target."Id",
            repair_target."ReturnNumber",
            (CASE WHEN repair_target."ReturnType" = 0 THEN 'PR' ELSE 'SR' END)
            || '-' || to_char(repair_target."EffectiveTimestamp", 'YYYYMMDD')
            || '-' || LPAD(nextval('return_number_seq')::text, 4, '0')
        FROM (
            SELECT * FROM blank_returns
            UNION ALL
            SELECT "Id", "ReturnNumber", "ReturnType", "EffectiveTimestamp"
            FROM duplicate_returns
            WHERE "DuplicateRank" > 1
        ) AS repair_target;

        UPDATE "Returns" AS r
        SET "ReturnNumber" = repair."NewReturnNumber",
            "UpdatedAt" = timezone('UTC', now())
        FROM "__return_number_repairs" AS repair
        WHERE r."Id" = repair."ReturnId";

        UPDATE "StockMovements" AS movement
        SET "ReferenceNumber" = repair."NewReturnNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN movement."Notes"
                ELSE replace(movement."Notes", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE movement."ReferenceId" = repair."ReturnId";

        UPDATE "AccountLedgers" AS ledger
        SET "DocumentNumber" = repair."NewReturnNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN ledger."Notes"
                ELSE replace(ledger."Notes", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE ledger."ReferenceId" = repair."ReturnId";

        UPDATE "CustomerTransactions" AS customer_tx
        SET "ReferenceNumber" = repair."NewReturnNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN customer_tx."Notes"
                ELSE replace(customer_tx."Notes", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE customer_tx."ReferenceId" = repair."ReturnId";

        UPDATE "SupplierTransactions" AS supplier_tx
        SET "ReferenceNumber" = repair."NewReturnNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN supplier_tx."Notes"
                ELSE replace(supplier_tx."Notes", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE supplier_tx."ReferenceId" = repair."ReturnId";

        UPDATE "CashboxTransactions" AS cashbox_tx
        SET "ReferenceNumber" = repair."NewReturnNumber",
            "Description" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN cashbox_tx."Description"
                ELSE replace(cashbox_tx."Description", repair."OldReturnNumber", repair."NewReturnNumber")
            END,
            "Notes" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN cashbox_tx."Notes"
                ELSE replace(cashbox_tx."Notes", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE cashbox_tx."ReferenceId" = repair."ReturnId";

        UPDATE "AuditLogs" AS audit
        SET "ReferenceNumber" = repair."NewReturnNumber",
            "Description" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN audit."Description"
                ELSE replace(audit."Description", repair."OldReturnNumber", repair."NewReturnNumber")
            END,
            "BeforeDataJson" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN audit."BeforeDataJson"
                ELSE replace(audit."BeforeDataJson", repair."OldReturnNumber", repair."NewReturnNumber")
            END,
            "AfterDataJson" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN audit."AfterDataJson"
                ELSE replace(audit."AfterDataJson", repair."OldReturnNumber", repair."NewReturnNumber")
            END
        FROM "__return_number_repairs" AS repair
        WHERE audit."EntityId" = repair."ReturnId"::text
          AND audit."EntityType" IN ('Return', 'SalesReturn', 'PurchaseReturn');

        UPDATE "PurchaseReturnRequestIdempotency" AS request
        SET "ResponseJson" = CASE
                WHEN COALESCE(repair."OldReturnNumber", '') = '' THEN request."ResponseJson"
                ELSE replace(request."ResponseJson", repair."OldReturnNumber", repair."NewReturnNumber")
            END,
            "UpdatedAt" = timezone('UTC', now())
        FROM "__return_number_repairs" AS repair
        WHERE request."ReturnId" = repair."ReturnId";

        CREATE TEMP TABLE "__stock_transfer_number_repairs"
        (
            "StockTransferId" uuid PRIMARY KEY,
            "OldTransferNumber" text,
            "NewTransferNumber" text NOT NULL
        ) ON COMMIT DROP;

        INSERT INTO "__stock_transfer_number_repairs" ("StockTransferId", "OldTransferNumber", "NewTransferNumber")
        WITH blank_transfers AS (
            SELECT
                t."Id",
                t."TransferNumber",
                COALESCE(
                    NULLIF(t."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    t."TransferDate",
                    timezone('UTC', now())) AS "EffectiveTimestamp"
            FROM "StockTransfers" t
            WHERE BTRIM(COALESCE(t."TransferNumber", '')) = ''
        ),
        duplicate_transfers AS (
            SELECT
                t."Id",
                t."TransferNumber",
                COALESCE(
                    NULLIF(t."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'),
                    t."TransferDate",
                    timezone('UTC', now())) AS "EffectiveTimestamp",
                ROW_NUMBER() OVER (
                    PARTITION BY t."TenantId", t."TransferNumber"
                    ORDER BY COALESCE(NULLIF(t."CreatedAt", TIMESTAMP '0001-01-01 00:00:00'), t."TransferDate"), t."Id") AS "DuplicateRank"
            FROM "StockTransfers" t
            WHERE BTRIM(COALESCE(t."TransferNumber", '')) <> ''
        )
        SELECT
            repair_target."Id",
            repair_target."TransferNumber",
            'TRF-' || to_char(repair_target."EffectiveTimestamp", 'YYYYMMDD')
            || '-' || LPAD(nextval('stock_transfer_number_seq')::text, 4, '0')
        FROM (
            SELECT * FROM blank_transfers
            UNION ALL
            SELECT "Id", "TransferNumber", "EffectiveTimestamp"
            FROM duplicate_transfers
            WHERE "DuplicateRank" > 1
        ) AS repair_target;

        UPDATE "StockTransfers" AS transfer
        SET "TransferNumber" = repair."NewTransferNumber",
            "UpdatedAt" = timezone('UTC', now())
        FROM "__stock_transfer_number_repairs" AS repair
        WHERE transfer."Id" = repair."StockTransferId";

        UPDATE "StockMovements" AS movement
        SET "ReferenceNumber" = repair."NewTransferNumber",
            "Notes" = CASE
                WHEN COALESCE(repair."OldTransferNumber", '') = '' THEN movement."Notes"
                ELSE replace(movement."Notes", repair."OldTransferNumber", repair."NewTransferNumber")
            END
        FROM "__stock_transfer_number_repairs" AS repair
        WHERE movement."ReferenceId" = repair."StockTransferId";

        UPDATE "AuditLogs" AS audit
        SET "ReferenceNumber" = repair."NewTransferNumber",
            "Description" = CASE
                WHEN COALESCE(repair."OldTransferNumber", '') = '' THEN audit."Description"
                ELSE replace(audit."Description", repair."OldTransferNumber", repair."NewTransferNumber")
            END,
            "BeforeDataJson" = CASE
                WHEN COALESCE(repair."OldTransferNumber", '') = '' THEN audit."BeforeDataJson"
                ELSE replace(audit."BeforeDataJson", repair."OldTransferNumber", repair."NewTransferNumber")
            END,
            "AfterDataJson" = CASE
                WHEN COALESCE(repair."OldTransferNumber", '') = '' THEN audit."AfterDataJson"
                ELSE replace(audit."AfterDataJson", repair."OldTransferNumber", repair."NewTransferNumber")
            END
        FROM "__stock_transfer_number_repairs" AS repair
        WHERE audit."EntityId" = repair."StockTransferId"::text
          AND audit."EntityType" = 'StockTransfer';

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Returns_TenantId_ReturnNumber"
            ON "Returns" ("TenantId", "ReturnNumber")
            WHERE "IsDeleted" = FALSE;

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockTransfers_TenantId_TransferNumber"
            ON "StockTransfers" ("TenantId", "TransferNumber")
            WHERE "IsDeleted" = FALSE;
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260409164612_AddReliableDocumentNumberSequences', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260409164612_AddReliableDocumentNumberSequences'
);

COMMIT;
