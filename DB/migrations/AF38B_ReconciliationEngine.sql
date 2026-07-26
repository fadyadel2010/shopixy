-- =========================================================================
-- AF-38B: Automatic Reconciliation Engine (Withdrawals vs Legacy Settlement)
-- =========================================================================

-- 1. Add RemainingAmount column to existing AffiliatePayoutRequests table
ALTER TABLE "AffiliatePayoutRequests" 
ADD COLUMN "RemainingAmount" numeric(18,4) NOT NULL DEFAULT 0;

-- 2. Initialize existing rows to the RequestedAmount
UPDATE "AffiliatePayoutRequests"
SET "RemainingAmount" = "RequestedAmount"
WHERE "RemainingAmount" = 0;

-- Note: The Default constraint can be left as 0 safely since the app 
-- always passes the correct initialized value in Entity Framework now.
