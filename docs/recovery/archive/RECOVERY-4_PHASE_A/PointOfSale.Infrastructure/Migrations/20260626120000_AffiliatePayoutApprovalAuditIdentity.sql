ALTER TABLE "AffiliatePayoutRequests"
ADD COLUMN IF NOT EXISTS "ApprovedBy" uuid NULL;

ALTER TABLE "AffiliatePayoutRequests"
ADD COLUMN IF NOT EXISTS "RejectedBy" uuid NULL;

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_ApprovedBy"
ON "AffiliatePayoutRequests" ("ApprovedBy")
WHERE "ApprovedBy" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_RejectedBy"
ON "AffiliatePayoutRequests" ("RejectedBy")
WHERE "RejectedBy" IS NOT NULL;
