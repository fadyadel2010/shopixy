-- Migration: Add index for IsAccessTokenBlacklisted column
-- Purpose: Index for queries that check blacklist status on revoked tokens
-- Date: 2026-04-21
-- IMPORTANT: Run this WITHOUT wrapping in BEGIN/COMMIT - CONCURRENTLY cannot run in a transaction

-- Add index for queries that check blacklist status on revoked tokens
-- Note: Index name shortened to fit PostgreSQL's 63-character identifier limit
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_OnlineCustomerRefreshTokens_Revoked_Blacklisted"
ON "OnlineCustomerRefreshTokens" ("TenantId", "IsRevoked", "IsAccessTokenBlacklisted")
WHERE "IsRevoked" = TRUE AND "IsAccessTokenBlacklisted" = FALSE;
