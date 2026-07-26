-- ============================================================================
-- Migration: S25_OrderRefundTenantConsistencyConstraints
-- Description: Enforce tenant consistency between order refunds and parents
--              with composite foreign keys instead of invalid subquery CHECKs.
-- Date: 2026-04-24
-- ============================================================================

BEGIN;

ALTER TABLE "OrderRefundLines"
    ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;

UPDATE "OrderRefundLines" orl
SET "TenantId" = orf."TenantId",
    "UpdatedAt" = NOW()
FROM "OrderRefunds" orf
WHERE orl."OrderRefundId" = orf."Id"
  AND (orl."TenantId" IS NULL OR orl."TenantId" <> orf."TenantId");

ALTER TABLE "OrderRefundLines"
    ALTER COLUMN "TenantId" SET NOT NULL;

-- ============================================================================
-- STEP 1: Data Cleanup - Fix or Remove Orphaned/Cross-Tenant Refunds
-- ============================================================================

-- Log orphaned refunds (OrderRefunds pointing to non-existent Orders)
DO $$
DECLARE
    orphaned_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO orphaned_count
    FROM "OrderRefunds" orf
    WHERE NOT EXISTS (
        SELECT 1
        FROM "Orders" o
        WHERE o."Id" = orf."OrderId"
          AND o."TenantId" = orf."TenantId"
    )
    AND orf."IsDeleted" = false;

    IF orphaned_count > 0 THEN
        RAISE WARNING 'Found % orphaned OrderRefunds rows that will be soft-deleted', orphaned_count;
    END IF;
END $$;

-- Soft-delete orphaned refunds (no matching order)
UPDATE "OrderRefunds"
SET "IsDeleted" = true,
    "UpdatedAt" = NOW()
WHERE NOT EXISTS (
    SELECT 1
    FROM "Orders" o
    WHERE o."Id" = "OrderRefunds"."OrderId"
      AND o."TenantId" = "OrderRefunds"."TenantId"
)
AND "IsDeleted" = false;

-- Log cross-tenant refunds (OrderRefunds with mismatched TenantId)
DO $$
DECLARE
    cross_tenant_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO cross_tenant_count
    FROM "OrderRefunds" orf
    INNER JOIN "Orders" o ON orf."OrderId" = o."Id"
    WHERE orf."TenantId" <> o."TenantId"
      AND orf."IsDeleted" = false;

    IF cross_tenant_count > 0 THEN
        RAISE WARNING 'Found % cross-tenant OrderRefunds rows that will be corrected', cross_tenant_count;
    END IF;
END $$;

-- Fix cross-tenant refunds by aligning TenantId with parent Order
UPDATE "OrderRefunds" orf
SET "TenantId" = o."TenantId",
    "UpdatedAt" = NOW()
FROM "Orders" o
WHERE orf."OrderId" = o."Id"
  AND orf."TenantId" <> o."TenantId"
  AND orf."IsDeleted" = false;

-- ============================================================================
-- STEP 2: Data Cleanup - Fix or Remove Orphaned/Cross-Tenant Refund Lines
-- ============================================================================

-- Log orphaned refund lines (OrderRefundLines pointing to non-existent OrderRefunds)
DO $$
DECLARE
    orphaned_lines_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO orphaned_lines_count
    FROM "OrderRefundLines" orl
    WHERE NOT EXISTS (
        SELECT 1
        FROM "OrderRefunds" orf
        WHERE orf."Id" = orl."OrderRefundId"
          AND orf."TenantId" = orl."TenantId"
    )
    AND orl."IsDeleted" = false;

    IF orphaned_lines_count > 0 THEN
        RAISE WARNING 'Found % orphaned OrderRefundLines rows that will be soft-deleted', orphaned_lines_count;
    END IF;
END $$;

-- Soft-delete orphaned refund lines (no matching refund)
UPDATE "OrderRefundLines"
SET "IsDeleted" = true,
    "UpdatedAt" = NOW()
WHERE NOT EXISTS (
    SELECT 1
    FROM "OrderRefunds" orf
    WHERE orf."Id" = "OrderRefundLines"."OrderRefundId"
      AND orf."TenantId" = "OrderRefundLines"."TenantId"
)
AND "IsDeleted" = false;

-- Log cross-tenant refund lines (OrderRefundLines with mismatched TenantId)
DO $$
DECLARE
    cross_tenant_lines_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO cross_tenant_lines_count
    FROM "OrderRefundLines" orl
    INNER JOIN "OrderRefunds" orf ON orl."OrderRefundId" = orf."Id"
    WHERE orl."TenantId" <> orf."TenantId"
      AND orl."IsDeleted" = false;

    IF cross_tenant_lines_count > 0 THEN
        RAISE WARNING 'Found % cross-tenant OrderRefundLines rows that will be corrected', cross_tenant_lines_count;
    END IF;
END $$;

-- Fix cross-tenant refund lines by aligning TenantId with parent OrderRefund
UPDATE "OrderRefundLines" orl
SET "TenantId" = orf."TenantId",
    "UpdatedAt" = NOW()
FROM "OrderRefunds" orf
WHERE orl."OrderRefundId" = orf."Id"
  AND orl."TenantId" <> orf."TenantId"
  AND orl."IsDeleted" = false;

-- ============================================================================
-- STEP 3: Create Unique Indexes for Composite Foreign Keys
-- ============================================================================

-- PostgreSQL does not allow subqueries inside CHECK constraints. Use composite
-- foreign keys so a refund row cannot point to an order from another tenant.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Orders_TenantId_Id"
    ON "Orders" ("TenantId", "Id");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_OrderRefunds_TenantId_Id"
    ON "OrderRefunds" ("TenantId", "Id");

-- ============================================================================
-- STEP 4: Drop Old CHECK Constraints
-- ============================================================================

ALTER TABLE "OrderRefunds"
    DROP CONSTRAINT IF EXISTS "CK_OrderRefunds_TenantId_Matches_Order";

ALTER TABLE "OrderRefundLines"
    DROP CONSTRAINT IF EXISTS "CK_OrderRefundLines_TenantId_Matches_OrderRefund";

-- ============================================================================
-- STEP 5: Add Foreign Key Constraints with NOT VALID (Non-Blocking)
-- ============================================================================

-- Add FK constraint for OrderRefunds -> Orders (NOT VALID for non-blocking add)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_OrderRefunds_Orders_TenantId_OrderId'
    ) THEN
        ALTER TABLE "OrderRefunds"
            ADD CONSTRAINT "FK_OrderRefunds_Orders_TenantId_OrderId"
            FOREIGN KEY ("TenantId", "OrderId")
            REFERENCES "Orders" ("TenantId", "Id")
            ON DELETE RESTRICT
            NOT VALID;
    END IF;
END $$;

-- Add FK constraint for OrderRefundLines -> OrderRefunds (NOT VALID for non-blocking add)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_OrderRefundLines_OrderRefunds_TenantId_OrderRefundId'
    ) THEN
        ALTER TABLE "OrderRefundLines"
            ADD CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderRefundId"
            FOREIGN KEY ("TenantId", "OrderRefundId")
            REFERENCES "OrderRefunds" ("TenantId", "Id")
            ON DELETE CASCADE
            NOT VALID;
    END IF;
END $$;

-- ============================================================================
-- STEP 6: Validate Constraints (Scans Existing Rows)
-- ============================================================================
-- NOTE: VALIDATE CONSTRAINT scans existing rows but does not block writes.
-- It only blocks concurrent schema changes on the same table.

ALTER TABLE "OrderRefunds"
    VALIDATE CONSTRAINT "FK_OrderRefunds_Orders_TenantId_OrderId";

ALTER TABLE "OrderRefundLines"
    VALIDATE CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderRefundId";

COMMIT;

-- ============================================================================
-- Verification Queries (Should Return 0 Rows After Migration)
-- ============================================================================

SELECT
    orf."Id" AS "RefundId",
    orf."TenantId" AS "RefundTenantId",
    o."TenantId" AS "OrderTenantId",
    orf."OrderId"
FROM "OrderRefunds" orf
INNER JOIN "Orders" o ON orf."OrderId" = o."Id"
WHERE orf."TenantId" <> o."TenantId"
  AND orf."IsDeleted" = false;

SELECT
    orl."Id" AS "RefundLineId",
    orl."TenantId" AS "RefundLineTenantId",
    orf."TenantId" AS "RefundTenantId",
    orl."OrderRefundId"
FROM "OrderRefundLines" orl
INNER JOIN "OrderRefunds" orf ON orl."OrderRefundId" = orf."Id"
WHERE orl."TenantId" <> orf."TenantId"
  AND orl."IsDeleted" = false;
