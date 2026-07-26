# AF-19: Global Marketplace Affiliate Refactor - Rollout Checklist

## ✅ PRE-DEPLOYMENT VERIFICATION

### Database Migration Safety
- [ ] Run collision detection query to verify no duplicate ReferralCodes exist:
```sql
SELECT "ReferralCode", COUNT(*) as cnt
FROM "AffiliateAccounts"
WHERE NOT "IsDeleted"
GROUP BY "ReferralCode"
HAVING COUNT(*) > 1;
```
Expected: 0 rows (no collisions)

- [ ] Verify all commission entries have valid OrderId references:
```sql
SELECT COUNT(*) FROM "AffiliateCommissionLedger" acl
LEFT JOIN "Orders" o ON acl."OrderId" = o."Id"
WHERE o."Id" IS NULL;
```
Expected: 0 rows

- [ ] Backup database before migration
- [ ] Test migration on staging environment first

### Code Compilation
- [ ] Build solution: `dotnet build`
- [ ] Run unit tests: `dotnet test`
- [ ] Check for compilation errors in:
  - `AffiliateCommissionLedger.cs`
  - `AffiliateAccount.cs`
  - `AffiliateService.cs`
  - `AffiliateReferralMiddleware.cs`
  - `AffiliateAuthController.cs`

## 🚀 DEPLOYMENT SEQUENCE

### Step 1: Database Migration (Zero Downtime)
```bash
psql -U postgres -d pointofsale -f PointOfSale.Infrastructure/Data/Migrations/af19_global_marketplace_affiliate.sql
```

**Expected Output:**
- ✅ StoreTenantId column added
- ✅ Backfill completed (all rows have StoreTenantId)
- ✅ Global ReferralCode uniqueness enforced
- ✅ No collisions detected
- ✅ Verification queries pass

**Rollback if:**
- Collision count > 0
- Backfill fails (NULL StoreTenantId rows remain)
- Unique constraint creation fails

### Step 2: Deploy Application Code
```bash
dotnet publish -c Release
# Deploy to production
```

### Step 3: Restart Application
```bash
systemctl restart pointofsale-api
```

## 🧪 POST-DEPLOYMENT TESTING

### Test 1: Global Affiliate Registration
```bash
curl -X POST https://api.example.com/api/affiliate/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "Test123!@#",
    "displayName": "Test Affiliate"
  }'
```
**Expected:** 200 OK, JWT returned, no storeSlug required

### Test 2: Cross-Store Promotion
**Scenario:** Affiliate from Store A promotes product from Store B

1. Register affiliate on Store A: `http://storea.localhost/affiliate/register`
2. Get referral code: `TESTA123456`
3. Visit Store B with ref code: `http://storeb.localhost/products/iphone?ref=TESTA123456`
4. Complete purchase on Store B
5. Verify commission recorded with:
   - `StoreTenantId` = Store B's TenantId
   - `TenantId` = Store A's TenantId (legacy)

**Verification Query:**
```sql
SELECT 
    acl."Id",
    acl."TenantId" as "AffiliateTenant",
    acl."StoreTenantId" as "StoreTenant",
    acl."CommissionAmount",
    o."OrderNumber",
    o."TenantId" as "OrderTenant"
FROM "AffiliateCommissionLedger" acl
JOIN "Orders" o ON acl."OrderId" = o."Id"
WHERE acl."AffiliateAccountId" = '<affiliate-id>'
ORDER BY acl."CreatedAt" DESC
LIMIT 5;
```

**Expected:**
- `StoreTenant` = Store B's TenantId
- `OrderTenant` = Store B's TenantId
- `AffiliateTenant` = Store A's TenantId (or Guid.Empty for new global affiliates)

### Test 3: Dashboard Aggregation
```bash
curl -X GET https://api.example.com/api/affiliate/dashboard \
  -H "Authorization: Bearer <affiliate-jwt>"
```
**Expected:** Dashboard shows earnings from ALL stores

### Test 4: Referral Code Uniqueness
```bash
# Try to register two affiliates with same referral code
curl -X POST https://api.example.com/api/affiliate/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test1@example.com",
    "password": "Test123!@#",
    "displayName": "TESTCODE"
  }'

curl -X POST https://api.example.com/api/affiliate/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test2@example.com",
    "password": "Test123!@#",
    "displayName": "TESTCODE"
  }'
```
**Expected:** Second registration gets different code (collision handled)

### Test 5: Existing Affiliates Continue Working
```bash
# Use existing affiliate referral code
curl -X GET https://storea.localhost/products/iphone?ref=<existing-code>
```
**Expected:** 
- Affiliate Mode activates
- Commission records correctly
- No errors in logs

## 📊 MONITORING

### Key Metrics to Watch
- [ ] Affiliate registration rate (should remain stable)
- [ ] Commission recording success rate (should be 100%)
- [ ] Cross-store promotion usage (new metric)
- [ ] Referral code collision rate (should be near 0)

### Log Patterns to Monitor
```bash
# Success patterns
grep "AF-19:GlobalRegistration" /var/log/pointofsale.log
grep "AF-19:CrossStore" /var/log/pointofsale.log
grep "AF-19:GlobalLookup" /var/log/pointofsale.log

# Error patterns (should be 0)
grep "AF-19.*ERROR" /var/log/pointofsale.log
grep "ReferralCode.*collision" /var/log/pointofsale.log
grep "StoreTenantId.*NULL" /var/log/pointofsale.log
```

### Database Health Checks
```sql
-- Verify no NULL StoreTenantId
SELECT COUNT(*) FROM "AffiliateCommissionLedger" WHERE "StoreTenantId" IS NULL;
-- Expected: 0

-- Verify global ReferralCode uniqueness
SELECT COUNT(DISTINCT "ReferralCode") as unique_codes, COUNT(*) as total_accounts
FROM "AffiliateAccounts" WHERE NOT "IsDeleted";
-- Expected: unique_codes == total_accounts

-- Check cross-store commission distribution
SELECT 
    "StoreTenantId",
    COUNT(*) as commission_count,
    SUM("CommissionAmount") as total_commission
FROM "AffiliateCommissionLedger"
WHERE "EntryType" = 0 -- Earned
GROUP BY "StoreTenantId"
ORDER BY commission_count DESC;
```

## 🔄 ROLLBACK PROCEDURE (If Needed)

### Step 1: Revert Application Code
```bash
# Deploy previous version
git checkout <previous-commit>
dotnet publish -c Release
systemctl restart pointofsale-api
```

### Step 2: Revert Database (⚠️ DATA LOSS)
```sql
BEGIN;

-- Restore tenant-scoped uniqueness
ALTER TABLE "AffiliateAccounts" 
DROP CONSTRAINT IF EXISTS "UQ_AffiliateAccounts_ReferralCode";

ALTER TABLE "AffiliateAccounts" 
ADD CONSTRAINT "UQ_AffiliateAccounts_Tenant_ReferralCode" 
UNIQUE ("TenantId", "ReferralCode");

-- Remove StoreTenantId (⚠️ loses cross-store tracking)
ALTER TABLE "AffiliateCommissionLedger" 
DROP COLUMN IF EXISTS "StoreTenantId";

COMMIT;
```

**⚠️ WARNING:** Rollback loses cross-store commission tracking data. Only rollback if critical issues occur.

## ✅ SUCCESS CRITERIA

- [ ] All existing affiliates can still log in
- [ ] All existing referral codes still work
- [ ] New affiliates can register without storeSlug
- [ ] Cross-store promotions work correctly
- [ ] Commission ledger has StoreTenantId for all entries
- [ ] Dashboard shows cross-store earnings
- [ ] No NULL StoreTenantId in database
- [ ] No ReferralCode collisions
- [ ] Zero errors in application logs
- [ ] MongoDB projections update correctly

## 📝 NOTES

### Backward Compatibility
- ✅ Existing affiliates continue working (TenantId ignored)
- ✅ Existing referral codes remain valid
- ✅ Existing commission entries unchanged
- ✅ API endpoints unchanged (storeSlug now optional)

### Breaking Changes
- ❌ None - fully backward compatible

### Performance Impact
- Minimal - added one column and one index
- Global ReferralCode lookup uses existing index
- StoreTenantId queries use new index

### Data Migration
- Zero downtime - backfill runs in transaction
- No data loss - TenantId preserved for audit
- Idempotent - safe to re-run migration

## 🎯 FINAL VERIFICATION

Run this comprehensive check after deployment:

```sql
-- AF-19 Health Check
SELECT 
    'StoreTenantId Coverage' as check_name,
    CASE 
        WHEN COUNT(*) FILTER (WHERE "StoreTenantId" IS NULL) = 0 
        THEN '✅ PASS' 
        ELSE '❌ FAIL' 
    END as status,
    COUNT(*) as total_entries,
    COUNT(*) FILTER (WHERE "StoreTenantId" IS NULL) as null_count
FROM "AffiliateCommissionLedger"

UNION ALL

SELECT 
    'ReferralCode Uniqueness' as check_name,
    CASE 
        WHEN COUNT(DISTINCT "ReferralCode") = COUNT(*) 
        THEN '✅ PASS' 
        ELSE '❌ FAIL' 
    END as status,
    COUNT(*) as total_accounts,
    COUNT(*) - COUNT(DISTINCT "ReferralCode") as collision_count
FROM "AffiliateAccounts"
WHERE NOT "IsDeleted";
```

**Expected Output:**
```
check_name                  | status   | total_entries | null_count
----------------------------|----------|---------------|------------
StoreTenantId Coverage      | ✅ PASS  | 1234          | 0
ReferralCode Uniqueness     | ✅ PASS  | 567           | 0
```

## 🎉 COMPLETION

- [ ] All tests pass
- [ ] Monitoring shows healthy metrics
- [ ] No errors in logs for 24 hours
- [ ] Cross-store promotions verified in production
- [ ] Team notified of successful deployment
- [ ] Documentation updated
- [ ] Rollback procedure tested in staging

**Deployment Date:** _____________
**Deployed By:** _____________
**Sign-off:** _____________
