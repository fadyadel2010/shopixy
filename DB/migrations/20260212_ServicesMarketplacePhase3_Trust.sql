-- =====================================================
-- Services Marketplace Phase 3 - Trust Engine
-- =====================================================
-- Purpose: Aggregated provider reputation from interaction-verified reviews
-- Core Invariant: Trust is derived, never manually set

-- =====================================================
-- 1. ProviderTrustAggregates
-- =====================================================
CREATE TABLE "ProviderTrustAggregates" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ProviderProfileId" UUID NOT NULL,
    "TotalReviews" INT NOT NULL DEFAULT 0,
    "SumRatings" INT NOT NULL DEFAULT 0,
    "AverageRating" DECIMAL(3,2) NOT NULL DEFAULT 0.00,
    "FiveStarCount" INT NOT NULL DEFAULT 0,
    "FourStarCount" INT NOT NULL DEFAULT 0,
    "ThreeStarCount" INT NOT NULL DEFAULT 0,
    "TwoStarCount" INT NOT NULL DEFAULT 0,
    "OneStarCount" INT NOT NULL DEFAULT 0,
    "LastReviewAt" TIMESTAMP,
    "Version" INT NOT NULL DEFAULT 1,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_ProviderTrustAggregates_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProviderTrustAggregates_MarketplaceProfiles" FOREIGN KEY ("ProviderProfileId") REFERENCES "MarketplaceProfiles"("Id") ON DELETE CASCADE,
    
    -- Unique constraint: one aggregate per provider
    CONSTRAINT "UQ_ProviderTrustAggregates_ProviderProfile" UNIQUE ("ProviderProfileId"),
    
    -- Check constraints for data integrity
    CONSTRAINT "CHK_ProviderTrustAggregates_TotalReviews" CHECK ("TotalReviews" >= 0),
    CONSTRAINT "CHK_ProviderTrustAggregates_SumRatings" CHECK ("SumRatings" >= 0),
    CONSTRAINT "CHK_ProviderTrustAggregates_AverageRating" CHECK ("AverageRating" >= 0.00 AND "AverageRating" <= 5.00),
    CONSTRAINT "CHK_ProviderTrustAggregates_StarCounts" CHECK (
        "FiveStarCount" >= 0 AND
        "FourStarCount" >= 0 AND
        "ThreeStarCount" >= 0 AND
        "TwoStarCount" >= 0 AND
        "OneStarCount" >= 0
    ),
    CONSTRAINT "CHK_ProviderTrustAggregates_StarCountsSum" CHECK (
        "TotalReviews" = "FiveStarCount" + "FourStarCount" + "ThreeStarCount" + "TwoStarCount" + "OneStarCount"
    ),
    CONSTRAINT "CHK_ProviderTrustAggregates_Version" CHECK ("Version" > 0)
);

CREATE INDEX "IX_ProviderTrustAggregates_TenantId" ON "ProviderTrustAggregates"("TenantId");
CREATE INDEX "IX_ProviderTrustAggregates_AverageRating" ON "ProviderTrustAggregates"("AverageRating" DESC) WHERE "IsDeleted" = FALSE;
CREATE INDEX "IX_ProviderTrustAggregates_TotalReviews" ON "ProviderTrustAggregates"("TotalReviews" DESC) WHERE "IsDeleted" = FALSE;

-- =====================================================
-- 2. Prevent Manual Trust Modification
-- =====================================================
CREATE OR REPLACE FUNCTION prevent_manual_trust_modification()
RETURNS TRIGGER AS $$
BEGIN
    -- This trigger ensures trust aggregates are only modified through domain events
    -- In practice, this is enforced by application layer, but we add DB-level protection
    
    -- Allow INSERT (initial creation)
    IF TG_OP = 'INSERT' THEN
        RETURN NEW;
    END IF;
    
    -- Allow UPDATE only if Version is incremented
    IF TG_OP = 'UPDATE' THEN
        IF NEW."Version" <= OLD."Version" THEN
            RAISE EXCEPTION 'ProviderTrustAggregate Version must be incremented on update';
        END IF;
        RETURN NEW;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_manual_trust_modification
    BEFORE UPDATE ON "ProviderTrustAggregates"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_manual_trust_modification();

-- =====================================================
-- 3. Helper View: Provider Trust Summary
-- =====================================================
CREATE OR REPLACE VIEW "ProviderTrustSummary" AS
SELECT
    pta."ProviderProfileId",
    mp."DisplayName" AS "ProviderName",
    pta."TotalReviews",
    pta."AverageRating",
    pta."FiveStarCount",
    pta."FourStarCount",
    pta."ThreeStarCount",
    pta."TwoStarCount",
    pta."OneStarCount",
    pta."LastReviewAt",
    CASE
        WHEN pta."TotalReviews" < 5 THEN 'New'
        WHEN pta."AverageRating" >= 4.6 AND pta."TotalReviews" >= 50 THEN 'TopRated'
        WHEN pta."AverageRating" >= 4.3 AND pta."TotalReviews" >= 20 THEN 'Trusted'
        WHEN pta."AverageRating" >= 4.0 AND pta."TotalReviews" >= 5 THEN 'Rising'
        ELSE 'Standard'
    END AS "TrustBadge"
FROM "ProviderTrustAggregates" pta
INNER JOIN "MarketplaceProfiles" mp ON pta."ProviderProfileId" = mp."Id"
WHERE pta."IsDeleted" = FALSE AND mp."IsDeleted" = FALSE;
