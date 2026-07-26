-- AF-18: Final debugging - exact middleware query
-- This is EXACTLY what the middleware should be running

-- V1 Fallback Query (what middleware runs)
SELECT 
    a."Id",
    a."ReferralCode",
    a."TenantId",
    a."Status",
    a."IsDeleted",
    a."CreatedAt",
    a."UpdatedAt"
FROM "AffiliateAccounts" a
WHERE a."ReferralCode" = 'ZEZO906409'
  AND a."Status" = 1;  -- AffiliateStatus.Active = 1

-- Check if there are ANY active affiliates (to verify query works)
SELECT COUNT(*) as active_count
FROM "AffiliateAccounts"
WHERE "Status" = 1;

-- Check all ZEZO records (case variations)
SELECT 
    "Id",
    "ReferralCode",
    "Status",
    "IsDeleted",
    "TenantId"
FROM "AffiliateAccounts"
WHERE "ReferralCode" ILIKE '%ZEZO%';

-- Verify the exact referral code (check for whitespace/special chars)
SELECT 
    "Id",
    "ReferralCode",
    LENGTH("ReferralCode") as code_length,
    "Status",
    "IsDeleted",
    "TenantId",
    -- Show hex representation to detect hidden characters
    encode("ReferralCode"::bytea, 'hex') as hex_code
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'ZEZO906409';
