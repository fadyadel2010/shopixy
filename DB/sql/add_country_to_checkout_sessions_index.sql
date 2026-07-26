-- Migration: Create index on CheckoutSessions.Country
-- Phase: Abandoned Checkout Recovery - Country Support (Part 2)
-- Date: 2026-04-19
-- Purpose: Add index for country-based queries
-- 
-- NOTE: This uses non-concurrent index creation to work with migration tools
-- that require transaction blocks. For production databases with heavy traffic,
-- consider running the concurrent version manually:
--
-- Manual production command (run outside migration tool):
-- CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_CheckoutSessions_Country" 
-- ON "CheckoutSessions" ("Country") 
-- WHERE "Country" IS NOT NULL;

-- Create index (non-concurrent version for migration compatibility)
CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_Country" 
ON "CheckoutSessions" ("Country") 
WHERE "Country" IS NOT NULL;

-- Add comment for documentation
COMMENT ON INDEX "IX_CheckoutSessions_Country" IS 
'Supports country-based filtering for abandoned checkout recovery and analytics.';
