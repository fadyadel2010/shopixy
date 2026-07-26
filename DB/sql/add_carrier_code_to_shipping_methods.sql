-- =====================================================
-- Add CarrierCode column to ShippingMethods table
-- Database: PostgreSQL (PgAdmin4)
-- Created: 2026-01-22
-- 
-- Links shipping methods to carrier adapters (bosta, aramex, etc.)
-- for automatic shipment creation with carrier integration.
-- =====================================================

-- Add CarrierCode column to ShippingMethods
ALTER TABLE "ShippingMethods"
ADD COLUMN IF NOT EXISTS "CarrierCode" VARCHAR(50) NULL;

-- Add comment for documentation
COMMENT ON COLUMN "ShippingMethods"."CarrierCode" IS 
'Carrier code for carrier-agnostic shipping (bosta, aramex, mylerz, dhl). NULL means manual.';

-- Verify the column was added
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'ShippingMethods'
  AND column_name = 'CarrierCode';
