-- =============================================================================
-- AF-7: Affiliate Commission Settlement
-- Migration: Extend AffiliateCommissionLedger for settlement lifecycle
-- =============================================================================
-- CHANGES:
-- 1. Update CK_AffiliateCommLedger_EntryType to allow Approved=2, Paid=3
-- 2. Update CK_AffiliateCommLedger_ReversalRef so Approved/Paid require OriginalEntryId
-- =============================================================================
-- SAFETY: Idempotent — uses DROP IF EXISTS + CREATE pattern
-- =============================================================================

-- 1. Drop and recreate EntryType CHECK constraint
ALTER TABLE "AffiliateCommissionLedger"
    DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_EntryType";

ALTER TABLE "AffiliateCommissionLedger"
    ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType"
    CHECK ("EntryType" IN (0, 1, 2, 3));

-- 2. Drop and recreate ReversalRef CHECK constraint
ALTER TABLE "AffiliateCommissionLedger"
    DROP CONSTRAINT IF EXISTS "CK_AffiliateCommLedger_ReversalRef";

ALTER TABLE "AffiliateCommissionLedger"
    ADD CONSTRAINT "CK_AffiliateCommLedger_ReversalRef"
    CHECK (
        ("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) 
        OR 
        ("EntryType" = 0 AND "OriginalEntryId" IS NULL)
    );

-- Verification
DO $$
BEGIN
    RAISE NOTICE 'AF-7 migration complete — EntryType constraint updated to allow 0,1,2,3';
END $$;
