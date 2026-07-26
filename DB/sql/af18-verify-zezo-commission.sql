-- AF-18: Verify ZEZO's commission was recorded

-- 1. Check the order that just completed
SELECT 
    o."Id",
    o."OrderNumber",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."Status",
    o."Total",
    o."TenantId",
    t."Name" as tenant_name
FROM "Orders" o
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."Id" = 'd8f9bd19-2896-47e1-811a-7cadaa7453b6';

-- 2. Check the commission that was recorded
SELECT 
    c."Id",
    c."OrderId",
    c."AffiliateAccountId",
    c."CommissionAmount",
    c."Status",
    c."CreatedAt",
    o."OrderNumber",
    a."ReferralCode",
    a."DisplayName"
FROM "AffiliateCommissions" c
LEFT JOIN "Orders" o ON c."OrderId" = o."Id"
LEFT JOIN "AffiliateAccounts" a ON c."AffiliateAccountId" = a."Id"
WHERE c."OrderId" = 'd8f9bd19-2896-47e1-811a-7cadaa7453b6';

-- 3. Check the commission ledger entry
SELECT 
    l."Id",
    l."OrderId",
    l."AffiliateAccountId",
    l."CommissionAmount",
    l."BaseAmount",
    l."CommissionRate",
    l."EntryType",
    l."RecordedAtUtc",
    a."ReferralCode"
FROM "AffiliateCommissionLedger" l
LEFT JOIN "AffiliateAccounts" a ON l."AffiliateAccountId" = a."Id"
WHERE l."OrderId" = 'd8f9bd19-2896-47e1-811a-7cadaa7453b6'
ORDER BY l."RecordedAtUtc" DESC;

-- 4. Check the accounting journal entry
SELECT 
    j."Id",
    j."JournalNumber",
    j."Description",
    j."TotalDebit",
    j."TotalCredit",
    j."PostedAtUtc",
    j."IdempotencyKey"
FROM "FinancialJournals" j
WHERE j."JournalNumber" = 'JE-2026-000001';

-- 5. Check journal entries (debits and credits)
SELECT 
    e."Id",
    e."JournalId",
    e."AccountCode",
    e."AccountName",
    e."DebitAmount",
    e."CreditAmount",
    e."Description"
FROM "FinancialJournalEntries" e
INNER JOIN "FinancialJournals" j ON e."JournalId" = j."Id"
WHERE j."JournalNumber" = 'JE-2026-000001'
ORDER BY e."EntryNumber";

-- 6. Verify ZEZO's total commissions
SELECT 
    a."Id",
    a."ReferralCode",
    a."DisplayName",
    COUNT(c."Id") as total_commissions,
    SUM(c."CommissionAmount") as total_earned,
    SUM(CASE WHEN c."Status" = 'Earned' THEN c."CommissionAmount" ELSE 0 END) as earned_amount,
    SUM(CASE WHEN c."Status" = 'Approved' THEN c."CommissionAmount" ELSE 0 END) as approved_amount,
    SUM(CASE WHEN c."Status" = 'Paid' THEN c."CommissionAmount" ELSE 0 END) as paid_amount
FROM "AffiliateAccounts" a
LEFT JOIN "AffiliateCommissions" c ON a."Id" = c."AffiliateAccountId"
WHERE a."ReferralCode" = 'ZEZO906409'
GROUP BY a."Id", a."ReferralCode", a."DisplayName";

-- 7. Check all ZEZO's orders with commissions
SELECT 
    o."OrderNumber",
    o."Status" as order_status,
    o."Total" as order_total,
    o."CreatedAt" as order_date,
    c."CommissionAmount",
    c."Status" as commission_status,
    c."CreatedAt" as commission_date,
    t."Name" as tenant_name
FROM "Orders" o
INNER JOIN "AffiliateCommissions" c ON o."Id" = c."OrderId"
LEFT JOIN "Tenants" t ON o."TenantId" = t."Id"
WHERE o."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY o."CreatedAt" DESC;
