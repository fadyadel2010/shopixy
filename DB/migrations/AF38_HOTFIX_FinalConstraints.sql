-- =========================================================================
-- AF-38B HOTFIX FINAL: Reset AffiliateCommissionLedger Constraints
-- This script ONLY targets the Check Constraints and ignores tables/columns.
-- =========================================================================

START TRANSACTION;

-- 1. Safely drop old constraints (if they exist)
ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_EntryType";
ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_Rate";
ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_ReversalRef";

-- 2. Add the corrected constraints
ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType" 
CHECK ("EntryType" IN (0, 1, 2, 3, 4));

ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_Rate" 
CHECK (("CommissionRate" > 0 AND "CommissionRate" <= 0.50) OR ("EntryType" = 4 AND "CommissionRate" = 0));

ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" 
CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" IN (0, 4) AND "OriginalEntryId" IS NULL));

COMMIT;
