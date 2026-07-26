ALTER TABLE "AffiliatePayoutRequests"
ADD COLUMN IF NOT EXISTS "PaidBy" uuid NULL;
