-- ============================================================================
-- Phase 10.3.5: Password Reset Tokens Table
-- Created: 2026-01-02
-- 
-- Purpose: Stores hashed password reset tokens for secure password recovery
-- 
-- SECURITY NOTES:
-- - RAW TOKENS ARE NEVER STORED - only SHA256 hashes
-- - Tokens are one-time use (IsUsed flag)
-- - Tokens expire after 30 minutes
-- - Previous unused tokens are invalidated on new request
-- ============================================================================

-- =============================================================================
-- Table: OnlineCustomerPasswordResetTokens
-- Stores hashed password reset tokens for Online Customers
-- =============================================================================
CREATE TABLE IF NOT EXISTS "OnlineCustomerPasswordResetTokens" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "TokenHash" varchar(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsUsed" boolean NOT NULL DEFAULT false,
    "UsedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByIp" varchar(64) NOT NULL,
    
    -- Foreign key to OnlineCustomers
    CONSTRAINT "FK_PasswordResetTokens_OnlineCustomer" 
        FOREIGN KEY ("OnlineCustomerId") 
        REFERENCES "OnlineCustomers" ("Id") 
        ON DELETE CASCADE
);

-- CRITICAL: Unique token hash per tenant
-- Prevents token collision attacks
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetToken_TenantId_TokenHash" 
    ON "OnlineCustomerPasswordResetTokens" ("TenantId", "TokenHash");

-- For invalidating previous unused tokens
CREATE INDEX IF NOT EXISTS "IX_PasswordResetToken_TenantId_CustomerId_IsUsed" 
    ON "OnlineCustomerPasswordResetTokens" ("TenantId", "OnlineCustomerId", "IsUsed");

-- For cleanup of expired tokens
CREATE INDEX IF NOT EXISTS "IX_PasswordResetToken_ExpiresAtUtc" 
    ON "OnlineCustomerPasswordResetTokens" ("ExpiresAtUtc");

-- ============================================================================
-- Add NormalizedEmail column if not exists (required for password reset)
-- This was added in Phase 10.3.3 but included here for completeness
-- ============================================================================
ALTER TABLE "OnlineCustomers" 
ADD COLUMN IF NOT EXISTS "NormalizedEmail" varchar(256);

-- Backfill NormalizedEmail for existing records
UPDATE "OnlineCustomers" 
SET "NormalizedEmail" = LOWER(TRIM("Email")) 
WHERE "NormalizedEmail" IS NULL OR "NormalizedEmail" = '';

-- Add EmailVerifiedAtUtc column if not exists
ALTER TABLE "OnlineCustomers" 
ADD COLUMN IF NOT EXISTS "EmailVerifiedAtUtc" timestamp with time zone;

-- ============================================================================
-- Verification Steps (after running migration):
-- 
-- 1. Verify table exists:
--    SELECT * FROM information_schema.tables 
--    WHERE table_name = 'OnlineCustomerPasswordResetTokens';
-- 
-- 2. Verify indexes:
--    SELECT indexname FROM pg_indexes 
--    WHERE tablename = 'OnlineCustomerPasswordResetTokens';
-- 
-- 3. Verify columns added to OnlineCustomers:
--    SELECT column_name FROM information_schema.columns 
--    WHERE table_name = 'OnlineCustomers' 
--    AND column_name IN ('NormalizedEmail', 'EmailVerifiedAtUtc');
-- ============================================================================
