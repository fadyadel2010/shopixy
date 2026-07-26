-- AF-17A: Trigger Full Reindex with Storefront Truth Alignment
-- This will reindex all affiliate products with correct inventory and pricing

BEGIN;

-- Trigger reindex for all affiliate-enabled products
UPDATE "Products" p
SET "UpdatedAt" = NOW()
FROM "AffiliateProductPolicies" app
WHERE app."ProductId" = p."Id" 
  AND app."IsEnabled" = true 
  AND app."IsDeleted" = false
  AND p."IsDeleted" = false
  AND p."IsActive" = true;

COMMIT;

-- Verify count
SELECT COUNT(*) as "ProductsToReindex"
FROM "Products" p
INNER JOIN "AffiliateProductPolicies" app ON app."ProductId" = p."Id"
WHERE app."IsEnabled" = true 
  AND app."IsDeleted" = false
  AND p."IsDeleted" = false
  AND p."IsActive" = true;
