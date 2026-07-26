-- ============================================
-- Add OnlinePrice column to Products table
-- Created: 2026-01-05
-- Purpose: Add dedicated storefront pricing field
-- ============================================

-- Add the OnlinePrice column (nullable decimal)
-- This is the price displayed on the storefront
-- If NULL or 0, the storefront will fall back to SalePrice
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "OnlinePrice" NUMERIC NULL;

-- Add a comment to document the column's purpose
COMMENT ON COLUMN "Products"."OnlinePrice" IS 'Online/Storefront price for e-commerce display. If NULL or 0, falls back to SalePrice. This is the source of truth for storefront pricing.';

-- Track migration in EF Core history (optional - only if you want EF migrations to recognize this)
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260105201157_AddOnlinePriceToProduct', '9.0.0')
-- ON CONFLICT ("MigrationId") DO NOTHING;
