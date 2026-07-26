-- Add BannerStyle column to Promotions table
-- This column stores the banner design style for category promotions
-- Options: 'gradient', 'solid', 'minimal', 'festive'

ALTER TABLE "Promotions" 
ADD COLUMN IF NOT EXISTS "BannerStyle" character varying(50) NOT NULL DEFAULT 'gradient';

-- Update existing promotions to have the default value
UPDATE "Promotions" 
SET "BannerStyle" = 'gradient' 
WHERE "BannerStyle" IS NULL;
