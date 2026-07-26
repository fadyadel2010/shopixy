-- S45: Backfill accounting outbox coverage for CommerceHub operational ledger rows.
-- Every CommerceHubFinancialTransaction must eventually produce a posted JournalEntry
-- with SourceType = 'CommerceHubFinancialTransaction'. New writes enqueue this in
-- CommerceHubFinancialLedgerService; this migration covers rows that already exist.

WITH ledger_rows AS (
    SELECT
        tx."Id",
        COALESCE(tx."EntityId", paired_vendor."EntityId") AS "AccountingTenantId",
        tx."TransactionType",
        tx."Direction",
        tx."EntityType",
        tx."FinancialBucket",
        tx."Amount",
        tx."Currency",
        tx."Description",
        tx."CreatedAtUtc",
        tx."CorrelationId"
    FROM "CommerceHubFinancialTransactions" tx
    LEFT JOIN LATERAL (
        SELECT vendor_tx."EntityId"
        FROM "CommerceHubFinancialTransactions" vendor_tx
        WHERE vendor_tx."EntityType" = 1
          AND vendor_tx."EntityId" IS NOT NULL
          AND vendor_tx."ReferenceType" = tx."ReferenceType"
          AND vendor_tx."ReferenceId" = tx."ReferenceId"
          AND vendor_tx."Currency" = tx."Currency"
          AND vendor_tx."Id" <> tx."Id"
        ORDER BY vendor_tx."CreatedAtUtc", vendor_tx."Id"
        LIMIT 1
    ) paired_vendor ON true
    WHERE NOT EXISTS (
        SELECT 1
        FROM "JournalEntries" journal
        WHERE journal."SourceType" = 'CommerceHubFinancialTransaction'
          AND journal."SourceId" = tx."Id"
          AND journal."Status" = 1
    )
)
INSERT INTO "AccountingOutboxMessages" (
    "Id",
    "TenantId",
    "EventType",
    "BusinessEntityId",
    "EntityTypeName",
    "PayloadJson",
    "IdempotencyKey",
    "OccurredAtUtc",
    "AttemptCount",
    "IsFailed",
    "CorrelationId",
    "CreatedAt",
    "UpdatedAt",
    "IsDeleted"
)
SELECT
    gen_random_uuid(),
    row."AccountingTenantId",
    'CommerceHubLedgerPosting',
    row."Id",
    'CommerceHubFinancialTransaction',
    jsonb_build_object(
        'TransactionId', row."Id",
        'TransactionType', CASE row."TransactionType"
            WHEN 0 THEN 'OrderPayment'
            WHEN 1 THEN 'Commission'
            WHEN 2 THEN 'Refund'
            WHEN 3 THEN 'Payout'
            WHEN 4 THEN 'Adjustment'
            WHEN 5 THEN 'Settlement'
            WHEN 6 THEN 'Promotion'
        END,
        'Direction', CASE row."Direction"
            WHEN 0 THEN 'Credit'
            WHEN 1 THEN 'Debit'
        END,
        'EntityType', CASE row."EntityType"
            WHEN 0 THEN 'Platform'
            WHEN 1 THEN 'Vendor'
        END,
        'FinancialBucket', CASE row."FinancialBucket"
            WHEN 0 THEN 'Cash'
            WHEN 1 THEN 'Receivable'
            WHEN 2 THEN 'Payable'
        END,
        'Amount', row."Amount",
        'Currency', row."Currency",
        'Description', row."Description",
        'OccurredAtUtc', row."CreatedAtUtc"
    )::text,
    'COMMERCEHUB_TX:' || replace(row."AccountingTenantId"::text, '-', '') || ':' || replace(row."Id"::text, '-', ''),
    COALESCE(row."CreatedAtUtc", NOW()),
    0,
    false,
    row."CorrelationId",
    NOW(),
    NOW(),
    false
FROM ledger_rows row
WHERE row."AccountingTenantId" IS NOT NULL
  AND row."AccountingTenantId" <> '00000000-0000-0000-0000-000000000000'::uuid
ON CONFLICT ("TenantId", "IdempotencyKey") DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260428123000_S45_CommerceHubLedgerPostingOutboxBackfill', '9.0.1'
WHERE EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_name = '__EFMigrationsHistory'
)
AND NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260428123000_S45_CommerceHubLedgerPostingOutboxBackfill'
);
