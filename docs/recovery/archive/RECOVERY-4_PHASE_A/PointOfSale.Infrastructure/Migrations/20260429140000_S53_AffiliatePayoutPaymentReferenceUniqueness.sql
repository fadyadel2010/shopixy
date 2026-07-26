-- Migration: S53_AffiliatePayoutPaymentReferenceUniqueness
-- Purpose: Add partial unique constraint on AffiliatePayoutRequests.PaymentReference for Paid status
-- Prevents duplicate payment references from being used for multiple paid payout requests
-- This backs the in-memory check in AffiliateSettlementOrchestrator with a DB-level constraint

-- Create partial unique index on PaymentReference where Status = 4 (Paid)
-- This ensures that each PaymentReference can only be used once for a Paid payout request
-- within the same StoreTenantId
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_PaymentReference_Paid_Unique"
ON "AffiliatePayoutRequests" ("StoreTenantId", "PaymentReference")
WHERE "Status" = 4 AND "PaymentReference" IS NOT NULL;

-- Note: Status = 4 corresponds to PayoutRequestStatus.Paid
-- The constraint only applies when Status is Paid and PaymentReference is not null
-- This allows:
-- - Multiple requests with the same PaymentReference in Pending/Approved/Rejected status
-- - Multiple requests with null PaymentReference
-- But prevents:
-- - Two Paid requests with the same PaymentReference within the same tenant
