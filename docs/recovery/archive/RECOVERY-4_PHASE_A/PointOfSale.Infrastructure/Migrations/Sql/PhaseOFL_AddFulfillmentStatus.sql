-- ============================================================================
-- PHASE OFL: Order Fulfillment Lifecycle - Database Migration
-- ============================================================================
-- This migration adds the FulfillmentStatus column to the Orders table.
-- 
-- BUSINESS CONTEXT:
-- - FulfillmentStatus tracks the physical order journey (Created → Delivered)
-- - PaymentStatus tracks the financial journey (Pending → Collected → Settled)
-- - These are SEPARATE concerns that integrate via domain events
--
-- MIGRATION STEPS:
-- 1. Add FulfillmentStatus column with default value
-- 2. Backfill existing orders based on current OrderStatus
-- 3. Create index for common queries
-- ============================================================================

-- Step 1: Add FulfillmentStatus column
-- Default: 1 (Confirmed) - most existing orders should be at least confirmed
-- FulfillmentStatus enum values:
--   0 = Created
--   1 = Confirmed
--   2 = Packed
--   3 = OutForDelivery
--   4 = Delivered
--   5 = Completed
--   6 = Returned
--   7 = FailedDelivery
--   8 = Cancelled

ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "FulfillmentStatus" integer NOT NULL DEFAULT 1;

-- Step 2: Backfill existing orders based on OrderStatus
-- Map legacy OrderStatus to new FulfillmentStatus

-- Orders with Status = Delivered (4) → FulfillmentStatus = Delivered (4)
UPDATE "Orders" 
SET "FulfillmentStatus" = 4 
WHERE "Status" = 4 AND "FulfillmentStatus" = 1;

-- Orders with Status = Completed (5) → FulfillmentStatus = Completed (5)
UPDATE "Orders" 
SET "FulfillmentStatus" = 5 
WHERE "Status" = 5 AND "FulfillmentStatus" = 1;

-- Orders with Status = Shipped (3) → FulfillmentStatus = OutForDelivery (3)
UPDATE "Orders" 
SET "FulfillmentStatus" = 3 
WHERE "Status" = 3 AND "FulfillmentStatus" = 1;

-- Orders with Status = Processing (2) → FulfillmentStatus = Packed (2)
UPDATE "Orders" 
SET "FulfillmentStatus" = 2 
WHERE "Status" = 2 AND "FulfillmentStatus" = 1;

-- Orders with Status = Cancelled (6) → FulfillmentStatus = Cancelled (8)
UPDATE "Orders" 
SET "FulfillmentStatus" = 8 
WHERE "Status" = 6 AND "FulfillmentStatus" = 1;

-- Orders with Status = Refunded (7) → FulfillmentStatus = Returned (6)
-- (Refund typically happens after return)
UPDATE "Orders" 
SET "FulfillmentStatus" = 6 
WHERE "Status" = 7 AND "FulfillmentStatus" = 1;

-- Orders with Status = Pending (0) → FulfillmentStatus = Created (0)
UPDATE "Orders" 
SET "FulfillmentStatus" = 0 
WHERE "Status" = 0 AND "FulfillmentStatus" = 1;

-- Step 3: Create index for common fulfillment queries
CREATE INDEX IF NOT EXISTS "IX_Orders_FulfillmentStatus" 
ON "Orders" ("FulfillmentStatus");

-- Composite index for tenant + status queries (common admin view)
CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_FulfillmentStatus" 
ON "Orders" ("TenantId", "FulfillmentStatus");

-- Step 4: Report migration results
DO $$
DECLARE
    total_orders INT;
    created_count INT;
    confirmed_count INT;
    packed_count INT;
    out_for_delivery_count INT;
    delivered_count INT;
    completed_count INT;
    returned_count INT;
    cancelled_count INT;
BEGIN
    SELECT COUNT(*) INTO total_orders FROM "Orders";
    SELECT COUNT(*) INTO created_count FROM "Orders" WHERE "FulfillmentStatus" = 0;
    SELECT COUNT(*) INTO confirmed_count FROM "Orders" WHERE "FulfillmentStatus" = 1;
    SELECT COUNT(*) INTO packed_count FROM "Orders" WHERE "FulfillmentStatus" = 2;
    SELECT COUNT(*) INTO out_for_delivery_count FROM "Orders" WHERE "FulfillmentStatus" = 3;
    SELECT COUNT(*) INTO delivered_count FROM "Orders" WHERE "FulfillmentStatus" = 4;
    SELECT COUNT(*) INTO completed_count FROM "Orders" WHERE "FulfillmentStatus" = 5;
    SELECT COUNT(*) INTO returned_count FROM "Orders" WHERE "FulfillmentStatus" = 6;
    SELECT COUNT(*) INTO cancelled_count FROM "Orders" WHERE "FulfillmentStatus" = 8;
    
    RAISE NOTICE '╔════════════════════════════════════════════════════════════════╗';
    RAISE NOTICE '║  PHASE OFL: Order Fulfillment Lifecycle Migration Complete     ║';
    RAISE NOTICE '╠════════════════════════════════════════════════════════════════╣';
    RAISE NOTICE '║  Total Orders: %                                               ║', total_orders;
    RAISE NOTICE '╠════════════════════════════════════════════════════════════════╣';
    RAISE NOTICE '║  Created (0):          %                                       ║', created_count;
    RAISE NOTICE '║  Confirmed (1):        %                                       ║', confirmed_count;
    RAISE NOTICE '║  Packed (2):           %                                       ║', packed_count;
    RAISE NOTICE '║  OutForDelivery (3):   %                                       ║', out_for_delivery_count;
    RAISE NOTICE '║  Delivered (4):        %                                       ║', delivered_count;
    RAISE NOTICE '║  Completed (5):        %                                       ║', completed_count;
    RAISE NOTICE '║  Returned (6):         %                                       ║', returned_count;
    RAISE NOTICE '║  Cancelled (8):        %                                       ║', cancelled_count;
    RAISE NOTICE '╚════════════════════════════════════════════════════════════════╝';
END $$;
