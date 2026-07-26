-- AF-16B: Normalize Existing Payout Wallet Numbers
-- Run this ONCE in staging before production deployment
-- Normalizes mobile wallet numbers to 11-digit Egyptian format

-- ═══════════════════════════════════════════════════════════════════
-- BACKUP FIRST (CRITICAL!)
-- ═══════════════════════════════════════════════════════════════════
-- CREATE TABLE "AffiliateAccounts_Backup_AF16B" AS 
-- SELECT * FROM "AffiliateAccounts" WHERE "PayoutWalletNumber" IS NOT NULL;

-- ═══════════════════════════════════════════════════════════════════
-- NORMALIZE MOBILE WALLETS
-- ═══════════════════════════════════════════════════════════════════

-- Step 1: Strip all non-digit characters from mobile wallets
UPDATE "AffiliateAccounts"
SET "PayoutWalletNumber" = REGEXP_REPLACE("PayoutWalletNumber", '\D', '', 'g')
WHERE "PayoutWalletType" IN ('VodafoneCash', 'OrangeMoney', 'EtisalatCash', 'InstaPay')
  AND "PayoutWalletNumber" IS NOT NULL;

-- Step 2: Handle E.164 format (20XXXXXXXXXX → 0XXXXXXXXXX)
UPDATE "AffiliateAccounts"
SET "PayoutWalletNumber" = '0' || SUBSTRING("PayoutWalletNumber" FROM 3)
WHERE "PayoutWalletType" IN ('VodafoneCash', 'OrangeMoney', 'EtisalatCash', 'InstaPay')
  AND "PayoutWalletNumber" LIKE '20%'
  AND LENGTH("PayoutWalletNumber") = 12;

-- ═══════════════════════════════════════════════════════════════════
-- VALIDATION REPORT
-- ═══════════════════════════════════════════════════════════════════

-- Check for invalid mobile wallet numbers (should be 11 digits)
SELECT 
    "Id",
    "DisplayName",
    "PayoutWalletType",
    "PayoutWalletNumber",
    LENGTH("PayoutWalletNumber") as "Length",
    CASE 
        WHEN LENGTH("PayoutWalletNumber") != 11 THEN 'INVALID LENGTH'
        WHEN "PayoutWalletNumber" !~ '^\d{11}$' THEN 'CONTAINS NON-DIGITS'
        ELSE 'VALID'
    END as "Status"
FROM "AffiliateAccounts"
WHERE "PayoutWalletType" IN ('VodafoneCash', 'OrangeMoney', 'EtisalatCash', 'InstaPay')
  AND "PayoutWalletNumber" IS NOT NULL
ORDER BY "Status" DESC, "DisplayName";

-- Check for invalid bank/other wallet numbers (should be 6-32 alphanumeric)
SELECT 
    "Id",
    "DisplayName",
    "PayoutWalletType",
    "PayoutWalletNumber",
    LENGTH("PayoutWalletNumber") as "Length",
    CASE 
        WHEN LENGTH("PayoutWalletNumber") < 6 THEN 'TOO SHORT'
        WHEN LENGTH("PayoutWalletNumber") > 32 THEN 'TOO LONG'
        WHEN "PayoutWalletNumber" !~ '^[a-zA-Z0-9]+$' THEN 'INVALID CHARACTERS'
        ELSE 'VALID'
    END as "Status"
FROM "AffiliateAccounts"
WHERE "PayoutWalletType" IN ('Bank', 'Other')
  AND "PayoutWalletNumber" IS NOT NULL
ORDER BY "Status" DESC, "DisplayName";

-- ═══════════════════════════════════════════════════════════════════
-- SUMMARY STATISTICS
-- ═══════════════════════════════════════════════════════════════════

SELECT 
    "PayoutWalletType",
    COUNT(*) as "Total",
    COUNT(CASE WHEN LENGTH("PayoutWalletNumber") = 11 THEN 1 END) as "Valid_11_Digits",
    COUNT(CASE WHEN LENGTH("PayoutWalletNumber") != 11 THEN 1 END) as "Invalid_Length"
FROM "AffiliateAccounts"
WHERE "PayoutWalletType" IN ('VodafoneCash', 'OrangeMoney', 'EtisalatCash', 'InstaPay')
  AND "PayoutWalletNumber" IS NOT NULL
GROUP BY "PayoutWalletType"
ORDER BY "PayoutWalletType";

COMMENT ON TABLE "AffiliateAccounts" IS 'AF-16B: Payout wallet numbers normalized on [DATE]';
