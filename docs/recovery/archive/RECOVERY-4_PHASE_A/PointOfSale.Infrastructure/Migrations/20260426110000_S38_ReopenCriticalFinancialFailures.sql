-- ============================================================================
-- Migration: S38_ReopenCriticalFinancialFailures
-- Description: Reopen pre-existing dead-lettered critical financial truth
--              events to prevent them from blocking aggregate version streams.
--              
--              Context: After introducing non-terminal failure handling for
--              critical events, any older rows with IsFailed = true will now
--              block later aggregate versions via HasUnprocessedEarlierAggregateVersionAsync().
--              This migration resets those rows to allow retry processing.
-- Date: 2026-04-26
-- ============================================================================

-- Reopen critical financial truth events that are currently in failed state
-- These events must not remain in terminal dead-letter state as they represent
-- committed business state that must eventually be reflected in the ledger.
UPDATE "AccountingOutboxMessages"
SET 
    "IsFailed" = false,
    "NextRetryAtUtc" = NOW() + INTERVAL '5 minutes',
    "ClaimedUntilUtc" = NULL
WHERE 
    "IsFailed" = true
    AND "ProcessedAtUtc" IS NULL
    AND "EventType" IN (
        'PosSale',
        'SaleReversal',
        'PosRefund',
        'OnlinePayment',
        'CodDelivered',
        'WalletApproved',
        'EcommerceRefund',
        'GatewaySettlement',
        'CodSettlement'
    );

-- ============================================================================
-- Rollback Instructions (Down Migration)
-- ============================================================================
-- Note: Rollback is not recommended as it would re-block the version stream.
-- If absolutely necessary, you can manually mark specific messages as failed:
-- 
-- UPDATE "AccountingOutboxMessages"
-- SET "IsFailed" = true
-- WHERE "Id" IN (/* specific message IDs */);
