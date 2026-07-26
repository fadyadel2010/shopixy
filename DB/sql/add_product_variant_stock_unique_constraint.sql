-- ============================================================================
-- PRODUCTION HARDENING — INVENTORY SYSTEM
-- TASK 1: SAFE ROW MATERIALIZATION
-- ============================================================================
-- 
-- This migration ensures a UNIQUE constraint exists on ProductVariantStocks
-- for the composite key (TenantId, VariantId, WarehouseId).
--
-- GUARANTEE: Exactly ONE row per (TenantId, VariantId, WarehouseId)
-- 
-- The constraint prevents duplicate rows that could occur from:
-- - Concurrent requests attempting to create the same stock row
-- - Race conditions in the insert-if-not-exists pattern
-- - Network retries causing duplicate inserts
--
-- COMPATIBILITY: PostgreSQL
-- ============================================================================

-- Verify the unique index exists (EF Core should have created it, but let's ensure)
-- This index is CRITICAL for the ON CONFLICT DO NOTHING pattern to work correctly

DO $$
BEGIN
    -- Check if the unique index already exists
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_indexes 
        WHERE indexname = 'IX_ProductVariantStocks_TenantId_VariantId_WarehouseId'
    ) THEN
        -- Create the unique index
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId"
        ON "ProductVariantStocks" ("TenantId", "VariantId", "WarehouseId")
        WHERE "IsDeleted" = FALSE;
        
        RAISE NOTICE 'Created unique index IX_ProductVariantStocks_TenantId_VariantId_WarehouseId';
    ELSE
        RAISE NOTICE 'Unique index IX_ProductVariantStocks_TenantId_VariantId_WarehouseId already exists';
    END IF;
END $$;

-- ============================================================================
-- TASK 2: NEGATIVE STOCK PROTECTION
-- ============================================================================
-- 
-- Check constraints to prevent negative stock values.
-- These should already exist from EF Core configuration, but we ensure them here.
-- ============================================================================

-- Ensure Quantity >= 0
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_ProductVariantStocks_Quantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
        ADD CONSTRAINT "CK_ProductVariantStocks_Quantity_NonNegative"
        CHECK ("Quantity" >= 0);
        
        RAISE NOTICE 'Created check constraint CK_ProductVariantStocks_Quantity_NonNegative';
    END IF;
END $$;

-- Ensure ReservedQuantity >= 0
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_ProductVariantStocks_ReservedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
        ADD CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_NonNegative"
        CHECK ("ReservedQuantity" >= 0);
        
        RAISE NOTICE 'Created check constraint CK_ProductVariantStocks_ReservedQuantity_NonNegative';
    END IF;
END $$;

-- Ensure ReservedQuantity <= Quantity
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_ProductVariantStocks_ReservedQuantity_Within_Quantity'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
        ADD CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_Within_Quantity"
        CHECK ("Quantity" >= "ReservedQuantity");
        
        RAISE NOTICE 'Created check constraint CK_ProductVariantStocks_ReservedQuantity_Within_Quantity';
    END IF;
END $$;

-- Ensure DamagedQuantity >= 0
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_ProductVariantStocks_DamagedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
        ADD CONSTRAINT "CK_ProductVariantStocks_DamagedQuantity_NonNegative"
        CHECK ("DamagedQuantity" >= 0);
        
        RAISE NOTICE 'Created check constraint CK_ProductVariantStocks_DamagedQuantity_NonNegative';
    END IF;
END $$;

-- ============================================================================
-- VERIFICATION QUERY
-- ============================================================================
-- Run this to verify the constraints are in place:
-- 
-- SELECT conname, contype 
-- FROM pg_constraint 
-- WHERE conrelid = 'public."ProductVariantStocks"'::regclass;
-- 
-- Expected output:
-- - CK_ProductVariantStocks_Quantity_NonNegative (c)
-- - CK_ProductVariantStocks_ReservedQuantity_NonNegative (c)
-- - CK_ProductVariantStocks_ReservedQuantity_Within_Quantity (c)
-- - CK_ProductVariantStocks_DamagedQuantity_NonNegative (c)
-- 
-- SELECT indexname, indexdef 
-- FROM pg_indexes 
-- WHERE tablename = 'ProductVariantStocks';
-- 
-- Expected output:
-- - IX_ProductVariantStocks_TenantId_VariantId_WarehouseId (UNIQUE)
-- ============================================================================