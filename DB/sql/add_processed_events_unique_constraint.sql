-- Migration: Fix ProcessedEvents idempotency constraint
-- Purpose: Remove TenantId from unique index to fix idempotency when TenantId is NULL
-- Issue: PostgreSQL allows multiple NULL values in unique indexes, breaking idempotency guarantee
-- Related: AffiliateProductIntelligenceProjectionConsumer and other consumers using ProcessedEvents

-- Drop the existing indexes to prevent constraint conflicts
DROP INDEX IF EXISTS IX_ProcessedEvents_Idempotency;
DROP INDEX IF EXISTS IX_ProcessedEvents_EventId_ConsumerName;

-- Diagnostic: Check for duplicate rows before creating unique index
-- This query identifies groups with multiple non-deleted rows for the same (EventId, ConsumerName, EventType)
DO $$
DECLARE
    duplicate_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT "EventId", "ConsumerName", "EventType", COUNT(*) as row_count
        FROM "ProcessedEvents"
        WHERE "IsDeleted" = false
        GROUP BY "EventId", "ConsumerName", "EventType"
        HAVING COUNT(*) > 1
    ) duplicates;
    
    IF duplicate_count > 0 THEN
        RAISE NOTICE 'Found % duplicate groups in ProcessedEvents. Proceeding with deduplication...', duplicate_count;
    ELSE
        RAISE NOTICE 'No duplicates found in ProcessedEvents. Proceeding with index creation...';
    END IF;
END $$;

-- Deduplication: Soft-delete all but the earliest row per (EventId, ConsumerName, EventType) group
-- Strategy: Keep the row with the smallest Id (earliest created), soft-delete the rest
-- This preserves the WHERE "IsDeleted" = false semantics for the unique index
UPDATE "ProcessedEvents" pe
SET "IsDeleted" = true,
    "UpdatedAt" = NOW()
WHERE "IsDeleted" = false
  AND "Id" NOT IN (
      -- Keep only the earliest row (smallest Id) per group
      SELECT MIN("Id")
      FROM "ProcessedEvents"
      WHERE "IsDeleted" = false
      GROUP BY "EventId", "ConsumerName", "EventType"
  );

-- Create corrected unique index on ProcessedEvents to ensure exactly-once event processing
-- Composite key: EventId + ConsumerName + EventType
-- NOTE: TenantId is intentionally EXCLUDED because it is nullable (Guid? in entity).
-- PostgreSQL allows multiple NULL values in unique indexes, which would break idempotency
-- when TenantId is NULL. The combination of EventId + ConsumerName + EventType is sufficient
-- for global idempotency across all tenants.
CREATE UNIQUE INDEX IX_ProcessedEvents_Idempotency
ON "ProcessedEvents" ("EventId", "ConsumerName", "EventType")
WHERE "IsDeleted" = false;

-- Note: This constraint enables atomic idempotency checks via duplicate key exceptions
-- Consumers should catch DbUpdateException and treat duplicate key violations as "already processed"
