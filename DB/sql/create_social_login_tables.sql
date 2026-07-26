-- ============================================================================
-- Phase 10.3.3: Social Login Tables
-- Created: 2026-01-02
-- 
-- Purpose: Stores social login provider links and settings
-- 
-- SECURITY NOTES:
-- - Provider tokens are NEVER stored
-- - Client secrets are encrypted
-- - Provider user IDs have unique constraints
-- ============================================================================

-- =============================================================================
-- Table: OnlineCustomerAuthProviders
-- Links OnlineCustomers to external OAuth providers (Google, Facebook)
-- =============================================================================
CREATE TABLE IF NOT EXISTS "OnlineCustomerAuthProviders" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "Provider" varchar(50) NOT NULL,
    "ProviderUserId" varchar(256) NOT NULL,
    "ProviderEmail" varchar(256) NOT NULL,
    "EmailVerifiedByProvider" boolean NOT NULL DEFAULT false,
    "DisplayName" varchar(256),
    "PictureUrl" varchar(2048),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "LastUsedAtUtc" timestamp with time zone,
    
    -- Foreign key to OnlineCustomers
    CONSTRAINT "FK_AuthProviders_OnlineCustomer" 
        FOREIGN KEY ("OnlineCustomerId") 
        REFERENCES "OnlineCustomers" ("Id") 
        ON DELETE CASCADE
);

-- One provider account can only link to one customer per tenant
-- Prevents provider account takeover attacks
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuthProvider_TenantId_Provider_ProviderUserId" 
    ON "OnlineCustomerAuthProviders" ("TenantId", "Provider", "ProviderUserId");

-- One customer can only have one link per provider
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuthProvider_TenantId_CustomerId_Provider" 
    ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId", "Provider");

-- For finding all providers for a customer
CREATE INDEX IF NOT EXISTS "IX_AuthProvider_TenantId_CustomerId" 
    ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId");

-- =============================================================================
-- Table: StorefrontAuthProviderSettings
-- POS-managed settings for social login providers
-- =============================================================================
CREATE TABLE IF NOT EXISTS "StorefrontAuthProviderSettings" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "Provider" varchar(50) NOT NULL,
    "IsEnabled" boolean NOT NULL DEFAULT false,
    "ClientId" varchar(256),
    "ClientSecretEncrypted" varchar(1024),
    "AdditionalScopes" varchar(512),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "UpdatedByUserId" uuid
);

-- One settings per provider per tenant
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuthProviderSettings_TenantId_Provider" 
    ON "StorefrontAuthProviderSettings" ("TenantId", "Provider");

-- ============================================================================
-- Verification Steps (after running migration):
-- 
-- 1. Verify tables exist:
--    SELECT * FROM information_schema.tables 
--    WHERE table_name IN ('OnlineCustomerAuthProviders', 'StorefrontAuthProviderSettings');
-- 
-- 2. Verify indexes:
--    SELECT indexname FROM pg_indexes 
--    WHERE tablename IN ('OnlineCustomerAuthProviders', 'StorefrontAuthProviderSettings');
-- 
-- 3. Verify foreign key:
--    SELECT constraint_name FROM information_schema.table_constraints 
--    WHERE table_name = 'OnlineCustomerAuthProviders' 
--    AND constraint_type = 'FOREIGN KEY';
-- ============================================================================

-- ============================================================================
-- Sample Data: Enable Google and Facebook for testing
-- (Replace {tenant-id} with actual tenant ID)
-- 
-- INSERT INTO "StorefrontAuthProviderSettings" 
-- ("Id", "TenantId", "Provider", "IsEnabled", "ClientId", "CreatedAtUtc")
-- VALUES 
-- (gen_random_uuid(), '{tenant-id}', 'Google', true, 'your-google-client-id', NOW()),
-- (gen_random_uuid(), '{tenant-id}', 'Facebook', true, 'your-facebook-app-id', NOW());
-- ============================================================================
