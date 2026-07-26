-- S47b: Accounting outbox source provenance (Part 2 - Constraints and Indexes)
-- Applies NOT NULL constraint and creates index on SourceType column.
--
-- MIGRATION SPLIT: This is part 2 of 2. Part 1 (S47) added the column and backfilled data.
-- See S47_MIGRATION_SPLIT_README.md for details.
--
-- IMPORTANT: This migration should run non-transactionally (outside a transaction)
-- to avoid holding table locks during constraint validation and index creation.

-- Step 1: Set NOT NULL constraint
-- PostgreSQL will validate all existing rows, but since S47 backfilled them,
-- this should be fast
ALTER TABLE "AccountingOutboxMessages"
    ALTER COLUMN "SourceType" SET NOT NULL;

-- Step 2: Create index
-- This runs outside a transaction to avoid blocking writes
CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_Tenant_SourceType_Entity"
    ON "AccountingOutboxMessages" ("TenantId", "SourceType", "BusinessEntityId");
