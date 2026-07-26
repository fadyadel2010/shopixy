-- =============================================================================
-- PHASE 11.4.1: VARIANT-BASED CHECKOUT & INVENTORY RESERVATION
-- Migration: Add VariantId and VariantAttributesJson to OrderItems
-- 
-- CRITICAL RULES:
-- - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
-- - VariantId is the SOURCE OF TRUTH for pricing and stock
-- - Orders now store immutable variant snapshots
-- 
-- Run this script in pgAdmin4 to apply the migration.
-- =============================================================================

-- Step 1: Add VariantId column (nullable for backward compatibility with legacy orders)
ALTER TABLE "OrderItems" 
ADD COLUMN IF NOT EXISTS "VariantId" uuid NULL;

-- Step 2: Add VariantAttributesJson column for storing variant attributes snapshot
ALTER TABLE "OrderItems" 
ADD COLUMN IF NOT EXISTS "VariantAttributesJson" text NULL;

-- Step 3: Create index on VariantId for SKU-level queries
CREATE INDEX IF NOT EXISTS "IX_OrderItems_VariantId" 
ON "OrderItems" ("VariantId");

-- Step 4: Add foreign key constraint to ProductVariants table
-- Only add if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_OrderItems_ProductVariants_VariantId' 
        AND table_name = 'OrderItems'
    ) THEN
        ALTER TABLE "OrderItems" 
        ADD CONSTRAINT "FK_OrderItems_ProductVariants_VariantId" 
        FOREIGN KEY ("VariantId") 
        REFERENCES "ProductVariants" ("Id") 
        ON DELETE SET NULL;
    END IF;
END $$;

-- Step 5: Update EF Migrations history table to mark migration as applied
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260113153904_AddVariantIdToOrderItemsPhase11_4_1', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- =============================================================================
-- Verification query - run after migration to verify columns exist
-- =============================================================================
-- SELECT 
--     column_name, 
--     data_type, 
--     is_nullable
-- FROM information_schema.columns 
-- WHERE table_name = 'OrderItems' 
-- AND column_name IN ('VariantId', 'VariantAttributesJson');
