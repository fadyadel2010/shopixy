-- ═══════════════════════════════════════════════════════════════════
-- AF-11: Affiliate Attribution Pipeline Fix
-- 
-- Adds AffiliateAccountId and AffiliateReferralCode columns to Orders table.
-- These fields are stamped at checkout when the order is placed through an
-- affiliate referral link. They are used by the AffiliateOrderAttributionConsumer
-- to create AffiliateAttribution records and trigger commission recording.
--
-- Safe to apply multiple times (idempotent).
-- Historical orders default to NULL (non-affiliate).
-- ═══════════════════════════════════════════════════════════════════

-- 1. Add AffiliateAccountId column (nullable UUID, FK to AffiliateAccounts)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'AffiliateAccountId'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "AffiliateAccountId" uuid NULL;
        RAISE NOTICE 'AF-11: Added AffiliateAccountId column to Orders table';
    ELSE
        RAISE NOTICE 'AF-11: AffiliateAccountId column already exists — skipping';
    END IF;
END $$;

-- 2. Add AffiliateReferralCode column (nullable text, snapshot)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'AffiliateReferralCode'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "AffiliateReferralCode" text NULL;
        RAISE NOTICE 'AF-11: Added AffiliateReferralCode column to Orders table';
    ELSE
        RAISE NOTICE 'AF-11: AffiliateReferralCode column already exists — skipping';
    END IF;
END $$;

-- 3. Index for affiliate reporting queries (find orders by affiliate)
CREATE INDEX IF NOT EXISTS "IX_Orders_AffiliateAccountId"
ON "Orders" ("AffiliateAccountId")
WHERE "AffiliateAccountId" IS NOT NULL;

-- 4. FK constraint (soft — does not block order creation if affiliate is deleted)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_Orders_AffiliateAccounts_AffiliateAccountId'
    ) THEN
        ALTER TABLE "Orders" 
        ADD CONSTRAINT "FK_Orders_AffiliateAccounts_AffiliateAccountId"
        FOREIGN KEY ("AffiliateAccountId") 
        REFERENCES "AffiliateAccounts"("Id")
        ON DELETE SET NULL;
        RAISE NOTICE 'AF-11: Added FK constraint for AffiliateAccountId';
    ELSE
        RAISE NOTICE 'AF-11: FK constraint already exists — skipping';
    END IF;
END $$;

-- Done
SELECT 'AF-11 migration complete' AS status;
