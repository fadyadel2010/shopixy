-- =========================================================================
-- AF-38B HOTFIX 2: AffiliateCommissionLedger Rate Constraint
-- Allow CommissionRate = 0 for EntryType = 4 (Payout).
-- =========================================================================

ALTER TABLE "AffiliateCommissionLedger" 
DROP CONSTRAINT "CK_AffiliateCommLedger_Rate";

ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_Rate" 
    CHECK (("CommissionRate" > 0 AND "CommissionRate" <= 0.50) OR ("EntryType" = 4 AND "CommissionRate" = 0));
