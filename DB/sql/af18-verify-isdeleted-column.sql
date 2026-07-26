-- AF-18: Verify IsDeleted column exists and check ZEZO's value
-- The middleware query includes: AND !l.IsDeleted / AND !a.IsDeleted
-- If this column doesn't exist or has wrong value, query will fail

-- Check if IsDeleted column exists in AffiliateAccounts
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'AffiliateAccounts'
  AND column_name = 'IsDeleted';

-- Check ZEZO's IsDeleted value (if column exists)
SELECT 
    "Id",
    "ReferralCode",
    "Status",
    "IsDeleted",
    "TenantId"
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'ZEZO906409';

-- Try query WITHOUT IsDeleted filter (what middleware SHOULD be doing if column doesn't exist)
SELECT 
    a."Id",
    a."ReferralCode",
    a."TenantId",
    a."Status"
FROM "AffiliateAccounts" a
WHERE a."ReferralCode" = 'ZEZO906409'
  AND a."Status" = 1;  -- Active

-- Check if IsDeleted column exists in AffiliatePromotionLinks
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'AffiliatePromotionLinks'
  AND column_name = 'IsDeleted';
