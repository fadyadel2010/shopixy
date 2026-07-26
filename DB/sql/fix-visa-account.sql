-- Fix Visa Account IsDeleted flag
-- This script ensures the Visa Account is visible in the Main & Sub Accounts screens

-- Update the Visa Account to ensure it's not soft-deleted
UPDATE "FinancialAccounts"
SET 
    "IsDeleted" = false,
    "Name" = 'حساب الفيزا',
    "AccountNumber" = 'VISA-001',
    "IsSystem" = true,
    "UpdatedAt" = NOW()
WHERE "Id" = '11111111-1111-1111-1111-111111111111';

-- If the account doesn't exist, insert it
INSERT INTO "FinancialAccounts" 
    ("Id", "Name", "AccountNumber", "IsSystem", "Balance", "IsDeleted", "CreatedAt", "ParentAccountId")
SELECT 
    '11111111-1111-1111-1111-111111111111'::uuid,
    'حساب الفيزا',
    'VISA-001',
    true,
    0,
    false,
    '2024-01-01 00:00:00'::timestamp,
    NULL
WHERE NOT EXISTS (
    SELECT 1 FROM "FinancialAccounts" 
    WHERE "Id" = '11111111-1111-1111-1111-111111111111'
);

-- Verify the account exists and is visible
SELECT 
    "Id", 
    "Name", 
    "AccountNumber", 
    "IsSystem", 
    "Balance", 
    "IsDeleted",
    "ParentAccountId",
    "CreatedAt"
FROM "FinancialAccounts"
WHERE "Id" = '11111111-1111-1111-1111-111111111111';
