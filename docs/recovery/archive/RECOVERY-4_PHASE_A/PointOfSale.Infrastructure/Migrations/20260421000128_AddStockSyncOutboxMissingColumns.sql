BEGIN;

-- Drop the existing index that doesn't include ClaimedUntilUtc
DROP INDEX IF EXISTS "IX_StockSyncOutbox_ProcessingStatus";

-- Add ClaimedUntilUtc column for claim-based processing
ALTER TABLE "StockSyncOutboxMessages"
ADD COLUMN IF NOT EXISTS "ClaimedUntilUtc" timestamp with time zone NULL;

-- Note: xmin is a PostgreSQL system column that exists automatically for all tables
-- It provides automatic row versioning and is used for optimistic concurrency control
-- No explicit column creation is needed - EF Core will map to the existing xmin column

-- Recreate the index with ClaimedUntilUtc included for efficient claim-based queries
CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_ProcessingStatus"
    ON "StockSyncOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = FALSE;

COMMIT;
