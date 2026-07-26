-- =====================================================
-- Fix: Add missing columns to CarrierConfigurations table
-- Database: PostgreSQL (PgAdmin4)
-- Created: 2026-01-22
-- 
-- Issue: Entity has CreatedBy and UpdatedBy from BaseEntity
-- but these columns don't exist in the database table.
-- =====================================================

-- Add CreatedBy column (nullable Guid)
ALTER TABLE "CarrierConfigurations"
ADD COLUMN IF NOT EXISTS "CreatedBy" UUID NULL;

-- Add UpdatedBy column (nullable Guid)
ALTER TABLE "CarrierConfigurations"
ADD COLUMN IF NOT EXISTS "UpdatedBy" UUID NULL;

-- Verify the columns were added
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'CarrierConfigurations'
  AND column_name IN ('CreatedBy', 'UpdatedBy');
