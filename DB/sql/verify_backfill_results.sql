-- ═══════════════════════════════════════════════════════════════════════════════
-- AF-19C: VERIFY BACKFILL RESULTS
-- Run these queries to confirm the backfill was successful
-- ═══════════════════════════════════════════════════════════════════════════════

-- 1. Count entries by StoreTenantId
SELECT 
    "StoreTenantId",
    COUNT(*) as entry_count,
    SUM("CommissionAmount") as total_commission,
    MIN("RecordedAtUtc") as earliest_entry,
    MAX("RecordedAtUtc") as latest_entry
FROM "AffiliateCommissionLedger"
GROUP BY "StoreTenantId"
ORDER BY entry_count DESC;

-- 2. Verify NO entries have NULL or empty StoreTenantId
SELECT 
    COUNT(*) as entries_with_missing_store_tenant
FROM "AffiliateCommissionLedger"
WHERE "StoreTenantId" IS NULL 
   OR "StoreTenantId" = '00000000-0000-0000-0000-000000000000';
-- Should return 0

-- 3. Verify StoreTenantId matches Order TenantId (data integrity check)
SELECT 
    COUNT(*) as mismatches
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
WHERE acl."StoreTenantId" != o."TenantId";
-- Should return 0

-- 4. Show sample of backfilled entries
SELECT 
    acl."Id",
    acl."OrderId",
    acl."StoreTenantId",
    o."TenantId" as order_tenant_id,
    o."OrderNumber",
    acl."EntryType",
    acl."CommissionAmount",
    acl."Currency",
    acl."RecordedAtUtc"
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
ORDER BY acl."RecordedAtUtc" DESC
LIMIT 10;

-- 5. Count entries by EntryType and StoreTenantId
SELECT 
    "StoreTenantId",
    "EntryType",
    COUNT(*) as count,
    SUM("CommissionAmount") as total
FROM "AffiliateCommissionLedger"
GROUP BY "StoreTenantId", "EntryType"
ORDER BY "StoreTenantId", "EntryType";

-- 6. Verify trigger is still enabled (immutability protection)
SELECT 
    tgname as trigger_name,
    CASE tgenabled
        WHEN 'O' THEN 'ENABLED ✅'
        WHEN 'D' THEN 'DISABLED ❌'
        ELSE 'UNKNOWN'
    END as status
FROM pg_trigger
WHERE tgname = 'trg_affiliate_commission_ledger_immutable';
-- Should show 'ENABLED ✅'
