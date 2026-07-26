-- Add ClaimedUntilUtc column to StockSyncOutboxMessages table
-- This enables atomic claim-before-publish pattern to prevent duplicate message publishing

-- Ensure the table exists first (run 20260420114500_AddStockSyncOutboxReliability.sql if not)
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables 
                   WHERE table_name = 'StockSyncOutboxMessages') THEN
        RAISE EXCEPTION 'Table StockSyncOutboxMessages does not exist. Please run migration 20260420114500_AddStockSyncOutboxReliability.sql first.';
    END IF;
END $$;

-- Add the column (nullable, defaults to NULL)
ALTER TABLE "StockSyncOutboxMessages"
ADD COLUMN IF NOT EXISTS "ClaimedUntilUtc" timestamp without time zone NULL;

-- Drop the old processing status index if it exists
DROP INDEX IF EXISTS "IX_StockSyncOutbox_ProcessingStatus";

-- Recreate processing status index without time-based filter
-- (time-based filtering happens in the query, not the index predicate)
CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_ProcessingStatus" 
ON "StockSyncOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc")
WHERE "PublishedAtUtc" IS NULL 
  AND "IsFailed" = false;

-- Create an additional index for efficient scanning by occurrence time
-- Include ClaimedUntilUtc in the index columns for index-only scans
CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_Claimable" 
ON "StockSyncOutboxMessages" ("OccurredAtUtc", "ClaimedUntilUtc", "NextRetryAtUtc")
WHERE "PublishedAtUtc" IS NULL 
  AND "IsFailed" = false;

COMMENT ON COLUMN "StockSyncOutboxMessages"."ClaimedUntilUtc" IS 
'Timestamp until which this message is claimed by a worker. Prevents duplicate publishing by multiple workers.';
