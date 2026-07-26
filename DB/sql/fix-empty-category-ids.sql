-- =====================================================================================
-- SECURITY FIX: Prevent Guid.Empty CategoryId to avoid category-scoped coupon bypass
-- =====================================================================================
-- 
-- ISSUE: Products with CategoryId = '00000000-0000-0000-0000-000000000000' can bypass
--        category-scoped coupons because CouponScopeValidation filters out Guid.Empty.
--
-- FIX: 1. Identify products with empty CategoryId
--      2. Report them for manual review
--      3. Optionally assign to a default "Uncategorized" category
--      4. Add CHECK constraint to prevent future occurrences
-- =====================================================================================

BEGIN;

-- Step 1: Check if any products have Guid.Empty CategoryId
DO $$
DECLARE
    empty_category_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO empty_category_count
    FROM "Products"
    WHERE "CategoryId" = '00000000-0000-0000-0000-000000000000';
    
    IF empty_category_count > 0 THEN
        RAISE NOTICE 'WARNING: Found % product(s) with empty CategoryId', empty_category_count;
        RAISE NOTICE 'These products could bypass category-scoped coupons!';
        
        -- Log the problematic products
        RAISE NOTICE 'Affected Products:';
        FOR rec IN 
            SELECT "Id", "Name", "TenantId", "SKU", "Barcode"
            FROM "Products"
            WHERE "CategoryId" = '00000000-0000-0000-0000-000000000000'
        LOOP
            RAISE NOTICE '  - Product ID: %, Name: %, SKU: %, Barcode: %', 
                rec."Id", rec."Name", rec."SKU", rec."Barcode";
        END LOOP;
    ELSE
        RAISE NOTICE 'Good: No products found with empty CategoryId';
    END IF;
END $$;

-- Step 2: Create an "Uncategorized" category for each tenant if needed
-- (Only if you want to auto-fix; otherwise, manual review is recommended)
/*
INSERT INTO "Categories" ("Id", "TenantId", "Name", "NameAr", "IsActive", "CreatedAt", "UpdatedAt")
SELECT 
    gen_random_uuid(),
    DISTINCT "TenantId",
    'Uncategorized',
    'غير مصنف',
    true,
    NOW(),
    NOW()
FROM "Products"
WHERE "CategoryId" = '00000000-0000-0000-0000-000000000000'
  AND NOT EXISTS (
    SELECT 1 FROM "Categories" c 
    WHERE c."TenantId" = "Products"."TenantId" 
      AND c."Name" = 'Uncategorized'
  );
*/

-- Step 3: Update products to use the Uncategorized category
-- (Uncomment if you want to auto-fix)
/*
UPDATE "Products" p
SET "CategoryId" = c."Id",
    "UpdatedAt" = NOW()
FROM "Categories" c
WHERE p."CategoryId" = '00000000-0000-0000-0000-000000000000'
  AND c."TenantId" = p."TenantId"
  AND c."Name" = 'Uncategorized';
*/

-- Step 4: Add CHECK constraint to prevent Guid.Empty CategoryId
-- This ensures no future products can have an empty CategoryId
ALTER TABLE "Products"
ADD CONSTRAINT "CK_Products_CategoryId_NotEmpty"
CHECK ("CategoryId" != '00000000-0000-0000-0000-000000000000');

-- Verify the constraint was added
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_Products_CategoryId_NotEmpty'
    ) THEN
        RAISE NOTICE 'SUCCESS: CHECK constraint added to prevent empty CategoryId';
    ELSE
        RAISE EXCEPTION 'FAILED: CHECK constraint was not added';
    END IF;
END $$;

COMMIT;

-- =====================================================================================
-- VERIFICATION QUERIES
-- =====================================================================================

-- Check for any remaining products with empty CategoryId
SELECT 
    "Id", 
    "Name", 
    "TenantId", 
    "CategoryId", 
    "SKU", 
    "Barcode"
FROM "Products"
WHERE "CategoryId" = '00000000-0000-0000-0000-000000000000';

-- Verify the constraint exists
SELECT 
    conname AS constraint_name,
    contype AS constraint_type,
    pg_get_constraintdef(oid) AS constraint_definition
FROM pg_constraint
WHERE conrelid = '"Products"'::regclass
  AND conname = 'CK_Products_CategoryId_NotEmpty';
