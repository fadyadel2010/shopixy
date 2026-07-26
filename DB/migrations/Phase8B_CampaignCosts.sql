-- =====================================================
-- Phase 8B: Campaign Cost & ROI - Database Migration
-- Created: 2026-01-21
-- Target: PostgreSQL (PgAdmin4)
-- =====================================================

-- Create the CampaignCosts table
CREATE TABLE IF NOT EXISTS "CampaignCosts" (
    "CampaignCostId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Source" character varying(100) NOT NULL,
    "Medium" character varying(100) NOT NULL,
    "Campaign" character varying(200) NOT NULL,
    "PeriodStartUtc" timestamp with time zone NOT NULL,
    "PeriodEndUtc" timestamp with time zone NOT NULL,
    "CostAmount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "Notes" character varying(500),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_CampaignCosts" PRIMARY KEY ("CampaignCostId")
);

-- Index for tenant lookup
CREATE INDEX IF NOT EXISTS "IX_CampaignCosts_TenantId" 
    ON "CampaignCosts" ("TenantId");

-- Unique index: One cost entry per campaign per period (excluding soft-deleted)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc" 
    ON "CampaignCosts" ("TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc")
    WHERE "IsDeleted" = false;

-- Index for period range queries
CREATE INDEX IF NOT EXISTS "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc" 
    ON "CampaignCosts" ("TenantId", "PeriodStartUtc", "PeriodEndUtc");

-- Record migration in EF migrations history (if using EF)
-- Note: Only run this if you want to sync with EF migrations history
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260121XXXXXX_AddCampaignCosts', '9.0.1')
-- ON CONFLICT DO NOTHING;

-- =====================================================
-- Verification queries (optional - run after migration)
-- =====================================================

-- Check table was created:
-- SELECT * FROM information_schema.tables WHERE table_name = 'CampaignCosts';

-- Check indexes were created:
-- SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'CampaignCosts';

-- =====================================================
-- Sample test data (optional - for testing)
-- =====================================================

-- INSERT INTO "CampaignCosts" (
--     "CampaignCostId", "TenantId", "Source", "Medium", "Campaign",
--     "PeriodStartUtc", "PeriodEndUtc", "CostAmount", "Currency",
--     "Notes", "CreatedAtUtc", "UpdatedAtUtc", "IsDeleted"
-- ) VALUES (
--     gen_random_uuid(), 
--     'YOUR_TENANT_ID_HERE'::uuid,
--     'facebook', 'cpc', 'winter_sale_2026',
--     '2026-01-01'::timestamp, '2026-01-31'::timestamp,
--     500.00, 'USD',
--     'Winter sale campaign on Facebook',
--     NOW(), NOW(), FALSE
-- );
