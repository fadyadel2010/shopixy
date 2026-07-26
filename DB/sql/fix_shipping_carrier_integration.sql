-- =====================================================
-- SHIPPING CARRIER INTEGRATION FIX
-- Run this in PgAdmin4 after backing up
-- =====================================================

-- STEP 1: Add CarrierCode column to ShippingMethods
ALTER TABLE "ShippingMethods"
ADD COLUMN IF NOT EXISTS "CarrierCode" VARCHAR(50) NULL;

-- Verify the column was added
SELECT column_name, data_type 
FROM information_schema.columns
WHERE table_name = 'ShippingMethods' AND column_name = 'CarrierCode';

-- STEP 2: View current shipping methods
SELECT "Id", "Name", "CarrierName", "CarrierCode", "TenantId"
FROM "ShippingMethods"
WHERE "IsDeleted" = false;

-- STEP 3: Update your "Standard Shipping" to use Bosta
-- First, find your shipping method ID:
SELECT "Id", "Name" FROM "ShippingMethods" WHERE "Name" LIKE '%Standard%';

-- Then update it (replace the ID with your actual ID):
-- UPDATE "ShippingMethods" SET "CarrierCode" = 'bosta' WHERE "Id" = 'YOUR-SHIPPING-METHOD-ID';

-- STEP 4: View existing shipments and their carrier codes
SELECT "Id", "ShipmentNumber", "CarrierCode", "Status", "CreatedAt"
FROM "Shipments"
ORDER BY "CreatedAt" DESC
LIMIT 10;

-- STEP 5: Fix existing shipments that should use Bosta
-- Find shipments that used Standard Shipping:
SELECT s."Id", s."ShipmentNumber", s."CarrierCode", sm."Name" as "ShippingMethod", sm."CarrierCode" as "MethodCarrierCode"
FROM "Shipments" s
LEFT JOIN "ShippingMethods" sm ON s."ShippingMethodId" = sm."Id"
WHERE s."IsDeleted" = false;

-- Update shipments to use bosta if their shipping method is linked to bosta:
-- UPDATE "Shipments" s
-- SET "CarrierCode" = sm."CarrierCode"
-- FROM "ShippingMethods" sm
-- WHERE s."ShippingMethodId" = sm."Id"
--   AND sm."CarrierCode" IS NOT NULL
--   AND (s."CarrierCode" IS NULL OR s."CarrierCode" = 'manual');
