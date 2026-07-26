-- ============================================================================
-- AF-19: Verify Current Migration State
-- Run this to check what's already applied
-- ============================================================================

-- Check 1: Does StoreTenantId column exist?
SELECT 
    'StoreTenantId Column' as check_name,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_name = 'AffiliateCommissionLedger' 
            AND column_name = 'StoreTenantId'
        ) THEN '✅ EXISTS'
        ELSE '❌ MISSING'
    END as status;

-- Check 2: Are there any NULL StoreTenantId values?
SELECT 
    'StoreTenantId NULL Count' as check_name,
    COUNT(*) as null_count,
    CASE 
        WHEN COUNT(*) = 0 THEN '✅ ALL FILLED'
        ELSE '❌ NEEDS BACKFILL'
    END as status
FROM "AffiliateCommissionLedger"
WHERE "StoreTenantId" IS NULL;

-- Check 3: Is StoreTenantId NOT NULL constraint applied?
SELECT 
    'StoreTenantId NOT NULL' as check_name,
    CASE 
        WHEN is_nullable = 'NO' THEN '✅ ENFORCED'
        ELSE '❌ NULLABLE'
    END as status
FROM information_schema.columns
WHERE table_name = 'AffiliateCommissionLedger' 
AND column_name = 'StoreTenantId';

-- Check 4: Does global ReferralCode unique constraint exist?
SELECT 
    'Global ReferralCode Unique' as check_name,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM pg_constraint 
            WHERE conname = 'UQ_AffiliateAccounts_ReferralCode'
        ) THEN '✅ EXISTS'
        ELSE '❌ MISSING'
    END as status;

-- Check 5: Does old tenant-scoped constraint still exist?
SELECT 
    'Old Tenant-Scoped Constraint' as check_name,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM pg_constraint 
            WHERE conname = 'UQ_AffiliateAccounts_Tenant_ReferralCode'
        ) THEN '❌ STILL EXISTS (should be dropped)'
        ELSE '✅ DROPPED'
    END as status;

-- Check 6: Are there any ReferralCode collisions?
SELECT 
    'ReferralCode Collisions' as check_name,
    COUNT(*) as collision_count,
    CASE 
        WHEN COUNT(*) = 0 THEN '✅ NO COLLISIONS'
        ELSE '❌ COLLISIONS EXIST'
    END as status
FROM (
    SELECT "ReferralCode", COUNT(*) as cnt
    FROM "AffiliateAccounts"
    WHERE NOT "IsDeleted"
    GROUP BY "ReferralCode"
    HAVING COUNT(*) > 1
) collisions;

-- Check 7: StoreTenantId index exists?
SELECT 
    'StoreTenantId Index' as check_name,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM pg_indexes 
            WHERE indexname = 'IX_AffiliateCommLedger_StoreTenantId'
        ) THEN '✅ EXISTS'
        ELSE '❌ MISSING'
    END as status;

-- Check 8: Composite index exists?
SELECT 
    'Affiliate-Store Composite Index' as check_name,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM pg_indexes 
            WHERE indexname = 'IX_AffiliateCommLedger_Affiliate_Store'
        ) THEN '✅ EXISTS'
        ELSE '❌ MISSING'
    END as status;

-- Summary: Show sample commission entries with StoreTenantId
SELECT 
    'Sample Commission Entries' as info,
    COUNT(*) as total_entries,
    COUNT("StoreTenantId") as with_store_tenant,
    COUNT(*) - COUNT("StoreTenantId") as missing_store_tenant
FROM "AffiliateCommissionLedger";
