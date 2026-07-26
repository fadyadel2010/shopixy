-- =============================================================================
-- PHASE 11.4.2.A: ORDER ITEM FINANCIAL MODEL & PAYMENT ALLOCATION
-- Migration: Add PaidAmount and RefundedAmount to OrderItems
-- 
-- CRITICAL RULES:
-- - MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS
-- - Every SKU knows exactly how much money was paid and refunded for it
-- - Order.Total = SUM(OrderItems.Subtotal), but money lives on OrderItems
-- - This enables: Partial payments, Partial refunds, SKU-level disputes
-- 
-- Run this script in pgAdmin4 to apply the migration.
-- =============================================================================

-- Step 1: Add PaidAmount column (default 0 for existing orders)
ALTER TABLE "OrderItems" 
ADD COLUMN IF NOT EXISTS "PaidAmount" numeric(18,2) NOT NULL DEFAULT 0;

-- Step 2: Add RefundedAmount column (default 0 for existing orders)
ALTER TABLE "OrderItems" 
ADD COLUMN IF NOT EXISTS "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0;

-- Step 3: For existing PAID orders, backfill PaidAmount from Order status
-- This reconciles existing data with the new financial model
UPDATE "OrderItems" oi
SET "PaidAmount" = oi."UnitPrice" * oi."Quantity"
FROM "Orders" o
WHERE oi."OrderId" = o."Id"
  AND o."IsPaid" = true
  AND oi."PaidAmount" = 0;

-- Alternative: If Order has PaymentStatus = 'Paid' (enum value 2)
UPDATE "OrderItems" oi
SET "PaidAmount" = oi."UnitPrice" * oi."Quantity"
FROM "Orders" o
WHERE oi."OrderId" = o."Id"
  AND o."PaymentStatus" = 2
  AND oi."PaidAmount" = 0;

-- Step 4: Update EF Migrations history table to mark migration as applied
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260113174500_AddOrderItemFinancialsPhase11_4_2A', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- =============================================================================
-- Verification query - run after migration to verify columns exist
-- =============================================================================
-- SELECT 
--     column_name, 
--     data_type, 
--     is_nullable,
--     column_default
-- FROM information_schema.columns 
-- WHERE table_name = 'OrderItems' 
-- AND column_name IN ('PaidAmount', 'RefundedAmount');

-- =============================================================================
-- Verification query - check reconciliation of paid orders
-- =============================================================================
-- SELECT 
--     o."OrderNumber",
--     o."IsPaid",
--     o."PaymentStatus",
--     oi."SKU",
--     oi."UnitPrice",
--     oi."Quantity",
--     oi."UnitPrice" * oi."Quantity" as "Subtotal",
--     oi."PaidAmount",
--     oi."RefundedAmount",
--     CASE WHEN oi."PaidAmount" >= (oi."UnitPrice" * oi."Quantity") THEN 'FULLY_PAID' ELSE 'PENDING' END as "ItemStatus"
-- FROM "OrderItems" oi
-- JOIN "Orders" o ON oi."OrderId" = o."Id"
-- WHERE o."IsDeleted" = false
-- ORDER BY o."CreatedAt" DESC
-- LIMIT 20;

-- =============================================================================
-- PHASE 11.4.2.A Summary:
-- - PaidAmount: Tracks how much money has been applied to each SKU
-- - RefundedAmount: Tracks how much money has been refunded for each SKU
-- - Subtotal: Computed as UnitPrice * Quantity (in entity, not stored in DB)
-- - IsFullyPaid: Computed as PaidAmount >= Subtotal (in entity)
-- - IsFullyRefunded: Computed as RefundedAmount >= PaidAmount (in entity)
-- =============================================================================
