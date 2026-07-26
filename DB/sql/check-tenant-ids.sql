-- Check tenant IDs for stores
SELECT 
    "Id" as tenant_id,
    "Name" as store_name,
    "Slug" as store_slug
FROM "Tenants"
WHERE "Slug" IN ('mena', 'anatico')
ORDER BY "Name";
