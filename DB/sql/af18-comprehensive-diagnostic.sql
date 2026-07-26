-- AF-18: Comprehensive Diagnostic Queries
-- Run these to verify ZEZO account state and troubleshoot issues

-- ============================================================================
-- 1. VERIFY ZEZO ACCOUNT EXISTS AND IS ACTIVE
-- ============================================================================
SELECT 
    "Id",
    "ReferralCode",
    "Status",
    "IsDeleted",
    "TenantId",
    "DisplayName",
    "CreatedAt",
    "ActivatedAtUtc"
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'ZEZO906409';

-- Expected Result:
-- Id: 019c8fb7-0756-7ab3-9187-086089e86e06
-- Status: 1 (Active)
-- IsDeleted: false
-- TenantId: 8a9f672b-43d5-4a2c-9762-5df63410fb73 (Mena)

-- ============================================================================
-- 2. CHECK IF ZEZO HAS ANY PROMOTION LINKS (V2 Flow)
-- ============================================================================
SELECT 
    "Id",
    "ReferralCode",
    "AffiliateAccountId",
    "AffiliateUserId",
    "ProductId",
    "IsActive",
    "IsDeleted",
    "TenantId"
FROM "AffiliatePromotionLinks"
WHERE "ReferralCode" = 'ZEZO906409';

-- Expected Result: Empty (ZEZO doesn't have promotion links yet)

-- ============================================================================
-- 3. VERIFY MIDDLEWARE V1 QUERY (Exact query from code)
-- ============================================================================
SELECT 
    a."Id",
    a."ReferralCode",
    a."TenantId",
    a."Status",
    a."IsDeleted"
FROM "AffiliateAccounts" a
WHERE a."ReferralCode" = 'ZEZO906409'
  AND a."Status" = 1;  -- AffiliateStatus.Active = 1

-- Expected Result: Should return ZEZO's account

-- ============================================================================
-- 4. CHECK ALL ACTIVE AFFILIATES (Verify query works in general)
-- ============================================================================
SELECT 
    "Id",
    "ReferralCode",
    "DisplayName",
    "Status",
    "TenantId"
FROM "AffiliateAccounts"
WHERE "Status" = 1
  AND "IsDeleted" = false
ORDER BY "CreatedAt" DESC
LIMIT 10;

-- Expected Result: Should show multiple active affiliates including ZEZO

-- ============================================================================
-- 5. CHECK FOR CASE/WHITESPACE ISSUES
-- ============================================================================
SELECT 
    "Id",
    "ReferralCode",
    LENGTH("ReferralCode") as code_length,
    "Status",
    "IsDeleted",
    -- Show hex to detect hidden characters
    encode("ReferralCode"::bytea, 'hex') as hex_code
FROM "AffiliateAccounts"
WHERE UPPER(TRIM("ReferralCode")) = 'ZEZO906409';

-- Expected Result: 
-- code_length: 10
-- hex_code: 5A455A4F393036343039 (ASCII for ZEZO906409)

-- ============================================================================
-- 6. CHECK RECENT ORDERS WITH AFFILIATE ATTRIBUTION
-- ============================================================================
SELECT 
    o."Id",
    o."OrderNumber",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."TenantId",
    o."Status",
    o."CreatedAt",
    t."Name" as tenant_name
FROM "Orders" o
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."AffiliateAccountId" IS NOT NULL
ORDER BY o."CreatedAt" DESC
LIMIT 10;

-- Expected Result: Should show orders with affiliate attribution

-- ============================================================================
-- 7. CHECK IF ZEZO HAS ANY ORDERS
-- ============================================================================
SELECT 
    o."Id",
    o."OrderNumber",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."TenantId",
    o."Status",
    o."Total",
    o."CreatedAt",
    t."Name" as tenant_name,
    t."Slug" as tenant_slug
FROM "Orders" o
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
   OR o."AffiliateReferralCode" = 'ZEZO906409'
ORDER BY o."CreatedAt" DESC;

-- Expected Result: Should show ZEZO's orders (if any)

-- ============================================================================
-- 8. CHECK IF ZEZO HAS ANY COMMISSIONS
-- ============================================================================
SELECT 
    c."Id",
    c."OrderId",
    c."AffiliateAccountId",
    c."CommissionAmount",
    c."Status",
    c."CreatedAt",
    o."OrderNumber",
    o."Status" as order_status
FROM "AffiliateCommissions" c
LEFT JOIN "Orders" o ON c."OrderId" = o."Id"
WHERE c."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY c."CreatedAt" DESC;

-- Expected Result: Should show ZEZO's commissions (if any)

-- ============================================================================
-- 9. CHECK AFFILIATE LINK CLICKS (Analytics)
-- ============================================================================
SELECT 
    "Id",
    "AffiliateAccountId",
    "ReferralCode",
    "SessionId",
    "ProductId",
    "OccurredAtUtc"
FROM "AffiliateLinkClicks"
WHERE "AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY "OccurredAtUtc" DESC
LIMIT 10;

-- Expected Result: Should show recent clicks on ZEZO's referral link

-- ============================================================================
-- 10. CROSS-TENANT VERIFICATION
-- ============================================================================
-- Check if ZEZO (Mena affiliate) has orders from other tenants
SELECT 
    o."Id",
    o."OrderNumber",
    o."AffiliateAccountId",
    o."TenantId" as order_tenant,
    a."TenantId" as affiliate_tenant,
    CASE 
        WHEN o."TenantId" = a."TenantId" THEN 'Same Tenant'
        ELSE 'Cross-Tenant'
    END as attribution_type,
    t."Name" as order_tenant_name,
    at."Name" as affiliate_tenant_name
FROM "Orders" o
INNER JOIN "AffiliateAccounts" a ON o."AffiliateAccountId" = a."Id"
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
LEFT JOIN "Tenants" at ON a."TenantId" = at."Id"
WHERE o."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY o."CreatedAt" DESC;

-- Expected Result: Shows if ZEZO has cross-tenant orders

-- ============================================================================
-- 11. CHECK TENANT DETAILS
-- ============================================================================
SELECT 
    "Id",
    "Name",
    "Slug",
    "IsActive"
FROM "Tenants"
WHERE "Id" IN (
    '8a9f672b-43d5-4a2c-9762-5df63410fb73',  -- Mena
    'fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0'   -- Anatico
);

-- Expected Result:
-- Mena: 8a9f672b-43d5-4a2c-9762-5df63410fb73
-- Anatico: fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0

-- ============================================================================
-- 12. FIND ORDERS WITHOUT AFFILIATE ATTRIBUTION (Potential backfill candidates)
-- ============================================================================
SELECT 
    o."Id",
    o."OrderNumber",
    o."TenantId",
    o."Status",
    o."Total",
    o."CreatedAt",
    t."Name" as tenant_name
FROM "Orders" o
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."AffiliateAccountId" IS NULL
  AND o."AffiliateReferralCode" IS NULL
  AND o."Status" IN ('Completed', 'Delivered')
  AND o."CreatedAt" >= NOW() - INTERVAL '7 days'
ORDER BY o."CreatedAt" DESC
LIMIT 20;

-- These orders might need backfill if they were placed via affiliate links
