-- Update Visa Account name to English
-- This will be translated in the frontend based on language selection

UPDATE "FinancialAccounts"
SET 
    "Name" = 'Visa Account',
    "UpdatedAt" = NOW()
WHERE "Id" = '11111111-1111-1111-1111-111111111111';

-- Verify the update
SELECT "Id", "Name", "AccountNumber", "IsSystem", "Balance"
FROM "FinancialAccounts"
WHERE "Id" = '11111111-1111-1111-1111-111111111111';
