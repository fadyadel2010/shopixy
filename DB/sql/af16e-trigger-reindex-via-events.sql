-- AF-16E: Trigger Reindex via Event Republishing
--
-- This script republishes ProductUpdatedEvent for all affiliate-enabled products,
-- which will trigger the projection consumer to reindex them with StorefrontBaseUrl.
--
-- SAFER ALTERNATIVE: Uses existing event infrastructure instead of direct updates.
--
-- USAGE:
--   Run this in your PostgreSQL database
--   The projection consumer will pick up events and reindex products

-- Step 1: Identify all affiliate-enabled products
SELECT 
    p."Id" as "ProductId",
    p."TenantId",
    p."Name",
    p."Slug",
    t."Name" as "TenantName",
    t."Slug" as "TenantSlug",
    t."StorefrontBaseUrl"
FROM "Products" p
INNER JOIN "AffiliateProductPolicies" app ON app."ProductId" = p."Id" AND app."IsEnabled" = true AND app."IsDeleted" = false
INNER JOIN "Tenants" t ON t."Id" = p."TenantId"
WHERE p."IsDeleted" = false
  AND p."IsActive" = true
ORDER BY t."Name", p."Name";

-- Step 2: Count products to be reindexed
SELECT 
    COUNT(*) as "TotalAffiliateProducts",
    COUNT(DISTINCT p."TenantId") as "TotalTenants"
FROM "Products" p
INNER JOIN "AffiliateProductPolicies" app ON app."ProductId" = p."Id" AND app."IsEnabled" = true AND app."IsDeleted" = false
WHERE p."IsDeleted" = false
  AND p."IsActive" = true;

-- Step 3: Trigger reindex by updating UpdatedAt timestamp
-- This will cause the projection consumer to reprocess these products
-- UNCOMMENT THE FOLLOWING LINES TO EXECUTE:

-- BEGIN;
-- 
-- UPDATE "Products" p
-- SET "UpdatedAt" = NOW()
-- FROM "AffiliateProductPolicies" app
-- WHERE app."ProductId" = p."Id" 
--   AND app."IsEnabled" = true 
--   AND app."IsDeleted" = false
--   AND p."IsDeleted" = false
--   AND p."IsActive" = true;
-- 
-- COMMIT;

-- Alternative: Use MassTransit outbox to publish events
-- This is the cleanest approach but requires MassTransit outbox to be configured

-- Step 4: Verify reindex by checking MongoDB
-- Run this in MongoDB shell:
-- db.public_affiliate_products.find({ storefrontBaseUrl: { $exists: true, $ne: "" } }).count()
-- db.public_affiliate_products.find({ storefrontBaseUrl: { $exists: false } }).count()
-- db.public_affiliate_products.find({ storefrontBaseUrl: "" }).count()
