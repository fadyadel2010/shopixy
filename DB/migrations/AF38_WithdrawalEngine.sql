-- =====================================================================
-- AF-38: Hybrid Affiliate Withdrawal Engine — Migration Script
-- Run this in PgAdmin4 against your PointOfSale database.
-- SAFE: Purely additive — no existing tables or columns are modified.
-- =====================================================================

-- 1. Create AffiliatePayoutRequests table
CREATE TABLE IF NOT EXISTS "AffiliatePayoutRequests" (
    "Id"                  UUID            NOT NULL,
    "StoreTenantId"       UUID            NOT NULL,
    "AffiliateAccountId"  UUID            NOT NULL,
    "RequestedAmount"     NUMERIC(18,4)   NOT NULL,
    "ApprovedAmount"      NUMERIC(18,4)   NULL,
    "Status"              INTEGER         NOT NULL DEFAULT 1,  -- 1=Pending, 2=Approved, 3=Rejected, 4=Paid
    "RequestedAtUtc"      TIMESTAMP WITH TIME ZONE NOT NULL,
    "ApprovedAtUtc"       TIMESTAMP WITH TIME ZONE NULL,
    "RejectedAtUtc"       TIMESTAMP WITH TIME ZONE NULL,
    "PaidAtUtc"           TIMESTAMP WITH TIME ZONE NULL,
    "AdminNotes"          VARCHAR(1000)   NULL,
    "PaymentReference"    VARCHAR(500)    NULL,
    "Currency"            VARCHAR(3)      NOT NULL DEFAULT 'EGP',
    CONSTRAINT "PK_AffiliatePayoutRequests" PRIMARY KEY ("Id")
);

-- 2. Create indexes for AffiliatePayoutRequests
CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_TenantId_Status"
    ON "AffiliatePayoutRequests" ("StoreTenantId", "Status");

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_AffiliateId_Status"
    ON "AffiliatePayoutRequests" ("AffiliateAccountId", "Status");

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status"
    ON "AffiliatePayoutRequests" ("StoreTenantId", "AffiliateAccountId", "Status");

-- 3. Add PayoutRequestId column to AffiliateCommissionLedger
-- This column is only populated for EntryType = 4 (Payout)
ALTER TABLE "AffiliateCommissionLedger"
    ADD COLUMN IF NOT EXISTS "PayoutRequestId" UUID NULL;

-- Done! No existing data is modified.
-- The new Payout entry type (4) is handled by EF Core enum mapping.
