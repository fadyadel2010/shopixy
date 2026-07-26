-- =========================================================================
-- AF-38B HOTFIX: AffiliateCommissionLedger Check Constraints
-- Allow EntryType = 4 (Payout) to be recorded without violating constraints.
-- =========================================================================

-- 1. Drop existing constraints
ALTER TABLE "AffiliateCommissionLedger" 
DROP CONSTRAINT "CK_AffiliateCommLedger_EntryType",
DROP CONSTRAINT "CK_AffiliateCommLedger_ReversalRef";

-- 2. Add updated constraints to allow EntryType = 4 (Payout) 
--    and specify that Payout entries do not need an OriginalEntryId.
ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType" 
    CHECK ("EntryType" IN (0, 1, 2, 3, 4));

ALTER TABLE "AffiliateCommissionLedger" 
ADD CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" 
    CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" IN (0, 4) AND "OriginalEntryId" IS NULL));
