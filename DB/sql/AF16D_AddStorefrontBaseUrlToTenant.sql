-- AF-16D: Add StorefrontBaseUrl column to Tenants table
-- Run this in PgAdmin4 against your PosEcommerceDb database
-- This adds the authoritative storefront base URL for each tenant,
-- used to construct promotion links instead of frontend inference.

ALTER TABLE "Tenants"
ADD COLUMN IF NOT EXISTS "StorefrontBaseUrl" text NULL;

-- Record migration in EF Core history so future migrations don't conflict
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260224214800_AF16D_AddStorefrontBaseUrlToTenant', '9.0.1')
ON CONFLICT DO NOTHING;

-- Optional: Set StorefrontBaseUrl for existing tenants based on their slug
-- Uncomment and adjust the template to match your environment:
-- UPDATE "Tenants"
-- SET "StorefrontBaseUrl" = 'http://' || "Slug" || '.localhost:5174'
-- WHERE "Slug" IS NOT NULL AND "StorefrontBaseUrl" IS NULL;
