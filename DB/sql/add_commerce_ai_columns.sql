-- ============================================================================
-- PHASE 6: Commerce AI Admin Control - Database Migration
-- ============================================================================
-- 
-- Adds Commerce AI governance columns to StoreConfigs table.
-- 
-- Columns:
-- - CommerceAiEnabled: Feature toggle (default: false)
-- - CommerceAiEnabledAt: Timestamp when enabled
-- - CommerceAiUpdatedBy: Admin who last toggled the feature
-- 
-- SAFETY:
-- - Uses IF NOT EXISTS to prevent errors on re-run
-- - Default is FALSE (AI disabled by default)
-- - Idempotent (safe to run multiple times)
-- ============================================================================

-- Add CommerceAiEnabled column (default: false)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'StoreConfigs' 
        AND column_name = 'CommerceAiEnabled'
    ) THEN
        ALTER TABLE "StoreConfigs" 
        ADD COLUMN "CommerceAiEnabled" BOOLEAN NOT NULL DEFAULT FALSE;
        
        RAISE NOTICE 'Added CommerceAiEnabled column to StoreConfigs';
    ELSE
        RAISE NOTICE 'CommerceAiEnabled column already exists';
    END IF;
END $$;

-- Add CommerceAiEnabledAt column (nullable)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'StoreConfigs' 
        AND column_name = 'CommerceAiEnabledAt'
    ) THEN
        ALTER TABLE "StoreConfigs" 
        ADD COLUMN "CommerceAiEnabledAt" TIMESTAMP NULL;
        
        RAISE NOTICE 'Added CommerceAiEnabledAt column to StoreConfigs';
    ELSE
        RAISE NOTICE 'CommerceAiEnabledAt column already exists';
    END IF;
END $$;

-- Add CommerceAiUpdatedBy column (nullable)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'StoreConfigs' 
        AND column_name = 'CommerceAiUpdatedBy'
    ) THEN
        ALTER TABLE "StoreConfigs" 
        ADD COLUMN "CommerceAiUpdatedBy" VARCHAR(255) NULL;
        
        RAISE NOTICE 'Added CommerceAiUpdatedBy column to StoreConfigs';
    ELSE
        RAISE NOTICE 'CommerceAiUpdatedBy column already exists';
    END IF;
END $$;

-- Verify columns were added
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default
FROM information_schema.columns
WHERE table_name = 'StoreConfigs'
AND column_name IN ('CommerceAiEnabled', 'CommerceAiEnabledAt', 'CommerceAiUpdatedBy')
ORDER BY column_name;

-- Show current state of all StoreConfigs
SELECT 
    "Id",
    "TenantId",
    "StoreName",
    "CommerceAiEnabled",
    "CommerceAiEnabledAt",
    "CommerceAiUpdatedBy",
    "UpdatedAtUtc"
FROM "StoreConfigs"
ORDER BY "UpdatedAtUtc" DESC;

-- ============================================================================
-- NOTES:
-- ============================================================================
-- 
-- 1. All new stores will have CommerceAiEnabled = false by default
-- 2. Existing stores will have CommerceAiEnabled = false after migration
-- 3. Admins must explicitly enable the feature via /admin/ecommerce/commerce-ai
-- 4. The feature can be toggled on/off at any time
-- 5. All toggles are logged via CommerceAiUpdatedBy and CommerceAiEnabledAt
-- 
-- ============================================================================
