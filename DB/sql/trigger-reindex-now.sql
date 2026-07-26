-- AF-16F: Trigger Full Reindex
-- Execute this to reindex all affiliate products with correct StorefrontBaseUrl

BEGIN;

UPDATE "Products" p
SET "UpdatedAt" = NOW()
FROM "AffiliateProductPolicies" app
WHERE app."ProductId" = p."Id" 
  AND app."IsEnabled" = true 
  AND app."IsDeleted" = false
  AND p."IsDeleted" = false
  AND p."IsActive" = true;

COMMIT;

-- Verify
SELECT COUNT(*) as "ProductsTriggered"
FROM "Products" p
INNER JOIN "AffiliateProductPolicies" app ON app."ProductId" = p."Id"
WHERE app."IsEnabled" = true 
  AND app."IsDeleted" = false
  AND p."IsDeleted" = false
  AND p."IsActive" = true;
