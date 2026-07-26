-- Multi-Unit Support Data Migration Script
-- This script seeds default units and migrates existing products to use the multi-unit system

-- Step 1: Insert default units
DO $$
DECLARE
    piece_unit_id UUID := gen_random_uuid();
    carton_unit_id UUID := gen_random_uuid();
    box_unit_id UUID := gen_random_uuid();
    kg_unit_id UUID := gen_random_uuid();
    gram_unit_id UUID := gen_random_uuid();
    liter_unit_id UUID := gen_random_uuid();
    ml_unit_id UUID := gen_random_uuid();
    dozen_unit_id UUID := gen_random_uuid();
BEGIN
    -- Insert common units
    INSERT INTO "Units" ("Id", "Name", "Symbol", "Description", "IsActive", "CreatedAt", "IsDeleted")
    VALUES 
        (piece_unit_id, 'Piece', 'pc', 'Individual piece or item', true, NOW(), false),
        (carton_unit_id, 'Carton', 'ctn', 'Carton or case', true, NOW(), false),
        (box_unit_id, 'Box', 'box', 'Box', true, NOW(), false),
        (kg_unit_id, 'Kilogram', 'kg', 'Kilogram', true, NOW(), false),
        (gram_unit_id, 'Gram', 'g', 'Gram', true, NOW(), false),
        (liter_unit_id, 'Liter', 'L', 'Liter', true, NOW(), false),
        (ml_unit_id, 'Milliliter', 'ml', 'Milliliter', true, NOW(), false),
        (dozen_unit_id, 'Dozen', 'dz', 'Dozen (12 pieces)', true, NOW(), false)
    ON CONFLICT DO NOTHING;

    -- Step 2: Migrate existing products to use Piece as base unit
    -- For each product, create a ProductUnit record with the existing prices
    INSERT INTO "ProductUnits" ("Id", "ProductId", "UnitId", "ConversionFactor", "PurchasePrice", "SellingPrice", "IsDefault", "IsBaseUnit", "CreatedAt", "IsDeleted")
    SELECT 
        gen_random_uuid(),
        p."Id",
        piece_unit_id,
        1.0, -- Base unit always has conversion factor of 1
        p."PurchasePrice",
        p."SalePrice",
        true, -- Set as default unit
        true, -- Set as base unit
        NOW(),
        false
    FROM "Products" p
    WHERE NOT EXISTS (
        SELECT 1 FROM "ProductUnits" pu WHERE pu."ProductId" = p."Id"
    );

    -- Step 3: Update existing transaction items to use base quantity
    -- For backward compatibility, set BaseQuantity = Quantity for existing records
    UPDATE "PurchaseInvoiceItems"
    SET "BaseQuantity" = "Quantity"
    WHERE "BaseQuantity" = 0 OR "BaseQuantity" IS NULL;

    UPDATE "SalesInvoiceItems"
    SET "BaseQuantity" = "Quantity"
    WHERE "BaseQuantity" = 0 OR "BaseQuantity" IS NULL;

    UPDATE "ReturnItems"
    SET "BaseQuantity" = "Quantity"
    WHERE "BaseQuantity" = 0 OR "BaseQuantity" IS NULL;

    RAISE NOTICE 'Multi-unit migration completed successfully!';
    RAISE NOTICE 'Default units created: Piece, Carton, Box, Kilogram, Gram, Liter, Milliliter, Dozen';
    RAISE NOTICE 'All existing products migrated to use Piece as base unit';
    RAISE NOTICE 'All existing transactions updated with base quantities';
END $$;

-- Verify the migration
SELECT 
    'Units Created' as "Status",
    COUNT(*) as "Count"
FROM "Units"
WHERE "IsDeleted" = false

UNION ALL

SELECT 
    'Products with Units' as "Status",
    COUNT(DISTINCT "ProductId") as "Count"
FROM "ProductUnits"
WHERE "IsDeleted" = false

UNION ALL

SELECT 
    'Base Units Configured' as "Status",
    COUNT(*) as "Count"
FROM "ProductUnits"
WHERE "IsBaseUnit" = true AND "IsDeleted" = false;
