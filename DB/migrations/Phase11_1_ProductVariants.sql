-- ============================================================================
-- PHASE 11.1 — PRODUCT VARIANTS MIGRATION (For pgAdmin4)
-- 
-- Run this directly in pgAdmin4 to apply the Phase 11.1 changes.
-- This script is idempotent - safe to run multiple times.
--
-- WHAT THIS DOES:
-- 1. Modifies ProductVariants table structure
-- 2. Creates ProductVariantStocks table
-- 3. Creates ProductVariantAttributes table  
-- 4. Creates default variants for all existing products
-- 5. Migrates stock data to variant-level
-- 6. Updates EF Core migrations history
-- ============================================================================

BEGIN;

-- ============================================================================
-- STEP 1: Modify ProductVariants table - Drop old columns if exist
-- ============================================================================

-- Drop old index if exists
DROP INDEX IF EXISTS "IX_ProductVariants_ProductId";

-- Drop old columns if they exist
DO $$ 
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'AdditionalPrice') THEN
        ALTER TABLE "ProductVariants" DROP COLUMN "AdditionalPrice";
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'Name') THEN
        ALTER TABLE "ProductVariants" DROP COLUMN "Name";
    END IF;
END $$;

-- ============================================================================
-- STEP 2: Add new columns to ProductVariants if they don't exist
-- ============================================================================

DO $$ 
BEGIN
    -- Add TenantId
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'TenantId') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
    
    -- Add Price
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'Price') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "Price" numeric(18,2) NOT NULL DEFAULT 0;
    END IF;
    
    -- Add OnlinePrice
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'OnlinePrice') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "OnlinePrice" numeric(18,2);
    END IF;
    
    -- Add CostPrice
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'CostPrice') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "CostPrice" numeric(18,2) NOT NULL DEFAULT 0;
    END IF;
    
    -- Add IsDefault
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'IsDefault') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "IsDefault" boolean NOT NULL DEFAULT false;
    END IF;
    
    -- Add ImageUrl
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'ImageUrl') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "ImageUrl" character varying(500);
    END IF;
    
    -- Add SortOrder
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ProductVariants' AND column_name = 'SortOrder') THEN
        ALTER TABLE "ProductVariants" ADD COLUMN "SortOrder" integer NOT NULL DEFAULT 0;
    END IF;
END $$;

-- Alter SKU and Barcode column types
ALTER TABLE "ProductVariants" ALTER COLUMN "SKU" TYPE character varying(100);
ALTER TABLE "ProductVariants" ALTER COLUMN "Barcode" TYPE character varying(100);

-- ============================================================================
-- STEP 3: Create ProductVariantAttributes table
-- ============================================================================

CREATE TABLE IF NOT EXISTS "ProductVariantAttributes" (
    "Id" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Value" character varying(255) NOT NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_ProductVariantAttributes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProductVariantAttributes_ProductVariants_VariantId" FOREIGN KEY ("VariantId") 
        REFERENCES "ProductVariants" ("Id") ON DELETE CASCADE
);

-- ============================================================================
-- STEP 4: Create ProductVariantStocks table
-- ============================================================================

CREATE TABLE IF NOT EXISTS "ProductVariantStocks" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "Quantity" numeric(18,2) NOT NULL DEFAULT 0,
    "ReservedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_ProductVariantStocks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProductVariantStocks_ProductVariants_VariantId" FOREIGN KEY ("VariantId") 
        REFERENCES "ProductVariants" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProductVariantStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") 
        REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
);

-- ============================================================================
-- STEP 5: Create indexes (idempotent)
-- ============================================================================

CREATE INDEX IF NOT EXISTS "IX_ProductVariants_Barcode" 
    ON "ProductVariants" ("Barcode") 
    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_ProductVariants_SKU" 
    ON "ProductVariants" ("SKU") 
    WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_ProductId" 
    ON "ProductVariants" ("TenantId", "ProductId");

CREATE INDEX IF NOT EXISTS "IX_ProductVariants_ProductId_IsDefault" 
    ON "ProductVariants" ("ProductId", "IsDefault") 
    WHERE "IsDefault" = true;

CREATE INDEX IF NOT EXISTS "IX_ProductVariantAttributes_VariantId" 
    ON "ProductVariantAttributes" ("VariantId");

CREATE INDEX IF NOT EXISTS "IX_ProductVariantAttributes_VariantId_Name" 
    ON "ProductVariantAttributes" ("VariantId", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId" 
    ON "ProductVariantStocks" ("TenantId", "VariantId", "WarehouseId");

CREATE INDEX IF NOT EXISTS "IX_ProductVariantStocks_VariantId" 
    ON "ProductVariantStocks" ("VariantId");

CREATE INDEX IF NOT EXISTS "IX_ProductVariantStocks_WarehouseId" 
    ON "ProductVariantStocks" ("WarehouseId");

-- ============================================================================
-- STEP 6: Update existing ProductVariants with TenantId and pricing from parent Product
-- ============================================================================

UPDATE "ProductVariants" pv
SET "TenantId" = p."TenantId",
    "IsDefault" = true,
    "Price" = p."SalePrice",
    "OnlinePrice" = p."OnlinePrice",
    "CostPrice" = p."PurchasePrice"
FROM "Products" p
WHERE pv."ProductId" = p."Id"
  AND pv."TenantId" = '00000000-0000-0000-0000-000000000000';

-- ============================================================================
-- STEP 7: Create default variants for products that don't have any
-- ============================================================================

INSERT INTO "ProductVariants" (
    "Id",
    "TenantId",
    "ProductId",
    "SKU",
    "Barcode",
    "Price",
    "OnlinePrice",
    "CostPrice",
    "IsDefault",
    "IsActive",
    "SortOrder",
    "CreatedAt",
    "IsDeleted"
)
SELECT 
    gen_random_uuid() AS "Id",
    p."TenantId",
    p."Id" AS "ProductId",
    COALESCE(p."SKU", p."Barcode") AS "SKU",
    p."Barcode",
    p."SalePrice" AS "Price",
    p."OnlinePrice",
    p."PurchasePrice" AS "CostPrice",
    true AS "IsDefault",
    p."IsActive",
    0 AS "SortOrder",
    CURRENT_TIMESTAMP AS "CreatedAt",
    p."IsDeleted"
FROM "Products" p
WHERE NOT EXISTS (
    SELECT 1 FROM "ProductVariants" pv WHERE pv."ProductId" = p."Id"
);

-- ============================================================================
-- STEP 8: Migrate WarehouseStocks to ProductVariantStocks
-- ============================================================================

INSERT INTO "ProductVariantStocks" (
    "Id",
    "TenantId",
    "VariantId",
    "WarehouseId",
    "Quantity",
    "ReservedQuantity",
    "CreatedAt",
    "IsDeleted"
)
SELECT 
    gen_random_uuid() AS "Id",
    ws."TenantId",
    pv."Id" AS "VariantId",
    ws."WarehouseId",
    ws."Quantity",
    ws."ReservedQuantity",
    CURRENT_TIMESTAMP AS "CreatedAt",
    ws."IsDeleted"
FROM "WarehouseStocks" ws
INNER JOIN "ProductVariants" pv ON pv."ProductId" = ws."ProductId" AND pv."IsDefault" = true
WHERE NOT EXISTS (
    SELECT 1 FROM "ProductVariantStocks" pvs 
    WHERE pvs."VariantId" = pv."Id" AND pvs."WarehouseId" = ws."WarehouseId"
);

-- ============================================================================
-- STEP 9: Update EF Core migrations history (mark this migration as applied)
-- ============================================================================

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260112204103_AddProductVariantsPhase11_1', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

-- ============================================================================
-- VALIDATION QUERIES (Run these to verify migration success)
-- ============================================================================

-- Counts should match:
SELECT 'Products' as "Table", COUNT(*) as "Count" FROM "Products"
UNION ALL
SELECT 'Default Variants', COUNT(*) FROM "ProductVariants" WHERE "IsDefault" = true;

-- Should return 0 products without variants:
SELECT COUNT(*) as "Products_Without_Variants"
FROM "Products" p 
WHERE NOT EXISTS (SELECT 1 FROM "ProductVariants" pv WHERE pv."ProductId" = p."Id");

-- Show variant counts per product:
SELECT "ProductId", COUNT(*) as "VariantCount" 
FROM "ProductVariants" 
GROUP BY "ProductId" 
HAVING COUNT(*) = 0
LIMIT 10;

-- ============================================================================
-- SUCCESS!
-- Phase 11.1 Product Variants migration complete.
-- ============================================================================
