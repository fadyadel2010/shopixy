-- ============================================================================
-- Migration: S24_OrderRefundLineTenantIsolation
-- Description: Add TenantId to OrderRefundLine for proper tenant isolation
-- Author: System
-- Date: 2026-04-24
-- ============================================================================

-- Add TenantId column to OrderRefundLines (nullable initially for backfill)
ALTER TABLE "OrderRefundLines"
    ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;

-- Backfill TenantId from parent OrderRefund
UPDATE "OrderRefundLines" AS orl
SET "TenantId" = orf."TenantId"
FROM "OrderRefunds" AS orf
WHERE orl."OrderRefundId" = orf."Id"
  AND orl."TenantId" IS NULL;

-- Make TenantId NOT NULL after backfill
ALTER TABLE "OrderRefundLines"
    ALTER COLUMN "TenantId" SET NOT NULL;

-- Add index for tenant isolation query filter performance
CREATE INDEX IF NOT EXISTS "IX_OrderRefundLines_TenantId_IsDeleted"
    ON "OrderRefundLines" ("TenantId", "IsDeleted")
    INCLUDE ("Id", "OrderRefundId", "OrderItemId", "Amount");

-- ============================================================================
-- Verification Queries
-- ============================================================================

-- Verify all OrderRefundLines have TenantId
SELECT
    COUNT(*) AS "TotalRefundLines",
    COUNT(*) FILTER (WHERE "TenantId" IS NULL) AS "NullTenantIds",
    COUNT(DISTINCT "TenantId") AS "UniqueTenants"
FROM "OrderRefundLines"
WHERE "IsDeleted" = false;

-- Verify TenantId matches parent OrderRefund
SELECT
    COUNT(*) AS "MismatchedTenantIds"
FROM "OrderRefundLines" AS orl
INNER JOIN "OrderRefunds" AS orf ON orl."OrderRefundId" = orf."Id"
WHERE orl."TenantId" <> orf."TenantId"
  AND orl."IsDeleted" = false;

-- Expected: TotalRefundLines > 0, NullTenantIds = 0, MismatchedTenantIds = 0
