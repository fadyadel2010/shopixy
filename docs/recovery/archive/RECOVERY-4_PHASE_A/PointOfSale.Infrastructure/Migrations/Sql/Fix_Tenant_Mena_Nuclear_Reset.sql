-- ============================================================================
-- NUCLEAR FIX: Reset Chart of Accounts for tenant Mena
-- ============================================================================
-- This script performs a complete reset of the accounting data for the
-- problematic tenant. Use with caution - this will DELETE all journal entries
-- and ledger accounts for this tenant.
-- ============================================================================

-- Step 1: Identify the tenant
-- Tenant ID: 8a9f672b-43d5-4a2c-9762-5df63410fb73 (Mena)

-- Step 2: Delete all failed outbox messages (they reference deleted accounts)
DELETE FROM "AccountingOutboxMessages" 
WHERE "AttemptCount" > 0 
  AND "ProcessedAtUtc" IS NULL;

-- Step 3: Delete all journal entry lines for this tenant
DELETE FROM "JournalEntryLines" 
WHERE "JournalEntryId" IN (
    SELECT "Id" FROM "JournalEntries" 
    WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73'
);

-- Step 4: Delete all journal entries for this tenant
DELETE FROM "JournalEntries" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- Step 5: Delete all commerce accounting audits
DELETE FROM "CommerceAccountingAudits" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- Step 6: Delete all treasury accounts for this tenant (they reference old ledger accounts)
DELETE FROM "TreasuryAccounts" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- Step 7: Delete all ledger accounts for this tenant (so they can be recreated)
DELETE FROM "LedgerAccounts" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- Step 8: Fix AccountType = 0 for all other tenants (just in case)
UPDATE "LedgerAccounts"
SET "AccountType" = 
    CASE 
        WHEN "AccountCode" LIKE '1%' THEN 1  -- Asset
        WHEN "AccountCode" LIKE '2%' THEN 2  -- Liability
        WHEN "AccountCode" LIKE '3%' THEN 3  -- Equity
        WHEN "AccountCode" LIKE '4%' THEN 4  -- Revenue
        WHEN "AccountCode" LIKE '5%' THEN 6  -- CostOfGoodsSold
        WHEN "AccountCode" LIKE '6%' THEN 5  -- Expense
        ELSE 1
    END
WHERE "AccountType" = 0;

-- Verify cleanup
SELECT 'LedgerAccounts' as table_name, COUNT(*) as count 
FROM "LedgerAccounts" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73'
UNION ALL
SELECT 'TreasuryAccounts', COUNT(*) 
FROM "TreasuryAccounts" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73'
UNION ALL
SELECT 'JournalEntries', COUNT(*) 
FROM "JournalEntries" 
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73';

-- All should be 0. Now restart the API and the startup seeder will recreate everything.
