-- ============================================================================
-- WhatsApp Web Messaging - Database Migration
-- Migration ID: 20260129143905_AddWhatsAppMessaging
-- ============================================================================
-- 
-- This script creates the necessary database objects for WhatsApp messaging.
-- Run this in PgAdmin4 against your PostgreSQL database.
--
-- PREREQUISITES:
-- - The "Tenants" table must exist
-- - The "Products" table must exist (for PixelTrackingConfigs)
--
-- ============================================================================

BEGIN;

-- ============================================================================
-- PART 1: Pixel Tracking Config Tables (Bundled in same migration)
-- ============================================================================

CREATE TABLE IF NOT EXISTS "PixelTrackingConfigs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "Strategy" integer NOT NULL,
    "Value" numeric(18,4) NOT NULL,
    "AppliesToJson" text NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" text,
    CONSTRAINT "PK_PixelTrackingConfigs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ProductPixelOverrides" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "Strategy" integer NOT NULL,
    "Value" numeric(18,4) NOT NULL,
    "ApplyToMeta" boolean NOT NULL,
    "ApplyToGoogle" boolean NOT NULL,
    "ApplyToTikTok" boolean NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" uuid NOT NULL,
    CONSTRAINT "PK_ProductPixelOverrides" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProductPixelOverrides_Products_ProductId" FOREIGN KEY ("ProductId") 
        REFERENCES "Products" ("Id") ON DELETE CASCADE
);

-- ============================================================================
-- PART 2: WhatsApp Accounts Table
-- ============================================================================
-- This table stores WhatsApp account configurations per tenant.
-- Each tenant can have multiple accounts, but typically one per use case.

CREATE TABLE IF NOT EXISTS whatsapp_accounts (
    -- Primary key with auto-generated UUID
    id uuid NOT NULL DEFAULT (gen_random_uuid()),
    
    -- WhatsApp phone number in E.164 format (e.g., +201234567890)
    phone_number character varying(20) NOT NULL,
    
    -- Display name for admin identification
    display_name character varying(100) NOT NULL,
    
    -- Account status: 0=PendingQR, 1=Connected, 2=Disconnected, 3=Banned, 4=Disabled
    status integer NOT NULL DEFAULT 0,
    
    -- Whether this account is enabled for messaging
    enabled boolean NOT NULL DEFAULT FALSE,
    
    -- Use for order confirmation messages
    use_for_order_confirmation boolean NOT NULL DEFAULT FALSE,
    
    -- Use for OTP verification messages
    use_for_otp_verification boolean NOT NULL DEFAULT FALSE,
    
    -- Connection timestamps
    last_connected_at_utc timestamp with time zone,
    last_disconnected_at_utc timestamp with time zone,
    
    -- Reason for current status (e.g., ban reason)
    status_reason text,
    
    -- Audit timestamps
    updated_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL DEFAULT (NOW()),
    
    -- TenantEntity base fields
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    
    -- Tenant isolation (REQUIRED)
    tenant_id uuid NOT NULL,
    
    -- Primary key constraint
    CONSTRAINT "PK_whatsapp_accounts" PRIMARY KEY (id),
    
    -- Foreign key to Tenants table (cascade delete if tenant is removed)
    CONSTRAINT "FK_whatsapp_accounts_Tenants_tenant_id" FOREIGN KEY (tenant_id) 
        REFERENCES "Tenants" ("Id") ON DELETE CASCADE
);

-- ============================================================================
-- PART 3: Indexes
-- ============================================================================

-- Pixel Tracking indexes
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PixelTrackingConfigs_TenantId" 
    ON "PixelTrackingConfigs" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_ProductPixelOverrides_ProductId" 
    ON "ProductPixelOverrides" ("ProductId");

CREATE INDEX IF NOT EXISTS "IX_ProductPixelOverrides_TenantId" 
    ON "ProductPixelOverrides" ("TenantId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductPixelOverrides_TenantId_ProductId" 
    ON "ProductPixelOverrides" ("TenantId", "ProductId");

-- WhatsApp indexes
-- Index for looking up enabled accounts by status (filtered index)
CREATE INDEX IF NOT EXISTS ix_whatsapp_accounts_status 
    ON whatsapp_accounts (status) 
    WHERE enabled = TRUE;

-- Index for tenant-based queries
CREATE INDEX IF NOT EXISTS ix_whatsapp_accounts_tenant_id 
    ON whatsapp_accounts (tenant_id);

-- Unique constraint: One phone number per tenant
CREATE UNIQUE INDEX IF NOT EXISTS uq_whatsapp_account_phone_tenant 
    ON whatsapp_accounts (tenant_id, phone_number);

-- ============================================================================
-- PART 4: Record Migration in EF History
-- ============================================================================
-- This allows EF Core to recognize the migration has been applied.

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260129143905_AddWhatsAppMessaging', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (Optional - Run separately to verify)
-- ============================================================================
-- 
-- Check tables exist:
-- SELECT table_name FROM information_schema.tables WHERE table_name = 'whatsapp_accounts';
--
-- Check indexes:
-- SELECT indexname FROM pg_indexes WHERE tablename = 'whatsapp_accounts';
--
-- Check migration recorded:
-- SELECT * FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%WhatsApp%';
--
-- ============================================================================
