-- AF-19: Debug order attribution for ORD-FC1B-2026-00018
-- Check if order has affiliate data and if attribution was created

-- 1. Check the order itself
SELECT 
    "Id",
    "OrderNumber",
    "TenantId",
    "AffiliateAccountId",
    "AffiliateReferralCode",
    "PromotionLinkId",
    "Status",
    "Total",
    "Currency",
    "CreatedAt"
FROM "Orders"
WHERE "OrderNumber" = 'ORD-FC1B-2026-00018';

-- 2. Check if attribution was created
SELECT 
    "Id",
    "OrderId",
    "AffiliateAccountId",
    "ReferralCode",
    "Source",
    "AttributedAtUtc",
    "PromotionLinkId"
FROM "AffiliateAttributions"
WHERE "OrderId" IN (
    SELECT "Id" FROM "Orders" WHERE "OrderNumber" = 'ORD-FC1B-2026-00018'
);

-- 3. Check commission ledger entries
SELECT 
    "Id",
    "OrderId",
    "OrderItemId",
    "AffiliateAccountId",
    "StoreTenantId",
    "TenantId",
    "CommissionAmount",
    "Status",
    "CreatedAtUtc"
FROM "AffiliateCommissionLedger"
WHERE "OrderId" IN (
    SELECT "Id" FROM "Orders" WHERE "OrderNumber" = 'ORD-FC1B-2026-00018'
);

-- 4. Check the affiliate account (TEST722103)
SELECT 
    "Id",
    "ReferralCode",
    "TenantId",
    "Status",
    "IsDeleted",
    "DisplayName"
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'TEST722103';
