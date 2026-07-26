-- Debug: Check affiliate data in PostgreSQL
SELECT 
    aa."Id" as affiliate_id,
    aa."DisplayName",
    aa."ReferralCode",
    aa."TenantId" as affiliate_tenant_id,
    aa."Status"
FROM "AffiliateAccounts" aa
WHERE aa."ReferralCode" = 'HANY450693';

-- Check orders with this affiliate
SELECT 
    o."Id" as order_id,
    o."OrderNumber",
    o."TenantId" as order_tenant_id,
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    t."Name" as store_name,
    t."Slug" as store_slug
FROM "Orders" o
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."AffiliateReferralCode" = 'HANY450693'
ORDER BY o."CreatedAt" DESC;

-- Check commission ledger
SELECT 
    acl."Id",
    acl."AffiliateAccountId",
    acl."OrderId",
    acl."OrderItemId",
    acl."TenantId" as ledger_tenant_id,
    acl."StoreTenantId",
    acl."EntryType",
    acl."Amount",
    t1."Name" as ledger_store,
    t2."Name" as actual_store
FROM "AffiliateCommissionLedger" acl
LEFT JOIN "Tenants" t1 ON acl."TenantId" = t1."Id"
LEFT JOIN "Tenants" t2 ON acl."StoreTenantId" = t2."Id"
WHERE acl."AffiliateAccountId" = '019c912f-012f-7795-90ce-912701633a66'
ORDER BY acl."CreatedAt" DESC;
