-- Migration: Add IsAccessTokenBlacklisted column to OnlineCustomerRefreshTokens
-- Purpose: Track whether the current access token JTI has been successfully blacklisted in Redis
-- This enables retry logic for Redis blacklisting failures without re-attempting on already-blacklisted tokens
-- Date: 2026-04-21

-- DEPLOYMENT NOTE: This migration has two parts:
-- Part 1: Column addition (transactional)
-- Part 2: Index creation (non-transactional, must be run separately with CONCURRENTLY)

-- ============================================================================
-- PART 1: Add column (run this first in a transaction)
-- ============================================================================
BEGIN;

-- Add the IsAccessTokenBlacklisted column with default value false
ALTER TABLE "OnlineCustomerRefreshTokens"
ADD COLUMN "IsAccessTokenBlacklisted" BOOLEAN NOT NULL DEFAULT FALSE;

-- Add comment explaining the column's purpose
COMMENT ON COLUMN "OnlineCustomerRefreshTokens"."IsAccessTokenBlacklisted" IS 
'Tracks whether the CurrentAccessTokenJti has been successfully blacklisted in Redis. Used to enable retry logic for Redis blacklisting failures.';

COMMIT;

-- ============================================================================
-- PART 2: Add index (run this separately WITHOUT a transaction)
-- ============================================================================
-- IMPORTANT: This must be run outside of any transaction to use CONCURRENTLY
-- CONCURRENTLY prevents write-blocking on production traffic during index creation

-- Add index for queries that check blacklist status on revoked tokens
-- Note: Index name shortened to fit PostgreSQL's 63-character identifier limit
CREATE INDEX CONCURRENTLY "IX_OnlineCustomerRefreshTokens_Revoked_Blacklisted"
ON "OnlineCustomerRefreshTokens" ("TenantId", "IsRevoked", "IsAccessTokenBlacklisted")
WHERE "IsRevoked" = TRUE AND "IsAccessTokenBlacklisted" = FALSE;
