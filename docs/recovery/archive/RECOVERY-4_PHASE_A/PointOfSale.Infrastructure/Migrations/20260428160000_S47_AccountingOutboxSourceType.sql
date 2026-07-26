-- S47: Accounting outbox source provenance (Part 1 - Column Addition and Backfill)
-- Adds the source type used by recovery/reconciliation flows to identify the
-- exact accounting authority record behind each outbox message.
--
-- MIGRATION SPLIT: This is part 1 of 2. Part 2 (S47b) adds constraints and indexes.
-- See S47_MIGRATION_SPLIT_README.md for details.

-- Step 1: Add nullable column (fast, no data copy)
ALTER TABLE "AccountingOutboxMessages"
    ADD COLUMN IF NOT EXISTS "SourceType" character varying(50);

-- Step 2: Validate prerequisites
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "AccountingOutboxMessages"
        WHERE ("SourceType" IS NULL OR btrim("SourceType") = '')
          AND ("EventType" IS NULL OR btrim("EventType") = '')
    ) THEN
        RAISE EXCEPTION 'Migration S47 failed: Found AccountingOutboxMessages with NULL/empty EventType. Manual data fix required before migration can proceed.';
    END IF;
END $$;

-- Step 3: Backfill SourceType values
UPDATE "AccountingOutboxMessages"
SET "SourceType" = CASE "EventType"
    WHEN 'CommerceHubLedgerPosting' THEN 'CommerceHubFinancialTransaction'
    WHEN 'PosSale' THEN 'SalesInvoice'
    WHEN 'PosPurchase' THEN 'PurchaseInvoice'
    WHEN 'PosRefund' THEN 'Return'
    ELSE 
        -- Replicate ResolveDefaultSourceType fallback logic:
        -- Prefer EntityTypeName if not null/whitespace and length <= 50
        -- Otherwise use EventType, truncated to 50 chars
        CASE 
            WHEN "EntityTypeName" IS NOT NULL 
                 AND btrim("EntityTypeName") <> '' 
                 AND length("EntityTypeName") <= 50 
            THEN "EntityTypeName"
            ELSE LEFT("EventType", 50)
        END
END
WHERE "SourceType" IS NULL OR btrim("SourceType") = '';

-- NOTE: NOT NULL constraint and index creation moved to S47b migration
-- to avoid holding table locks during the backfill operation
