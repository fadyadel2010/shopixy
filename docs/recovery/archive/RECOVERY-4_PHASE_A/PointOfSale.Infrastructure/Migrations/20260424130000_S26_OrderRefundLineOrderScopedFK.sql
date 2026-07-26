-- =====================================================================================
-- Migration: S26_OrderRefundLineOrderScopedFK
-- Date: 2026-04-24 13:00:00
-- Description: Fix OrderRefundLine FK to ensure OrderItemId references items from 
--              the correct order using composite FK constraint
-- =====================================================================================
-- PROBLEM:
-- The current FK on OrderRefundLine.OrderItemId allows referencing OrderItems from
-- ANY order, not just the order being refunded. This is a data integrity violation.
--
-- SOLUTION:
-- 1. Add OrderId column to OrderRefundLine (denormalized for composite FK)
-- 2. Create unique index on OrderItem(OrderId, Id) as principal key
-- 3. Replace simple FK with composite FK (OrderId, OrderItemId) -> OrderItem(OrderId, Id)
-- 4. Replace the refund FK with a composite FK that enforces TenantId and OrderId
--    match the referenced OrderRefund row
-- =====================================================================================

BEGIN TRANSACTION;

-- ============================================================================
-- STEP 1: Add OrderId Column to OrderRefundLines
-- ============================================================================

ALTER TABLE "OrderRefundLines"
ADD COLUMN IF NOT EXISTS "OrderId" uuid NULL;

ALTER TABLE "OrderRefundLines"
ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;

-- ============================================================================
-- STEP 2: Populate OrderId from OrderRefund (Backfill)
-- ============================================================================

UPDATE "OrderRefundLines" orl
SET "OrderId" = (
    SELECT "OrderId" 
    FROM "OrderRefunds" 
    WHERE "Id" = orl."OrderRefundId"
)
WHERE "OrderId" IS NULL;

UPDATE "OrderRefundLines" orl
SET "TenantId" = (
    SELECT "TenantId"
    FROM "OrderRefunds"
    WHERE "Id" = orl."OrderRefundId"
)
WHERE "TenantId" IS NULL;

-- ============================================================================
-- STEP 3: Data Cleanup - Fix Mismatched OrderRefundLines
-- ============================================================================

-- Log mismatched refund lines (OrderRefundLines with OrderId != OrderRefund.OrderId)
DO $$
DECLARE
    mismatched_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO mismatched_count
    FROM "OrderRefundLines" orl
    INNER JOIN "OrderRefunds" orf ON orl."OrderRefundId" = orf."Id"
    WHERE orl."OrderId" IS NOT NULL
      AND orl."OrderId" <> orf."OrderId"
      AND orl."IsDeleted" = false;

    IF mismatched_count > 0 THEN
        RAISE WARNING 'Found % OrderRefundLines rows with mismatched OrderId that will be corrected', mismatched_count;
    END IF;
END $$;

-- Fix mismatched OrderId by aligning with parent OrderRefund
UPDATE "OrderRefundLines" orl
SET "OrderId" = orf."OrderId",
    "UpdatedAt" = NOW()
FROM "OrderRefunds" orf
WHERE orl."OrderRefundId" = orf."Id"
  AND orl."OrderId" IS NOT NULL
  AND orl."OrderId" <> orf."OrderId"
  AND orl."IsDeleted" = false;

-- Fix missing or mismatched TenantId by aligning with parent OrderRefund
UPDATE "OrderRefundLines" orl
SET "TenantId" = orf."TenantId",
    "UpdatedAt" = NOW()
FROM "OrderRefunds" orf
WHERE orl."OrderRefundId" = orf."Id"
  AND (orl."TenantId" IS NULL OR orl."TenantId" <> orf."TenantId");

-- Log orphaned refund lines (OrderRefundLines with invalid OrderItemId for the OrderId)
DO $$
DECLARE
    orphaned_items_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO orphaned_items_count
    FROM "OrderRefundLines" orl
    WHERE orl."OrderId" IS NOT NULL
      AND orl."OrderItemId" IS NOT NULL
      AND NOT EXISTS (
          SELECT 1
          FROM "OrderItems" oi
          WHERE oi."OrderId" = orl."OrderId"
            AND oi."Id" = orl."OrderItemId"
      )
      AND orl."IsDeleted" = false;

    IF orphaned_items_count > 0 THEN
        RAISE WARNING 'Found % OrderRefundLines rows with invalid OrderItemId that will be soft-deleted', orphaned_items_count;
    END IF;
END $$;

-- Soft-delete refund lines with invalid OrderItemId references
UPDATE "OrderRefundLines" orl
SET "IsDeleted" = true,
    "UpdatedAt" = NOW()
WHERE orl."OrderId" IS NOT NULL
  AND orl."OrderItemId" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM "OrderItems" oi
      WHERE oi."OrderId" = orl."OrderId"
        AND oi."Id" = orl."OrderItemId"
  )
  AND orl."IsDeleted" = false;

-- ============================================================================
-- STEP 4: Make OrderId NOT NULL
-- ============================================================================

ALTER TABLE "OrderRefundLines"
ALTER COLUMN "OrderId" SET NOT NULL;

ALTER TABLE "OrderRefundLines"
ALTER COLUMN "TenantId" SET NOT NULL;

-- ============================================================================
-- STEP 5: Create Unique Indexes for Composite Foreign Keys
-- ============================================================================

-- Create unique index on OrderItem(OrderId, Id) for composite FK principal key
CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderItems_OrderId_Id" 
ON "OrderItems" ("OrderId", "Id");

-- Create unique index on OrderRefunds(TenantId, OrderId, Id) for composite FK
CREATE UNIQUE INDEX IF NOT EXISTS "UX_OrderRefunds_TenantId_OrderId_Id"
ON "OrderRefunds" ("TenantId", "OrderId", "Id");

-- ============================================================================
-- STEP 6: Drop Old Constraints
-- ============================================================================

ALTER TABLE "OrderRefundLines"
DROP CONSTRAINT IF EXISTS "FK_OrderRefundLines_OrderItems_OrderItemId";

ALTER TABLE "OrderRefundLines"
DROP CONSTRAINT IF EXISTS "FK_OrderRefundLines_OrderRefunds_OrderRefundId";

ALTER TABLE "OrderRefundLines"
DROP CONSTRAINT IF EXISTS "FK_OrderRefundLines_OrderRefunds_TenantId_OrderRefundId";

ALTER TABLE "OrderRefundLines"
DROP CONSTRAINT IF EXISTS "CK_OrderRefundLines_OrderId_Matches_OrderRefund";

-- ============================================================================
-- STEP 7: Add Composite FK Constraints with NOT VALID (Non-Blocking)
-- ============================================================================

-- Add composite FK for OrderRefundLines -> OrderItems (NOT VALID for non-blocking add)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_OrderRefundLines_OrderItems_OrderId_OrderItemId'
    ) THEN
        ALTER TABLE "OrderRefundLines"
            ADD CONSTRAINT "FK_OrderRefundLines_OrderItems_OrderId_OrderItemId"
            FOREIGN KEY ("OrderId", "OrderItemId")
            REFERENCES "OrderItems" ("OrderId", "Id")
            ON DELETE RESTRICT
            NOT VALID;
    END IF;
END $$;

-- Add composite FK for OrderRefundLines -> OrderRefunds (NOT VALID for non-blocking add)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId'
    ) THEN
        ALTER TABLE "OrderRefundLines"
            ADD CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId"
            FOREIGN KEY ("TenantId", "OrderId", "OrderRefundId")
            REFERENCES "OrderRefunds" ("TenantId", "OrderId", "Id")
            ON DELETE CASCADE
            NOT VALID;
    END IF;
END $$;

-- ============================================================================
-- STEP 8: Validate Constraints (Scans Existing Rows)
-- ============================================================================
-- NOTE: VALIDATE CONSTRAINT scans existing rows but does not block writes.
-- It only blocks concurrent schema changes on the same table.

ALTER TABLE "OrderRefundLines"
    VALIDATE CONSTRAINT "FK_OrderRefundLines_OrderItems_OrderId_OrderItemId";

ALTER TABLE "OrderRefundLines"
    VALIDATE CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId";

COMMIT;

-- =====================================================================================
-- VERIFICATION QUERIES (Should Return 0 Rows After Migration)
-- =====================================================================================

-- Verify the composite FK is working:
SELECT orl."Id", orl."OrderId", orl."OrderItemId"
FROM "OrderRefundLines" orl
WHERE NOT EXISTS (
    SELECT 1
    FROM "OrderItems" oi
    WHERE oi."OrderId" = orl."OrderId"
      AND oi."Id" = orl."OrderItemId"
)
AND orl."IsDeleted" = false;

-- Verify OrderId consistency:
SELECT orl."Id", orl."OrderId", orl."OrderRefundId", orf."OrderId" as RefundOrderId
FROM "OrderRefundLines" orl
JOIN "OrderRefunds" orf ON orl."OrderRefundId" = orf."Id"
WHERE orl."OrderId" != orf."OrderId"
  AND orl."IsDeleted" = false;

-- Verify TenantId + OrderId + OrderRefundId consistency:
SELECT orl."Id", orl."TenantId", orl."OrderId", orl."OrderRefundId"
FROM "OrderRefundLines" orl
WHERE NOT EXISTS (
    SELECT 1
    FROM "OrderRefunds" orf
    WHERE orf."TenantId" = orl."TenantId"
      AND orf."OrderId" = orl."OrderId"
      AND orf."Id" = orl."OrderRefundId"
)
AND orl."IsDeleted" = false;
