-- =============================================================================
-- FIX: Coupon Unique Index for Multi-Tenant Support
-- =============================================================================
-- Issue: The current unique index IX_Coupons_Code is only on Code column,
--        which prevents the same coupon code from existing for different tenants.
-- Fix: Create a composite unique index on (TenantId, Code) instead.
-- =============================================================================

-- Step 1: Drop the old unique index on Code only
DROP INDEX IF EXISTS "IX_Coupons_Code";

-- Step 2: Create new composite unique index on (TenantId, Code)
-- This allows the same coupon code to exist for different tenants
CREATE UNIQUE INDEX "IX_Coupons_TenantId_Code" 
ON "Coupons" ("TenantId", "Code") 
WHERE "IsDeleted" = false;

-- Verify the index was created
SELECT indexname, indexdef 
FROM pg_indexes 
WHERE tablename = 'Coupons';
