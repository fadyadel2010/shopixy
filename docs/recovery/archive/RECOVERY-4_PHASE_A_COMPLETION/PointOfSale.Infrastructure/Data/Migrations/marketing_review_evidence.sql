-- Phase MR-1: Marketing Review Evidence
-- Adds marketing columns to ProviderTrustAggregates and creates MarketingReviewEvidences table

-- 0. Add marketing columns to ProviderTrustAggregates (idempotent via IF NOT EXISTS)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ProviderTrustAggregates' AND column_name = 'MarketingReviewCount') THEN
        ALTER TABLE "ProviderTrustAggregates" ADD COLUMN "MarketingReviewCount" integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ProviderTrustAggregates' AND column_name = 'PosVerifiedCount') THEN
        ALTER TABLE "ProviderTrustAggregates" ADD COLUMN "PosVerifiedCount" integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ProviderTrustAggregates' AND column_name = 'TotalVerifiedRevenue') THEN
        ALTER TABLE "ProviderTrustAggregates" ADD COLUMN "TotalVerifiedRevenue" decimal(18,2) NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ProviderTrustAggregates' AND column_name = 'TotalVerifiedOrders') THEN
        ALTER TABLE "ProviderTrustAggregates" ADD COLUMN "TotalVerifiedOrders" integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ProviderTrustAggregates' AND column_name = 'MarketingWeightedScore') THEN
        ALTER TABLE "ProviderTrustAggregates" ADD COLUMN "MarketingWeightedScore" decimal(18,2) NOT NULL DEFAULT 0;
    END IF;
END $$;

-- 1. Create Table
CREATE TABLE IF NOT EXISTS "MarketingReviewEvidences" (
    "Id"                uuid NOT NULL,
    "TenantId"          uuid NOT NULL,
    "ServiceReviewId"   uuid NOT NULL,
    "CampaignName"      varchar(200) NOT NULL,
    "Platform"          varchar(50) NOT NULL,
    "StartDate"         date NOT NULL,
    "EndDate"           date NOT NULL,
    "OrdersCount"       integer NULL,
    "RevenueAmount"     decimal(18,2) NULL,
    "EvidenceType"      varchar(20) NOT NULL,
    "CreatedAt"         timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt"         timestamp with time zone NULL,
    "CreatedBy"         uuid NULL,
    "UpdatedBy"         uuid NULL,
    "IsDeleted"         boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_MarketingReviewEvidences" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId" 
        FOREIGN KEY ("ServiceReviewId") REFERENCES "ServiceReviews" ("Id") ON DELETE CASCADE
);

-- 2. UNIQUE constraint: exactly one evidence per review
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketingReviewEvidences_ServiceReview" 
    ON "MarketingReviewEvidences" ("ServiceReviewId");

-- 3. Query indexes
CREATE INDEX IF NOT EXISTS "IX_MarketingReviewEvidences_TenantId" 
    ON "MarketingReviewEvidences" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_MarketingReviewEvidences_Platform" 
    ON "MarketingReviewEvidences" ("Platform");

CREATE INDEX IF NOT EXISTS "IX_MarketingReviewEvidences_EvidenceType" 
    ON "MarketingReviewEvidences" ("EvidenceType");

-- 4. CHECK constraint: PosVerified requires both metrics
-- If EvidenceType = 'PosVerified', then OrdersCount AND RevenueAmount must be NOT NULL
-- If EvidenceType = 'SelfReported', then OrdersCount AND RevenueAmount must be NULL
ALTER TABLE "MarketingReviewEvidences" 
    DROP CONSTRAINT IF EXISTS "CK_MarketingReviewEvidences_EvidenceType_Metrics";
ALTER TABLE "MarketingReviewEvidences" 
    ADD CONSTRAINT "CK_MarketingReviewEvidences_EvidenceType_Metrics"
    CHECK (
        ("EvidenceType" = 'PosVerified' AND "OrdersCount" IS NOT NULL AND "RevenueAmount" IS NOT NULL)
        OR
        ("EvidenceType" = 'SelfReported' AND "OrdersCount" IS NULL AND "RevenueAmount" IS NULL)
    );

-- 5. CHECK constraint: EndDate >= StartDate
ALTER TABLE "MarketingReviewEvidences" 
    DROP CONSTRAINT IF EXISTS "CK_MarketingReviewEvidences_DateRange";
ALTER TABLE "MarketingReviewEvidences" 
    ADD CONSTRAINT "CK_MarketingReviewEvidences_DateRange"
    CHECK ("EndDate" >= "StartDate");

-- 6. CHECK constraint: Non-negative metrics
ALTER TABLE "MarketingReviewEvidences" 
    DROP CONSTRAINT IF EXISTS "CK_MarketingReviewEvidences_NonNegativeMetrics";
ALTER TABLE "MarketingReviewEvidences" 
    ADD CONSTRAINT "CK_MarketingReviewEvidences_NonNegativeMetrics"
    CHECK (
        ("OrdersCount" IS NULL OR "OrdersCount" >= 0)
        AND
        ("RevenueAmount" IS NULL OR "RevenueAmount" >= 0)
    );
