-- ============================================================================
-- PROMOTION ENGINE - QUICK FIX FOR MISSING COLUMNS
-- 
-- Run this if you already ran the initial migration and are getting:
-- "column p.CreatedBy does not exist" error
-- ============================================================================

-- Add missing BaseEntity columns to Promotions table
ALTER TABLE "Promotions" ADD COLUMN IF NOT EXISTS "CreatedBy" uuid;
ALTER TABLE "Promotions" ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid;

-- Add missing columns to PromotionUsages table
ALTER TABLE "PromotionUsages" ADD COLUMN IF NOT EXISTS "CreatedBy" uuid;
ALTER TABLE "PromotionUsages" ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid;

-- Done!
-- Restart the API after running this.
