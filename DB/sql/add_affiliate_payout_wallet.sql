-- AF-16: Affiliate Payout Wallet Registration
-- Adds payout wallet fields to AffiliateAccounts table

ALTER TABLE "AffiliateAccounts" 
ADD COLUMN "PayoutWalletNumber" TEXT NULL,
ADD COLUMN "PayoutWalletType" TEXT NULL,
ADD COLUMN "PayoutWalletUpdatedAtUtc" TIMESTAMPTZ NULL;

-- Optional index for admin queries (not unique - multiple affiliates may share same wallet)
CREATE INDEX IF NOT EXISTS "IX_AffiliateAccounts_PayoutWallet" 
ON "AffiliateAccounts"("TenantId", "PayoutWalletNumber");

COMMENT ON COLUMN "AffiliateAccounts"."PayoutWalletNumber" IS 'Affiliate payout wallet number (Vodafone Cash, Bank Account, etc.)';
COMMENT ON COLUMN "AffiliateAccounts"."PayoutWalletType" IS 'Wallet type: VodafoneCash, OrangeMoney, EtisalatCash, Bank, etc.';
COMMENT ON COLUMN "AffiliateAccounts"."PayoutWalletUpdatedAtUtc" IS 'Last time wallet was updated by affiliate';
