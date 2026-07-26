-- Migration: Add OnlineCustomerId to Orders table
-- Phase 10.4: Fixes FK constraint violation when storefront customers place orders
-- 
-- Problem: Order.CustomerId references Customers table (POS customers)
--          but storefront orders need to reference OnlineCustomers table
-- 
-- Solution: Add OnlineCustomerId column that references OnlineCustomers

-- Step 1: Add the OnlineCustomerId column
ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "OnlineCustomerId" uuid NULL;

-- Step 2: Create the foreign key constraint to OnlineCustomers
ALTER TABLE "Orders"
ADD CONSTRAINT "FK_Orders_OnlineCustomers_OnlineCustomerId" 
FOREIGN KEY ("OnlineCustomerId") 
REFERENCES "OnlineCustomers" ("Id")
ON DELETE SET NULL;

-- Step 3: Create an index for performance
CREATE INDEX IF NOT EXISTS "IX_Orders_OnlineCustomerId" 
ON "Orders" ("OnlineCustomerId");

-- Step 4: Update EF Core migration history (optional - for tracking)
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260109175711_AddOnlineCustomerIdToOrders', '9.0.0');

-- Verify the column was added:
-- SELECT column_name, data_type, is_nullable 
-- FROM information_schema.columns 
-- WHERE table_name = 'Orders' AND column_name = 'OnlineCustomerId';
