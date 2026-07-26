-- ============================================================================
-- AF-14 + AF-14C: Add Affiliate Accounting accounts to existing tenant
-- Replace the UUID below with your tenant's ID
-- AF-14C: PreventNegative=true on Affiliate Payable (2105)
-- ============================================================================

DO $$
DECLARE
    v_tenant_id UUID := 'fc1b4f0e-7bf1-4d01-aac5-c015ce8a81c0';  -- Production tenant
    v_now TIMESTAMP WITH TIME ZONE := NOW();
BEGIN
    -- 2105 - Affiliate Payable (Liability, PreventNegative=true)
    -- AccountType: 2 = Liability (NOT 1=Asset!)
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted", "PreventNegative")
    SELECT gen_random_uuid(), v_tenant_id, '2105', 'Affiliate Payable', 2, 0, true, v_now, false, true
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2105');

    -- If account already exists (from AF-14), ensure PreventNegative is set
    UPDATE "LedgerAccounts"
    SET "PreventNegative" = true
    WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2105' AND "PreventNegative" = false;

    -- 6110 - Affiliate Marketing Expense (Expense)
    -- AccountType: 5 = Expense (NOT 4=Revenue!)
    INSERT INTO "LedgerAccounts" ("Id", "TenantId", "AccountCode", "Name", "AccountType", "CurrentBalance", "IsActive", "CreatedAt", "IsDeleted")
    SELECT gen_random_uuid(), v_tenant_id, '6110', 'Affiliate Marketing Expense', 5, 0, true, v_now, false
    WHERE NOT EXISTS (SELECT 1 FROM "LedgerAccounts" WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6110');

    -- FIX: Correct AccountType for any previously-seeded accounts with wrong type
    UPDATE "LedgerAccounts"
    SET "AccountType" = 2  -- Liability
    WHERE "TenantId" = v_tenant_id AND "AccountCode" = '2105' AND "AccountType" != 2;

    UPDATE "LedgerAccounts"
    SET "AccountType" = 5  -- Expense
    WHERE "TenantId" = v_tenant_id AND "AccountCode" = '6110' AND "AccountType" != 5;

    RAISE NOTICE 'AF-14/AF-14C accounts seeded for tenant %', v_tenant_id;
END $$;
