-- ===========================================================================
-- Phase 3: Analytics Dispatchers - TenantAnalyticsConfiguration Table
-- 
-- This table stores per-tenant analytics provider settings (GA4, Meta CAPI).
-- Secrets are encrypted using Data Protection API before storage.
--
-- Run this in PgAdmin4 to add the new table.
-- ===========================================================================

-- Create TenantAnalyticsConfigurations table
CREATE TABLE IF NOT EXISTS "TenantAnalyticsConfigurations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SettingsJson" text NOT NULL DEFAULT '{}',
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "PK_TenantAnalyticsConfigurations" PRIMARY KEY ("Id")
);

-- Create unique index on TenantId (one config per tenant)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantAnalyticsConfigurations_TenantId" 
    ON "TenantAnalyticsConfigurations" ("TenantId");

-- Add to EF migrations history (optional - only if you want EF to recognize this migration)
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260120154000_AddTenantAnalyticsConfiguration', '9.0.0');

-- ===========================================================================
-- VERIFICATION QUERY
-- Run this after creating the table to verify it was created correctly:
-- ===========================================================================
-- SELECT table_name, column_name, data_type 
-- FROM information_schema.columns 
-- WHERE table_name = 'TenantAnalyticsConfigurations';
