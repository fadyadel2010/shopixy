-- =====================================================
-- SHIPMENTS DOMAIN STABILIZATION - DATA NORMALIZATION
-- Database: PostgreSQL (PgAdmin4)
-- Created: 2026-01-22
-- 
-- ONE-TIME MIGRATION to normalize legacy data.
-- Run this AFTER deploying the code changes.
--
-- WHAT THIS DOES:
-- 1. Normalizes ShippingMethods.CarrierCode (NULL/empty → 'manual')
-- 2. Normalizes Shipments.CarrierCode (NULL/empty → 'manual')
-- 
-- BEFORE RUNNING:
-- - Take a database backup
-- - Run in a transaction
-- - Verify counts before and after
-- =====================================================

BEGIN;

-- =====================================================
-- STEP 1: Count affected records (BEFORE)
-- =====================================================
SELECT 'ShippingMethods with NULL/empty CarrierCode' AS check_type,
       COUNT(*) AS count
FROM "ShippingMethods"
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

SELECT 'Shipments with NULL/empty CarrierCode' AS check_type,
       COUNT(*) AS count
FROM "Shipments"
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

-- =====================================================
-- STEP 2: Normalize ShippingMethods.CarrierCode
-- =====================================================
UPDATE "ShippingMethods"
SET "CarrierCode" = 'manual',
    "UpdatedAt" = NOW()
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

-- =====================================================
-- STEP 3: Normalize Shipments.CarrierCode
-- =====================================================
UPDATE "Shipments"
SET "CarrierCode" = 'manual',
    "UpdatedAt" = NOW()
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

-- =====================================================
-- STEP 4: Verify counts (AFTER)
-- =====================================================
SELECT 'ShippingMethods with NULL/empty CarrierCode (should be 0)' AS check_type,
       COUNT(*) AS count
FROM "ShippingMethods"
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

SELECT 'Shipments with NULL/empty CarrierCode (should be 0)' AS check_type,
       COUNT(*) AS count
FROM "Shipments"
WHERE "CarrierCode" IS NULL OR "CarrierCode" = '';

-- =====================================================
-- STEP 5: Show CarrierCode distribution
-- =====================================================
SELECT 'ShippingMethods CarrierCode distribution' AS report_type,
       "CarrierCode", COUNT(*) AS count
FROM "ShippingMethods"
GROUP BY "CarrierCode"
ORDER BY count DESC;

SELECT 'Shipments CarrierCode distribution' AS report_type,
       "CarrierCode", COUNT(*) AS count
FROM "Shipments"
GROUP BY "CarrierCode"
ORDER BY count DESC;

-- =====================================================
-- COMMIT (or ROLLBACK to cancel)
-- =====================================================
-- If everything looks good, run:
COMMIT;

-- If something went wrong, run:
-- ROLLBACK;
