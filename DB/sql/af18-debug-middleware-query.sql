-- Debug: Exact query the middleware is running
-- This should match what the middleware does

-- V2 flow (PromotionLink) - should return NULL since ZEZO doesn't have promotion links
SELECT 
    l."Id",
    l."AffiliateUserId",
    l."AffiliateAccountId",
    l."ProductId",
    l."ReferralCode",
    l."TenantId",
    l."IsActive",
    l."IsDeleted"
FROM "AffiliatePromotionLinks" l
WHERE l."ReferralCode" = 'ZEZO906409'
  AND l."IsActive" = true
  AND l."IsDeleted" = false;

-- V1 fallback (AffiliateAccount) - this SHOULD return ZEZO's account
SELECT 
    a."Id",
    a."ReferralCode",
    a."TenantId",
    a."Status",
    a."IsDeleted"
FROM "AffiliateAccounts" a
WHERE a."ReferralCode" = 'ZEZO906409'
  AND a."Status" = 1  -- Active
  AND a."IsDeleted" = false;

-- Check if there's any whitespace or case issues
SELECT 
    a."Id",
    a."ReferralCode",
    LENGTH(a."ReferralCode") as code_length,
    a."TenantId",
    a."Status",
    a."IsDeleted"
FROM "AffiliateAccounts" a
WHERE UPPER(TRIM(a."ReferralCode")) = 'ZEZO906409'
  AND a."IsDeleted" = false;
