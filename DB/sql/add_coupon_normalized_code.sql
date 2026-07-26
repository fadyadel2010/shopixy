-- =====================================================================================
-- Migration: Add NormalizedCode column to Coupons table
-- Purpose: Enable efficient case-insensitive coupon code lookups with DB-side uniqueness
-- Date: 2026-04-15
-- =====================================================================================

-- Step 1: Add NormalizedCode column (nullable initially for backfill)
ALTER TABLE "Coupons"
ADD COLUMN "NormalizedCode" text NULL;

-- Step 2: Backfill existing rows with normalized codes
-- Note: BTRIM removes leading/trailing whitespace to match C# SetCode() behavior (code.Trim().ToUpperInvariant())
UPDATE "Coupons"
SET "NormalizedCode" = UPPER(BTRIM("Code"))
WHERE "NormalizedCode" IS NULL;

-- Step 3: Make NormalizedCode NOT NULL now that all rows are populated
ALTER TABLE "Coupons"
ALTER COLUMN "NormalizedCode" SET NOT NULL;

-- Step 4: Add unique index on (TenantId, NormalizedCode) to enforce uniqueness
-- This prevents duplicate coupon codes within the same tenant (case-insensitive)
CREATE UNIQUE INDEX "IX_Coupons_TenantId_NormalizedCode"
ON "Coupons" ("TenantId", "NormalizedCode")
WHERE "IsDeleted" = false;

-- Step 5: Add regular index for lookups (includes IsDeleted for query performance)
CREATE INDEX "IX_Coupons_NormalizedCode"
ON "Coupons" ("NormalizedCode")
WHERE "IsDeleted" = false;

-- =====================================================================================
-- Verification queries (run after migration)
-- =====================================================================================

-- Verify all coupons have NormalizedCode populated
-- SELECT COUNT(*) as total, 
--        COUNT("NormalizedCode") as with_normalized,
--        COUNT(*) - COUNT("NormalizedCode") as missing
-- FROM "Coupons";

-- Check for any duplicate normalized codes within tenants
-- SELECT "TenantId", "NormalizedCode", COUNT(*) as count
-- FROM "Coupons"
-- WHERE "IsDeleted" = false
-- GROUP BY "TenantId", "NormalizedCode"
-- HAVING COUNT(*) > 1;
