-- Find ZEZO's tenant and storefront URL
SELECT 
    aa."Id" AS AffiliateAccountId,
    aa."ReferralCode",
    aa."DisplayName",
    aa."Status",
    t."Id" AS TenantId,
    t."Name" AS TenantName,
    t."Slug" AS TenantSlug,
    -- Construct the storefront URL
    CASE 
        WHEN t."Slug" IS NOT NULL THEN 'http://' || t."Slug" || '.localhost:5174?ref=ZEZO906409'
        ELSE 'No slug configured'
    END AS StorefrontTestUrl
FROM "AffiliateAccounts" aa
INNER JOIN "Tenants" t ON aa."TenantId" = t."Id"
WHERE aa."ReferralCode" = 'ZEZO906409'
  AND aa."IsDeleted" = false;
