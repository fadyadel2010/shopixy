-- Fix missing AccountLedger entries for Customer Opening Balances

INSERT INTO "AccountLedgers" (
    "Id", 
    "PartyType", 
    "PartyId", 
    "TransactionDate", 
    "TransactionType", 
    "DocumentNumber", 
    "DebitAmount", 
    "CreditAmount", 
    "RunningBalance", 
    "Notes", 
    "CreatedAt", 
    "UpdatedAt",
    "IsDeleted"
)
SELECT 
    gen_random_uuid(), -- Generate new ID
    0, -- PartyType 0 = Customer
    ct."CustomerId",
    ct."TransactionDate",
    ct."TransactionType",
    ct."ReferenceNumber",
    ct."DebitAmount",
    ct."CreditAmount",
    ct."BalanceAfter",
    COALESCE(ct."Notes", 'Opening Balance'),
    ct."CreatedAt",
    ct."UpdatedAt",
    false -- IsDeleted
FROM "CustomerTransactions" ct
WHERE ct."IsOpeningBalance" = true
AND NOT EXISTS (
    SELECT 1 FROM "AccountLedgers" al 
    WHERE al."PartyId" = ct."CustomerId" 
    AND al."TransactionType" = ct."TransactionType"
);
