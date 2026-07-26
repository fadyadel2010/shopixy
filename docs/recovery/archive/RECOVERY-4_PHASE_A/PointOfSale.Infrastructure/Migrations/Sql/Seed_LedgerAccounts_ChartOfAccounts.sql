-- ============================================================================
-- Seed Ledger Accounts (Chart of Accounts) for Tenant
-- Run this in PgAdmin4 to create the standard accounts
-- Date: 2026-02-09
-- ============================================================================

-- Replace this GUID with your tenant's ID
DO $$
DECLARE
    v_tenant_id UUID := '8a9f672b-43d5-4a2c-9762-5df63410fb73';
    v_now TIMESTAMP WITH TIME ZONE := NOW();
BEGIN
    -- ════════════════════════════════════════════════════════════════════════
    -- ASSET ACCOUNTS (1xxx)
    -- ════════════════════════════════════════════════════════════════════════
    
    -- 1010 - Cash in Hand (Cashbox Main)
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1010', 'Cash in Hand', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1010');
    
    -- 1020 - POS Visa Account
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1020', 'POS Visa Account', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1020');
    
    -- 1030 - Bank Account
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1030', 'Bank Account', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1030');
    
    -- 1110 - Visa Clearing
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1110', 'Visa Clearing', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1110');
    
    -- 1120 - Stripe Clearing
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1120', 'Stripe Clearing', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1120');
    
    -- 1130 - COD Clearing
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1130', 'COD Clearing', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1130');
    
    -- 1140 - Wallet Clearing (Vodafone Cash, etc.)
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1140', 'Wallet Clearing', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1140');
    
    -- 1310 - Inventory
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1310', 'Inventory', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1310');
    
    -- 1410 - Accounts Receivable
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1410', 'Accounts Receivable', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1410');

    -- 1420 - Marketplace Vendor Receivable
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '1420', 'Marketplace Vendor Receivable', 0, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '1420');

    -- ════════════════════════════════════════════════════════════════════════
    -- LIABILITY ACCOUNTS (2xxx)
    -- ════════════════════════════════════════════════════════════════════════
    
    -- 2010 - Supplier Payables
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '2010', 'Supplier Payables', 1, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2010');
    
    -- 2020 - Customer Deposits
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '2020', 'Customer Deposits', 1, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2020');

    -- 2105 - Affiliate Payable (AF-14, AF-14C: PreventNegative=true)
    -- AccountType: 2 = Liability
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted", "PreventNegative")
    SELECT gen_random_uuid(), v_tenant_id, '2105', 'Affiliate Payable', 2, 0, true, v_now, false, true
WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2105');

    -- 3010 - Opening Balance Equity
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '3010', 'Opening Balance Equity', 3, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '3010');

    -- ════════════════════════════════════════════════════════════════════════
    -- REVENUE ACCOUNTS (4xxx)
    -- ════════════════════════════════════════════════════════════════════════
    
    -- 4010 - POS Sales Revenue
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '4010', 'POS Sales Revenue', 3, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '4010');
    
    -- 4020 - E-commerce Sales Revenue
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '4020', 'E-commerce Sales Revenue', 3, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '4020');
    
    -- 4030 - Shipping Revenue
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '4030', 'Shipping Revenue', 3, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '4030');

    -- ════════════════════════════════════════════════════════════════════════
    -- COGS ACCOUNTS (5xxx)
    -- ════════════════════════════════════════════════════════════════════════
    
    -- 5010 - Cost of Goods Sold
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '5010', 'Cost of Goods Sold', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '5010');

    -- ════════════════════════════════════════════════════════════════════════
    -- EXPENSE ACCOUNTS (6xxx)
    -- ════════════════════════════════════════════════════════════════════════
    
    -- 6010 - Gateway Fees
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6010', 'Gateway Fees', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6010');
    
    -- 6020 - Shipping Fees
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6020', 'Shipping Fees', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6020');
    
    -- 6030 - Bank Fees
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6030', 'Bank Fees', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6030');
    
    -- 6100 - General Expenses
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6100', 'General Expenses', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6100');

    -- 6110 - Affiliate Marketing Expense (AF-14)
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6110', 'Affiliate Marketing Expense', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6110');

    -- 6120 - Accounts Receivable Adjustments
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6120', 'Accounts Receivable Adjustments', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6120');

    -- 6130 - Accounts Payable Adjustments
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6130', 'Accounts Payable Adjustments', 4, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6130');

    RAISE NOTICE 'Chart of Accounts seeded for tenant %', v_tenant_id;
END $$;

-- Verify the accounts were created
SELECT "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive"
FROM "LedgerAccounts"
WHERE "TenantId" = '8a9f672b-43d5-4a2c-9762-5df63410fb73'
ORDER BY "AccountCode";
