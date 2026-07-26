-- ============================================================================
-- Phase 10.3.4: OnlineCustomerRefreshTokens Table
-- Created: 2026-01-02
-- 
-- Purpose: Stores hashed refresh tokens for Storefront customer authentication
-- 
-- SECURITY NOTES:
-- - Tokens are stored as SHA256 hashes, NEVER in plain text
-- - Tokens are NEVER deleted - only marked as revoked (soft delete)
-- - Token rotation creates chain for reuse detection
-- - IP and User-Agent tracked for security auditing
-- ============================================================================

-- Create the table
CREATE TABLE IF NOT EXISTS "OnlineCustomerRefreshTokens" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "TokenHash" varchar(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsRevoked" boolean NOT NULL DEFAULT false,
    "RevokedAtUtc" timestamp with time zone,
    "RevokedReason" varchar(256),
    "ReplacedByTokenId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByIp" varchar(45) NOT NULL,
    "CreatedByUserAgent" varchar(512),
    
    -- Foreign key to OnlineCustomers
    CONSTRAINT "FK_RefreshToken_OnlineCustomer" 
        FOREIGN KEY ("OnlineCustomerId") 
        REFERENCES "OnlineCustomers" ("Id") 
        ON DELETE CASCADE
);

-- Unique index for token lookup (most common query)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshToken_TenantId_TokenHash" 
    ON "OnlineCustomerRefreshTokens" ("TenantId", "TokenHash");

-- Index for finding all tokens for a customer
CREATE INDEX IF NOT EXISTS "IX_RefreshToken_TenantId_CustomerId" 
    ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId");

-- Index for cleanup of expired tokens
CREATE INDEX IF NOT EXISTS "IX_RefreshToken_ExpiresAtUtc" 
    ON "OnlineCustomerRefreshTokens" ("ExpiresAtUtc");

-- Index for finding active (non-revoked) tokens
CREATE INDEX IF NOT EXISTS "IX_RefreshToken_TenantId_CustomerId_IsRevoked" 
    ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId", "IsRevoked");

-- ============================================================================
-- Verification Steps (after running migration):
-- 
-- 1. Verify table exists:
--    SELECT * FROM information_schema.tables 
--    WHERE table_name = 'OnlineCustomerRefreshTokens';
-- 
-- 2. Verify indexes:
--    SELECT indexname FROM pg_indexes 
--    WHERE tablename = 'OnlineCustomerRefreshTokens';
-- 
-- 3. Verify foreign key:
--    SELECT constraint_name FROM information_schema.table_constraints 
--    WHERE table_name = 'OnlineCustomerRefreshTokens' 
--    AND constraint_type = 'FOREIGN KEY';
-- ============================================================================
