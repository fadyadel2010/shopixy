-- =============================================================================
-- PHASE 11.5: SHOPIFY-GRADE PRODUCT AUTHORING
-- Migration: Add HasVariants column to Products table
-- Run this in pgAdmin4
-- =============================================================================

-- Step 1: Add HasVariants column to Products table
ALTER TABLE "Products" 
ADD COLUMN IF NOT EXISTS "HasVariants" boolean NOT NULL DEFAULT false;

-- Step 2: Add SKU-level financial tracking fields to OrderItems
ALTER TABLE "OrderItems"
ADD COLUMN IF NOT EXISTS "PaidAmount" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "OrderItems"
ADD COLUMN IF NOT EXISTS "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0;

-- Step 3: Set HasVariants = true for products that have more than 1 variant
UPDATE "Products" p
SET "HasVariants" = true
WHERE (
    SELECT COUNT(*) 
    FROM "ProductVariants" v 
    WHERE v."ProductId" = p."Id"
) > 1;

-- Step 4: Insert migration history record for EF Core tracking
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260113204319_AddHasVariantsToProduct', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Verification query (run this to check results)
SELECT 
    p."Id",
    p."Name",
    p."HasVariants",
    (SELECT COUNT(*) FROM "ProductVariants" v WHERE v."ProductId" = p."Id") as "VariantCount"
FROM "Products" p
ORDER BY "VariantCount" DESC
LIMIT 20;
