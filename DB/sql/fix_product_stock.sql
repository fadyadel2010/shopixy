-- =============================================================================
-- FIX: Add Stock for Product SKU 303030
-- =============================================================================
-- The checkout is failing because the product has 0 stock in WarehouseStocks.
-- Run this in pgAdmin4 to add stock for testing.
-- =============================================================================

-- Step 1: Find the product and warehouse info
SELECT p."Id" AS "ProductId", p."Name", p."SKU", w."Id" AS "WarehouseId", w."Name" AS "WarehouseName"
FROM "Products" p
CROSS JOIN "Warehouses" w
WHERE p."SKU" = '303030'
  AND p."IsDeleted" = false
  AND w."IsDeleted" = false
LIMIT 1;

-- Step 2: Check existing stock for this product
SELECT ws.*, p."Name" AS "ProductName", w."Name" AS "WarehouseName"
FROM "WarehouseStocks" ws
JOIN "Products" p ON p."Id" = ws."ProductId"
JOIN "Warehouses" w ON w."Id" = ws."WarehouseId"
WHERE p."SKU" = '303030';

-- Step 3: Add stock (adjust the IDs from Step 1 results)
-- UNCOMMENT AND RUN AFTER GETTING THE IDs FROM STEP 1

-- INSERT INTO "WarehouseStocks" ("Id", "TenantId", "ProductId", "WarehouseId", "Quantity", "CreatedAt", "IsDeleted")
-- VALUES (
--     gen_random_uuid(),
--     '3b55c4a3-81b8-4df7-bca2-cf9f2e852801',  -- Your TenantId
--     '9b5c5c22-f721-4c36-aa75-813a507932c5',  -- ProductId from Step 1
--     '<WAREHOUSE_ID>',                          -- WarehouseId from Step 1
--     100,                                        -- Quantity to add
--     NOW(),
--     false
-- );

-- Alternative: If you already have a warehouse stock record, update it
-- UPDATE "WarehouseStocks" 
-- SET "Quantity" = 100
-- WHERE "ProductId" = '9b5c5c22-f721-4c36-aa75-813a507932c5';

-- Step 4: Verify stock was added
SELECT ws.*, p."Name" AS "ProductName", w."Name" AS "WarehouseName"
FROM "WarehouseStocks" ws
JOIN "Products" p ON p."Id" = ws."ProductId"
JOIN "Warehouses" w ON w."Id" = ws."WarehouseId"
WHERE p."SKU" = '303030';
