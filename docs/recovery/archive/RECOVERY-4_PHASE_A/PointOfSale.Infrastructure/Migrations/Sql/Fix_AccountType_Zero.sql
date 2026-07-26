-- ============================================================================
-- FIX: AccountType = 0 (Invalid) to proper type
-- ============================================================================
-- This script fixes accounts that were created with AccountType = 0,
-- which is not a valid enum value. The enum starts at 1 (Asset).
--
-- AccountType enum values:
-- 1 = Asset
-- 2 = Liability
-- 3 = Equity
-- 4 = Revenue
-- 5 = Expense
-- 6 = CostOfGoodsSold
-- ============================================================================

-- First, let's see which accounts have invalid type
SELECT "Id", "TenantId", "AccountCode", "Name", "AccountType"
FROM "LedgerAccounts"
WHERE "AccountType" = 0;

-- Fix: Infer correct type from AccountCode pattern
-- 1xxx = Assets
-- 2xxx = Liabilities  
-- 3xxx = Equity
-- 4xxx = Revenue
-- 5xxx = COGS
-- 6xxx = Expenses

UPDATE "LedgerAccounts"
SET "AccountType" = 
    CASE 
        WHEN "AccountCode" LIKE '1%' THEN 1  -- Asset
        WHEN "AccountCode" LIKE '2%' THEN 2  -- Liability
        WHEN "AccountCode" LIKE '3%' THEN 3  -- Equity
        WHEN "AccountCode" LIKE '4%' THEN 4  -- Revenue
        WHEN "AccountCode" LIKE '5%' THEN 6  -- CostOfGoodsSold
        WHEN "AccountCode" LIKE '6%' THEN 5  -- Expense
        ELSE 1  -- Default to Asset if unclear
    END
WHERE "AccountType" = 0;

-- Verify the fix
SELECT "Id", "TenantId", "AccountCode", "Name", "AccountType"
FROM "LedgerAccounts"
WHERE "AccountType" = 0;

-- Should return 0 rows now
