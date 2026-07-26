BEGIN;

ALTER TABLE "PurchaseRequestIdempotency"
    ALTER COLUMN "Key" TYPE character varying(255);

ALTER TABLE "PurchaseReturnRequestIdempotency"
    ALTER COLUMN "Key" TYPE character varying(255);

ALTER TABLE "SalesRequestIdempotency"
    ALTER COLUMN "Key" TYPE character varying(200);

ALTER TABLE "CommerceHubCheckoutIdempotency"
    ALTER COLUMN "Key" TYPE character varying(255);

DO $$
BEGIN
    -- Normalize supplier opening-balance markers and soft-delete duplicate active records
    -- before the unique indexes are created.
    UPDATE "SupplierTransactions"
    SET "IsOpeningBalance" = TRUE,
        "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE "IsDeleted" = false
      AND "TransactionType" = 11
      AND "IsOpeningBalance" = false;

    -- Capture affected parties from SupplierTransactions deduplication
    CREATE TEMP TABLE temp_affected_supplier_transactions (
        "TenantId" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        PRIMARY KEY ("TenantId", "SupplierId")
    ) ON COMMIT DROP;

    WITH ranked_supplier_transactions AS (
        SELECT
            st."Id",
            st."TenantId",
            st."SupplierId",
            ROW_NUMBER() OVER (
                PARTITION BY st."TenantId", st."SupplierId"
                ORDER BY
                    CASE
                        WHEN s."OpeningBalanceAmount" IS NOT NULL
                             AND ABS(st."DebitAmount" - st."CreditAmount") = s."OpeningBalanceAmount"
                        THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN COALESCE(BTRIM(s."BalanceType"), '') = '' THEN 0
                        WHEN s."BalanceType" = 'Debit' AND st."DebitAmount" >= st."CreditAmount" THEN 0
                        WHEN s."BalanceType" = 'Credit' AND st."CreditAmount" > st."DebitAmount" THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN s."OpeningBalanceDate" IS NOT NULL
                             AND st."TransactionDate" = s."OpeningBalanceDate"
                        THEN 0
                        ELSE 1
                    END,
                    st."TransactionDate",
                    st."CreatedAt",
                    st."Id"
            ) AS "RowNumber"
        FROM "SupplierTransactions" st
        INNER JOIN "Suppliers" s
            ON s."TenantId" = st."TenantId"
           AND s."Id" = st."SupplierId"
        WHERE st."IsDeleted" = false
          AND st."TransactionType" = 11
    )
    INSERT INTO temp_affected_supplier_transactions ("TenantId", "SupplierId")
    SELECT "TenantId", "SupplierId"
    FROM ranked_supplier_transactions
    GROUP BY "TenantId", "SupplierId"
    HAVING COUNT(*) > 1;

    UPDATE "SupplierTransactions" st
    SET "IsDeleted" = true,
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM (
        SELECT
            st."Id",
            ROW_NUMBER() OVER (
                PARTITION BY st."TenantId", st."SupplierId"
                ORDER BY
                    CASE
                        WHEN s."OpeningBalanceAmount" IS NOT NULL
                             AND ABS(st."DebitAmount" - st."CreditAmount") = s."OpeningBalanceAmount"
                        THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN COALESCE(BTRIM(s."BalanceType"), '') = '' THEN 0
                        WHEN s."BalanceType" = 'Debit' AND st."DebitAmount" >= st."CreditAmount" THEN 0
                        WHEN s."BalanceType" = 'Credit' AND st."CreditAmount" > st."DebitAmount" THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN s."OpeningBalanceDate" IS NOT NULL
                             AND st."TransactionDate" = s."OpeningBalanceDate"
                        THEN 0
                        ELSE 1
                    END,
                    st."TransactionDate",
                    st."CreatedAt",
                    st."Id"
            ) AS "RowNumber"
        FROM "SupplierTransactions" st
        INNER JOIN "Suppliers" s
            ON s."TenantId" = st."TenantId"
           AND s."Id" = st."SupplierId"
        WHERE st."IsDeleted" = false
          AND st."TransactionType" = 11
    ) ranked
    WHERE st."Id" = ranked."Id"
      AND ranked."RowNumber" > 1;

    -- Capture affected parties from AccountLedgers deduplication
    CREATE TEMP TABLE temp_affected_ledger_parties (
        "TenantId" uuid NOT NULL,
        "PartyId" uuid NOT NULL,
        PRIMARY KEY ("TenantId", "PartyId")
    ) ON COMMIT DROP;

    WITH ranked_supplier_ledgers AS (
        SELECT
            al."Id",
            al."TenantId",
            al."PartyId",
            ROW_NUMBER() OVER (
                PARTITION BY al."TenantId", al."PartyId"
                ORDER BY
                    CASE
                        WHEN s."OpeningBalanceAmount" IS NOT NULL
                             AND ABS(al."DebitAmount" - al."CreditAmount") = s."OpeningBalanceAmount"
                        THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN COALESCE(BTRIM(s."BalanceType"), '') = '' THEN 0
                        WHEN s."BalanceType" = 'Debit' AND al."DebitAmount" >= al."CreditAmount" THEN 0
                        WHEN s."BalanceType" = 'Credit' AND al."CreditAmount" > al."DebitAmount" THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN s."OpeningBalanceDate" IS NOT NULL
                             AND al."TransactionDate" = s."OpeningBalanceDate"
                        THEN 0
                        ELSE 1
                    END,
                    al."TransactionDate",
                    al."CreatedAt",
                    al."Id"
            ) AS "RowNumber"
        FROM "AccountLedgers" al
        INNER JOIN "Suppliers" s
            ON s."TenantId" = al."TenantId"
           AND s."Id" = al."PartyId"
        WHERE al."IsDeleted" = false
          AND al."PartyType" = 1
          AND al."TransactionType" = 11
    )
    INSERT INTO temp_affected_ledger_parties ("TenantId", "PartyId")
    SELECT "TenantId", "PartyId"
    FROM ranked_supplier_ledgers
    GROUP BY "TenantId", "PartyId"
    HAVING COUNT(*) > 1;

    UPDATE "AccountLedgers" al
    SET "IsDeleted" = true,
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM (
        SELECT
            al."Id",
            ROW_NUMBER() OVER (
                PARTITION BY al."TenantId", al."PartyId"
                ORDER BY
                    CASE
                        WHEN s."OpeningBalanceAmount" IS NOT NULL
                             AND ABS(al."DebitAmount" - al."CreditAmount") = s."OpeningBalanceAmount"
                        THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN COALESCE(BTRIM(s."BalanceType"), '') = '' THEN 0
                        WHEN s."BalanceType" = 'Debit' AND al."DebitAmount" >= al."CreditAmount" THEN 0
                        WHEN s."BalanceType" = 'Credit' AND al."CreditAmount" > al."DebitAmount" THEN 0
                        ELSE 1
                    END,
                    CASE
                        WHEN s."OpeningBalanceDate" IS NOT NULL
                             AND al."TransactionDate" = s."OpeningBalanceDate"
                        THEN 0
                        ELSE 1
                    END,
                    al."TransactionDate",
                    al."CreatedAt",
                    al."Id"
            ) AS "RowNumber"
        FROM "AccountLedgers" al
        INNER JOIN "Suppliers" s
            ON s."TenantId" = al."TenantId"
           AND s."Id" = al."PartyId"
        WHERE al."IsDeleted" = false
          AND al."PartyType" = 1
          AND al."TransactionType" = 11
    ) ranked
    WHERE al."Id" = ranked."Id"
      AND ranked."RowNumber" > 1;

    -- Use captured affected parties for windowed updates
    WITH ordered_supplier_ledgers AS (
        SELECT
            al."Id",
            SUM(al."DebitAmount" - al."CreditAmount") OVER (
                PARTITION BY al."TenantId", al."PartyId"
                ORDER BY al."TransactionDate", al."CreatedAt", al."Id"
            ) AS "RunningBalance"
        FROM "AccountLedgers" al
        INNER JOIN temp_affected_ledger_parties affected
            ON affected."TenantId" = al."TenantId"
           AND affected."PartyId" = al."PartyId"
        WHERE al."IsDeleted" = false
          AND al."PartyType" = 1
    )
    UPDATE "AccountLedgers" al
    SET "RunningBalance" = ordered_supplier_ledgers."RunningBalance",
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM ordered_supplier_ledgers
    WHERE al."Id" = ordered_supplier_ledgers."Id";

    -- Use captured affected suppliers for windowed updates
    WITH ordered_supplier_transactions AS (
        SELECT
            st."Id",
            SUM(st."DebitAmount" - st."CreditAmount") OVER (
                PARTITION BY st."TenantId", st."SupplierId"
                ORDER BY st."TransactionDate", st."CreatedAt", st."Id"
            ) AS "BalanceAfter"
        FROM "SupplierTransactions" st
        INNER JOIN temp_affected_supplier_transactions affected
            ON affected."TenantId" = st."TenantId"
           AND affected."SupplierId" = st."SupplierId"
        WHERE st."IsDeleted" = false
    )
    UPDATE "SupplierTransactions" st
    SET "BalanceAfter" = ordered_supplier_transactions."BalanceAfter",
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM ordered_supplier_transactions
    WHERE st."Id" = ordered_supplier_transactions."Id";

    WITH supplier_balances AS (
        SELECT
            s."TenantId",
            s."Id" AS "SupplierId",
            COALESCE(SUM(al."DebitAmount" - al."CreditAmount"), 0) AS "Balance"
        FROM "Suppliers" s
        LEFT JOIN "AccountLedgers" al
            ON al."TenantId" = s."TenantId"
           AND al."PartyType" = 1
           AND al."PartyId" = s."Id"
           AND al."IsDeleted" = false
        GROUP BY s."TenantId", s."Id"
    )
    UPDATE "Suppliers" s
    SET "Balance" = supplier_balances."Balance",
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM supplier_balances
    WHERE s."TenantId" = supplier_balances."TenantId"
      AND s."Id" = supplier_balances."SupplierId";

    WITH authoritative_openings AS (
        SELECT DISTINCT ON (opening_source."TenantId", opening_source."SupplierId")
            opening_source."TenantId",
            opening_source."SupplierId",
            ABS(opening_source."DebitAmount" - opening_source."CreditAmount") AS "OpeningBalanceAmount",
            CASE
                WHEN opening_source."DebitAmount" - opening_source."CreditAmount" < 0 THEN 'Credit'
                ELSE 'Debit'
            END AS "BalanceType",
            opening_source."TransactionDate" AS "OpeningBalanceDate"
        FROM (
            SELECT
                st."TenantId",
                st."SupplierId",
                st."TransactionDate",
                st."DebitAmount",
                st."CreditAmount",
                st."CreatedAt",
                st."Id",
                0 AS "SourcePriority"
            FROM "SupplierTransactions" st
            WHERE st."IsDeleted" = false
              AND st."TransactionType" = 11

            UNION ALL

            SELECT
                al."TenantId",
                al."PartyId" AS "SupplierId",
                al."TransactionDate",
                al."DebitAmount",
                al."CreditAmount",
                al."CreatedAt",
                al."Id",
                1 AS "SourcePriority"
            FROM "AccountLedgers" al
            WHERE al."IsDeleted" = false
              AND al."PartyType" = 1
              AND al."TransactionType" = 11
        ) opening_source
        ORDER BY
            opening_source."TenantId",
            opening_source."SupplierId",
            opening_source."SourcePriority",
            opening_source."TransactionDate",
            opening_source."CreatedAt",
            opening_source."Id"
    )
    UPDATE "Suppliers" s
    SET "OpeningBalanceAmount" = authoritative_openings."OpeningBalanceAmount",
        "BalanceType" = authoritative_openings."BalanceType",
        "OpeningBalanceDate" = authoritative_openings."OpeningBalanceDate",
        "UpdatedAt" = CURRENT_TIMESTAMP
    FROM authoritative_openings
    WHERE s."TenantId" = authoritative_openings."TenantId"
      AND s."Id" = authoritative_openings."SupplierId";

    WITH authoritative_openings AS (
        SELECT DISTINCT ON (opening_source."TenantId", opening_source."SupplierId")
            opening_source."TenantId",
            opening_source."SupplierId"
        FROM (
            SELECT
                st."TenantId",
                st."SupplierId",
                st."TransactionDate",
                st."CreatedAt",
                st."Id",
                0 AS "SourcePriority"
            FROM "SupplierTransactions" st
            WHERE st."IsDeleted" = false
              AND st."TransactionType" = 11

            UNION ALL

            SELECT
                al."TenantId",
                al."PartyId" AS "SupplierId",
                al."TransactionDate",
                al."CreatedAt",
                al."Id",
                1 AS "SourcePriority"
            FROM "AccountLedgers" al
            WHERE al."IsDeleted" = false
              AND al."PartyType" = 1
              AND al."TransactionType" = 11
        ) opening_source
        ORDER BY
            opening_source."TenantId",
            opening_source."SupplierId",
            opening_source."SourcePriority",
            opening_source."TransactionDate",
            opening_source."CreatedAt",
            opening_source."Id"
    )
    UPDATE "Suppliers" s
    SET "OpeningBalanceAmount" = NULL,
        "BalanceType" = NULL,
        "OpeningBalanceDate" = NULL,
        "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE NOT EXISTS (
        SELECT 1
        FROM authoritative_openings ao
        WHERE ao."TenantId" = s."TenantId"
          AND ao."SupplierId" = s."Id")
      AND (
          s."OpeningBalanceAmount" IS NOT NULL OR
          s."BalanceType" IS NOT NULL OR
          s."OpeningBalanceDate" IS NOT NULL);
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountLedgers_SupplierOpeningBalance_Once"
    ON "AccountLedgers" ("TenantId", "PartyType", "PartyId", "TransactionType")
    WHERE "IsDeleted" = false
      AND "PartyType" = 1
      AND "TransactionType" = 11;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SupplierTransactions_OpeningBalance_Once"
    ON "SupplierTransactions" ("TenantId", "SupplierId", "TransactionType")
    WHERE "IsDeleted" = false
      AND "TransactionType" = 11;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260410120000_HardenSupplierOpeningBalanceIdempotency', '8.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
