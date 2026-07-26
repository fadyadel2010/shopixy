-- =====================================================================================
-- Migration: S59_AffiliateProductPolicySoftDeleteFilteredIndex
-- Date: 2026-04-29 17:00:00
-- Author: System Hardening
-- 
-- PROBLEM:
-- The unique index UQ_AffiliateProductPolicies_Tenant_Product on AffiliateProductPolicy
-- does not respect the soft-delete flag (IsDeleted). This prevents creating a new policy
-- when a previous one is soft-deleted, causing unique constraint violations.
--
-- SOLUTION:
-- Drop and recreate the unique index with a filter condition that excludes soft-deleted
-- rows (WHERE "IsDeleted" = false). This ensures the unique constraint applies only to
-- active (non-deleted) AffiliateProductPolicy records.
--
-- IMPACT:
-- - Allows creating new policies for the same TenantId+ProductId after soft-deleting old ones
-- - Aligns with existing soft-delete patterns used throughout the codebase
-- - Preserves query filter behavior (queries already filter IsDeleted = false)
-- - No data migration required (only index structure change)
--
-- ROLLBACK:
-- To rollback, drop the filtered index and recreate without the filter:
--   DROP INDEX IF EXISTS "UQ_AffiliateProductPolicies_Tenant_Product";
--   CREATE UNIQUE INDEX "UQ_AffiliateProductPolicies_Tenant_Product" 
--     ON "AffiliateProductPolicies" ("TenantId", "ProductId");
-- =====================================================================================

-- Drop existing unique constraint/index (without filter). Depending on which
-- migration path created the schema, this name can be either a table constraint
-- with a backing index or a standalone unique index.
ALTER TABLE "AffiliateProductPolicies"
    DROP CONSTRAINT IF EXISTS "UQ_AffiliateProductPolicies_Tenant_Product";

DROP INDEX IF EXISTS "UQ_AffiliateProductPolicies_Tenant_Product";

-- Recreate unique index with soft-delete filter
-- This ensures uniqueness only applies to active (non-deleted) records
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AffiliateProductPolicies_Tenant_Product"
    ON "AffiliateProductPolicies" ("TenantId", "ProductId")
    WHERE "IsDeleted" = false;

-- Verification query (should return 0 if no duplicate active policies exist)
-- SELECT "TenantId", "ProductId", COUNT(*) as DuplicateCount
-- FROM "AffiliateProductPolicies"
-- WHERE "IsDeleted" = false
-- GROUP BY "TenantId", "ProductId"
-- HAVING COUNT(*) > 1;
