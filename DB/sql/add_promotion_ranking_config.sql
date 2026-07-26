-- Migration: AddPromotionRankingConfigToStoreConfig
-- Date: 2026-01-18
-- Purpose: Add PromotionRankingConfig columns to StoreConfigs table
--          for tenant-specific promotion ranking configuration

-- NOTE: This is idempotent - safe to run multiple times
-- Uses column existence checks to avoid "already exists" errors

START TRANSACTION;

-- Add PromotionRanking_Enabled column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_Enabled') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_Enabled" boolean NOT NULL DEFAULT TRUE;
    END IF;
END $$;

-- Add PromotionRanking_BaseBoost column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_BaseBoost') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_BaseBoost" double precision NOT NULL DEFAULT 5.0;
    END IF;
END $$;

-- Add PromotionRanking_HighDiscountBoost column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_HighDiscountBoost') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_HighDiscountBoost" double precision NOT NULL DEFAULT 10.0;
    END IF;
END $$;

-- Add PromotionRanking_HighDiscountThreshold column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_HighDiscountThreshold') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_HighDiscountThreshold" double precision NOT NULL DEFAULT 20.0;
    END IF;
END $$;

-- Add PromotionRanking_MaxTotalBoost column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_MaxTotalBoost') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_MaxTotalBoost" double precision NOT NULL DEFAULT 25.0;
    END IF;
END $$;

-- Add PromotionRanking_EnableTimeDecay column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_EnableTimeDecay') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_EnableTimeDecay" boolean NOT NULL DEFAULT TRUE;
    END IF;
END $$;

-- Add PromotionRanking_TimeDecayScaleDays column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_TimeDecayScaleDays') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayScaleDays" double precision NOT NULL DEFAULT 7.0;
    END IF;
END $$;

-- Add PromotionRanking_TimeDecayOffsetDays column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_TimeDecayOffsetDays') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayOffsetDays" double precision NOT NULL DEFAULT 1.0;
    END IF;
END $$;

-- Add PromotionRanking_TimeDecayMinimum column
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' 
                   AND column_name = 'PromotionRanking_TimeDecayMinimum') THEN
        ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayMinimum" double precision NOT NULL DEFAULT 0.3;
    END IF;
END $$;

-- Add SearchBoostOverride column to Promotions table (if not exists)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Promotions' 
                   AND column_name = 'SearchBoostOverride') THEN
        ALTER TABLE "Promotions" ADD "SearchBoostOverride" double precision;
    END IF;
END $$;

-- Record migration in history (if not already recorded)
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260118125347_AddPromotionRankingConfigToStoreConfig', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory" 
    WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig'
);

COMMIT;

-- Verify the columns were added
SELECT 'Migration completed successfully!' as status;

SELECT column_name, data_type, column_default 
FROM information_schema.columns 
WHERE table_name = 'StoreConfigs' 
AND column_name LIKE 'PromotionRanking_%'
ORDER BY column_name;
