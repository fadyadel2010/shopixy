-- Migration: Add InvalidatedAtUtc column to CheckoutSessions table
-- Purpose: Track when sessions are invalidated during reconciliation to prevent non-idempotent duplicate processing
-- Date: 2026-04-21
-- Issue: Reconciliation was returning already-invalidated duplicates, causing infinite re-processing

-- Add the InvalidatedAtUtc column (nullable, no default)
ALTER TABLE "CheckoutSessions"
ADD COLUMN "InvalidatedAtUtc" timestamp without time zone NULL;

-- Add index for query performance (filtering out invalidated sessions)
CREATE INDEX "IX_CheckoutSessions_InvalidatedAtUtc"
ON "CheckoutSessions" ("InvalidatedAtUtc")
WHERE "InvalidatedAtUtc" IS NULL;

-- Add comment for documentation
COMMENT ON COLUMN "CheckoutSessions"."InvalidatedAtUtc" IS 
'Timestamp when this session was invalidated during reconciliation (duplicate resolution). NULL if never invalidated. Used to exclude already-invalidated duplicates from recoverable session queries.';
