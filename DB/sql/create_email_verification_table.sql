-- ============================================================================
-- Phase 10.3.2.5: OnlineCustomerEmailVerifications Table
-- Created: 2026-01-02
-- 
-- Purpose: Stores hashed email verification tokens for Storefront customers
-- 
-- SECURITY NOTES:
-- - Tokens are stored as SHA256 hashes, NEVER in plain text
-- - Each token is one-time use (IsUsed flag)
-- - Tokens expire after 24 hours (configurable)
-- ============================================================================

-- Create the table
CREATE TABLE IF NOT EXISTS "OnlineCustomerEmailVerifications" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "TokenHash" varchar(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsUsed" boolean NOT NULL DEFAULT false,
    "UsedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    
    -- Foreign key to OnlineCustomers
    CONSTRAINT "FK_EmailVerification_OnlineCustomer" 
        FOREIGN KEY ("OnlineCustomerId") 
        REFERENCES "OnlineCustomers" ("Id") 
        ON DELETE CASCADE
);

-- Index for token lookup (most common query)
-- Unique to prevent duplicate tokens
CREATE UNIQUE INDEX IF NOT EXISTS "IX_EmailVerification_TenantId_TokenHash" 
    ON "OnlineCustomerEmailVerifications" ("TenantId", "TokenHash");

-- Index for finding active/unused tokens for a customer
CREATE INDEX IF NOT EXISTS "IX_EmailVerification_TenantId_CustomerId_IsUsed" 
    ON "OnlineCustomerEmailVerifications" ("TenantId", "OnlineCustomerId", "IsUsed");

-- Index for cleanup of expired tokens
CREATE INDEX IF NOT EXISTS "IX_EmailVerification_ExpiresAtUtc" 
    ON "OnlineCustomerEmailVerifications" ("ExpiresAtUtc");

-- ============================================================================
-- Verification Steps (after running migration):
-- 
-- 1. Verify table exists:
--    SELECT * FROM information_schema.tables 
--    WHERE table_name = 'OnlineCustomerEmailVerifications';
-- 
-- 2. Verify indexes:
--    SELECT indexname FROM pg_indexes 
--    WHERE tablename = 'OnlineCustomerEmailVerifications';
-- 
-- 3. Verify foreign key:
--    SELECT constraint_name FROM information_schema.table_constraints 
--    WHERE table_name = 'OnlineCustomerEmailVerifications' 
--    AND constraint_type = 'FOREIGN KEY';
-- ============================================================================
