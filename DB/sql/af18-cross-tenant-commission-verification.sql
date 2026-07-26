-- AF-18: Cross-Tenant Commission Verification
-- Verifies that ZEZO (Mena affiliate) can earn commissions from Anatico orders

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 1: Verify ZEZO's affiliate account exists in Mena tenant
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    a."Id" as affiliate_id,
    a."TenantId" as affiliate_tenant,
    a."ReferralCode",
    a."DisplayName",
    a."Status",
    t."Name" as tenant_name
FROM "AffiliateAccounts" a
LEFT JOIN "Tenants" t ON a."TenantId" = t."Id"
WHERE a."ReferralCode" = 'ZEZO906409'
  AND NOT a."IsDeleted";

-- Expected: One row with TenantId = Mena (8a9f672b-43d5-4a2c-9762-5df63410fb73)

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 2: Find cross-tenant orders (Anatico orders with ZEZO attribution)
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    o."Id" as order_id,
    o."OrderNumber",
    o."TenantId" as order_tenant,
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."Status",
    o."Total",
    o."Currency",
    a."TenantId" as affiliate_tenant,
    a."DisplayName" as affiliate_name,
    t1."Name" as order_tenant_name,
    t2."Name" as affiliate_tenant_name,
    CASE 
        WHEN o."TenantId" != a."TenantId" THEN 'CROSS-TENANT'
        ELSE 'SAME-TENANT'
    END as sale_type
FROM "Orders" o
INNER JOIN "AffiliateAccounts" a ON o."AffiliateAccountId" = a."Id"
LEFT JOIN "Tenants" t1 ON o."TenantId" = t1."Id"
LEFT JOIN "Tenants" t2 ON a."TenantId" = t2."Id"
WHERE o."AffiliateReferralCode" = 'ZEZO906409'
  AND NOT o."IsDeleted"
ORDER BY o."CreatedAt" DESC;

-- Expected: Orders from Anatico tenant (fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0)
-- with ZEZO's affiliate ID (from Mena tenant)

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 3: Check commission ledger entries
-- AF-18 FIX: Commission ledger should be in AFFILIATE's tenant (Mena), not order's tenant (Anatico)
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    acl."Id" as ledger_id,
    acl."TenantId" as ledger_tenant,
    acl."AffiliateAccountId",
    acl."OrderId",
    acl."OrderItemId",
    acl."EntryType",
    acl."BaseAmount",
    acl."CommissionRate",
    acl."CommissionAmount",
    acl."Currency",
    acl."RecordedAtUtc",
    o."OrderNumber",
    o."TenantId" as order_tenant,
    a."TenantId" as affiliate_tenant,
    t1."Name" as ledger_tenant_name,
    t2."Name" as order_tenant_name,
    t3."Name" as affiliate_tenant_name,
    CASE 
        WHEN acl."TenantId" = a."TenantId" THEN '✓ CORRECT (Affiliate Tenant)'
        WHEN acl."TenantId" = o."TenantId" THEN '✗ WRONG (Order Tenant)'
        ELSE '? UNKNOWN'
    END as ledger_location
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
INNER JOIN "AffiliateAccounts" a ON acl."AffiliateAccountId" = a."Id"
LEFT JOIN "Tenants" t1 ON acl."TenantId" = t1."Id"
LEFT JOIN "Tenants" t2 ON o."TenantId" = t2."Id"
LEFT JOIN "Tenants" t3 ON a."TenantId" = t3."Id"
WHERE a."ReferralCode" = 'ZEZO906409'
ORDER BY acl."RecordedAtUtc" DESC;

-- Expected after fix:
-- - ledger_tenant = Mena (8a9f672b-43d5-4a2c-9762-5df63410fb73)
-- - order_tenant = Anatico (fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0)
-- - affiliate_tenant = Mena (8a9f672b-43d5-4a2c-9762-5df63410fb73)
-- - ledger_location = '✓ CORRECT (Affiliate Tenant)'

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 4: Check accounting journal entries
-- AF-18: Journal entries should be in AFFILIATE's tenant (Mena) for commission revenue
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    je."Id" as journal_id,
    je."JournalNumber",
    je."TenantId" as journal_tenant,
    je."Description",
    je."TotalDebit",
    je."TotalCredit",
    je."PostedAtUtc",
    t."Name" as journal_tenant_name,
    CASE 
        WHEN je."TenantId" = a."TenantId" THEN '✓ CORRECT (Affiliate Tenant)'
        WHEN je."TenantId" = o."TenantId" THEN '✗ WRONG (Order Tenant)'
        ELSE '? UNKNOWN'
    END as journal_location
FROM "JournalEntries" je
INNER JOIN "AffiliateCommissionLedger" acl ON je."IdempotencyKey" LIKE '%' || acl."Id"::text || '%'
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
INNER JOIN "AffiliateAccounts" a ON acl."AffiliateAccountId" = a."Id"
LEFT JOIN "Tenants" t ON je."TenantId" = t."Id"
WHERE a."ReferralCode" = 'ZEZO906409'
  AND NOT je."IsDeleted"
ORDER BY je."PostedAtUtc" DESC;

-- Expected after fix:
-- - journal_tenant = Mena (affiliate's home tenant)
-- - journal_location = '✓ CORRECT (Affiliate Tenant)'

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 5: Summary - Cross-Tenant Commission Flow
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    'Cross-Tenant Commission Summary' as report_type,
    COUNT(DISTINCT o."Id") as total_orders,
    COUNT(DISTINCT acl."Id") as total_commission_entries,
    SUM(acl."CommissionAmount") as total_commission_earned,
    o."Currency",
    COUNT(DISTINCT CASE WHEN acl."TenantId" = a."TenantId" THEN acl."Id" END) as correct_ledger_entries,
    COUNT(DISTINCT CASE WHEN acl."TenantId" != a."TenantId" THEN acl."Id" END) as wrong_ledger_entries
FROM "Orders" o
INNER JOIN "AffiliateAccounts" a ON o."AffiliateAccountId" = a."Id"
LEFT JOIN "AffiliateCommissionLedger" acl ON acl."OrderId" = o."Id"
WHERE a."ReferralCode" = 'ZEZO906409'
  AND NOT o."IsDeleted"
GROUP BY o."Currency";

-- Expected:
-- - correct_ledger_entries > 0 (commissions in affiliate's tenant)
-- - wrong_ledger_entries = 0 (no commissions in order's tenant)

-- ═══════════════════════════════════════════════════════════════════════════
-- DIAGNOSTIC: Find orders WITHOUT commission (bug detection)
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 
    o."Id" as order_id,
    o."OrderNumber",
    o."Status",
    o."Total",
    o."Currency",
    o."TenantId" as order_tenant,
    a."TenantId" as affiliate_tenant,
    t1."Name" as order_tenant_name,
    t2."Name" as affiliate_tenant_name,
    'MISSING COMMISSION' as issue
FROM "Orders" o
INNER JOIN "AffiliateAccounts" a ON o."AffiliateAccountId" = a."Id"
LEFT JOIN "AffiliateCommissionLedger" acl ON acl."OrderId" = o."Id"
LEFT JOIN "Tenants" t1 ON o."TenantId" = t1."Id"
LEFT JOIN "Tenants" t2 ON a."TenantId" = t2."Id"
WHERE a."ReferralCode" = 'ZEZO906409'
  AND o."Status" IN ('Delivered', 'Completed')
  AND acl."Id" IS NULL
  AND NOT o."IsDeleted";

-- Expected after fix: No rows (all delivered/completed orders should have commissions)
