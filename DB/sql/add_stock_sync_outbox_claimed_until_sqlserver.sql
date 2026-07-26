-- Add ClaimedUntilUtc column to StockSyncOutboxMessages table (SQL Server version)
-- This enables atomic claim-before-publish pattern to prevent duplicate message publishing

-- Add the column (nullable, defaults to NULL)
ALTER TABLE [StockSyncOutboxMessages]
ADD [ClaimedUntilUtc] datetime2 NULL;
GO

-- Drop the old processing status index if it exists
DROP INDEX IF EXISTS [IX_StockSyncOutbox_ProcessingStatus] ON [StockSyncOutboxMessages];
GO

-- Recreate processing status index without time-based filter
-- (time-based filtering happens in the query, not the index predicate)
CREATE INDEX [IX_StockSyncOutbox_ProcessingStatus] 
ON [StockSyncOutboxMessages] ([PublishedAtUtc], [IsFailed], [NextRetryAtUtc], [ClaimedUntilUtc])
WHERE [PublishedAtUtc] IS NULL 
  AND [IsFailed] = 0;
GO

-- Create an additional index for efficient scanning by occurrence time
-- Include ClaimedUntilUtc in the index columns for index-only scans
CREATE INDEX [IX_StockSyncOutbox_Claimable] 
ON [StockSyncOutboxMessages] ([OccurredAtUtc], [ClaimedUntilUtc], [NextRetryAtUtc])
WHERE [PublishedAtUtc] IS NULL 
  AND [IsFailed] = 0;
GO

-- Add extended property for documentation
EXEC sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'Timestamp until which this message is claimed by a worker. Prevents duplicate publishing by multiple workers.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'StockSyncOutboxMessages',
    @level2type = N'COLUMN', @level2name = N'ClaimedUntilUtc';
GO
