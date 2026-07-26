-- ═══════════════════════════════════════════════════════════════════════════
-- AF-18: TENANT MISMATCH DIAGNOSTIC
-- 
-- ISSUE: ZEZO affiliate belongs to one tenant but order placed in another
-- ═══════════════════════════════════════════════════════════════════════════

-- Find ZEZO affiliate account and its tenant
SELECT 
    "Id" AS AffiliateAccountId,
    "TenantId",
    "ReferralCode",
    "DisplayName",
    "Status"
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'ZEZO906409'
  AND "IsDeleted" = false;

-- Find the tenant name for ZEZO's account
SELECT 
    t."Id" AS TenantId,
    t."Name" AS TenantName,
    t."Slug" AS TenantSlug
FROM "Tenants" t
WHERE t."Id" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- Find the tenant where the order was placed
SELECT 
    t."Id" AS TenantId,
    t."Name" AS TenantName,
    t."Slug" AS TenantSlug
FROM "Tenants" t
WHERE t."Id" = 'fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0';

-- Find the most recent order
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."TenantId",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    t."Name" AS TenantName,
    t."Slug" AS TenantSlug
FROM "Orders" o
INNER JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."OrderNumber" = 'ORD-FC1B-2026-00012'
  AND o."IsDeleted" = false;
