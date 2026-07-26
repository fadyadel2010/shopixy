-- =====================================================
-- Services Marketplace Foundation - Phase 1
-- =====================================================
-- Purpose: Interaction-verified marketplace with NO payments, NO escrow, NO commission
-- Core Invariant: No ServiceRequest → No Rating

-- =====================================================
-- 1. MarketplaceProfiles
-- =====================================================
CREATE TABLE "MarketplaceProfiles" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "UserId" UUID NOT NULL,
    "DisplayName" VARCHAR(100) NOT NULL,
    "Bio" TEXT,
    "ProfileImageUrl" VARCHAR(500),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_MarketplaceProfiles_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MarketplaceProfiles_Users" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "UQ_MarketplaceProfiles_TenantUser" UNIQUE ("TenantId", "UserId")
);

CREATE INDEX "IX_MarketplaceProfiles_TenantId" ON "MarketplaceProfiles"("TenantId");
CREATE INDEX "IX_MarketplaceProfiles_UserId" ON "MarketplaceProfiles"("UserId");
CREATE INDEX "IX_MarketplaceProfiles_IsActive" ON "MarketplaceProfiles"("IsActive") WHERE "IsDeleted" = FALSE;

-- =====================================================
-- 2. Services
-- =====================================================
CREATE TABLE "Services" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ProfileId" UUID NOT NULL,
    "Title" VARCHAR(200) NOT NULL,
    "Description" TEXT NOT NULL,
    "Category" VARCHAR(100),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_Services_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Services_MarketplaceProfiles" FOREIGN KEY ("ProfileId") REFERENCES "MarketplaceProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Services_TenantId" ON "Services"("TenantId");
CREATE INDEX "IX_Services_ProfileId" ON "Services"("ProfileId");
CREATE INDEX "IX_Services_Category" ON "Services"("Category") WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE;

-- =====================================================
-- 3. ServiceRequests (State Machine Core)
-- =====================================================
CREATE TYPE "ServiceRequestStatus" AS ENUM (
    'Pending',
    'Accepted',
    'CompletedByProvider',
    'CompletedByBuyer',
    'Cancelled'
);

CREATE TABLE "ServiceRequests" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ServiceId" UUID NOT NULL,
    "BuyerUserId" UUID NOT NULL,
    "ProviderUserId" UUID NOT NULL,
    "Status" "ServiceRequestStatus" NOT NULL DEFAULT 'Pending',
    "RequestMessage" TEXT,
    "AcceptedAt" TIMESTAMP,
    "CompletedByProviderAt" TIMESTAMP,
    "CompletedByBuyerAt" TIMESTAMP,
    "CancelledAt" TIMESTAMP,
    "CancellationReason" TEXT,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_ServiceRequests_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceRequests_Services" FOREIGN KEY ("ServiceId") REFERENCES "Services"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceRequests_BuyerUsers" FOREIGN KEY ("BuyerUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceRequests_ProviderUsers" FOREIGN KEY ("ProviderUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    
    -- Enforce state machine timestamps
    CONSTRAINT "CHK_ServiceRequests_AcceptedAt" CHECK (
        ("Status" IN ('Accepted', 'CompletedByProvider', 'CompletedByBuyer') AND "AcceptedAt" IS NOT NULL) OR
        ("Status" NOT IN ('Accepted', 'CompletedByProvider', 'CompletedByBuyer'))
    ),
    CONSTRAINT "CHK_ServiceRequests_CompletedByProviderAt" CHECK (
        ("Status" IN ('CompletedByProvider', 'CompletedByBuyer') AND "CompletedByProviderAt" IS NOT NULL) OR
        ("Status" NOT IN ('CompletedByProvider', 'CompletedByBuyer'))
    ),
    CONSTRAINT "CHK_ServiceRequests_CompletedByBuyerAt" CHECK (
        ("Status" = 'CompletedByBuyer' AND "CompletedByBuyerAt" IS NOT NULL) OR
        ("Status" != 'CompletedByBuyer')
    ),
    CONSTRAINT "CHK_ServiceRequests_CancelledAt" CHECK (
        ("Status" = 'Cancelled' AND "CancelledAt" IS NOT NULL) OR
        ("Status" != 'Cancelled')
    ),
    
    -- Buyer and Provider must be different
    CONSTRAINT "CHK_ServiceRequests_DifferentUsers" CHECK ("BuyerUserId" != "ProviderUserId")
);

CREATE INDEX "IX_ServiceRequests_TenantId" ON "ServiceRequests"("TenantId");
CREATE INDEX "IX_ServiceRequests_ServiceId" ON "ServiceRequests"("ServiceId");
CREATE INDEX "IX_ServiceRequests_BuyerUserId" ON "ServiceRequests"("BuyerUserId");
CREATE INDEX "IX_ServiceRequests_ProviderUserId" ON "ServiceRequests"("ProviderUserId");
CREATE INDEX "IX_ServiceRequests_Status" ON "ServiceRequests"("Status") WHERE "IsDeleted" = FALSE;

-- =====================================================
-- 4. ServiceReviews (Interaction-Verified Only)
-- =====================================================
CREATE TABLE "ServiceReviews" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ServiceRequestId" UUID NOT NULL,
    "ServiceId" UUID NOT NULL,
    "ReviewerUserId" UUID NOT NULL,
    "ProviderUserId" UUID NOT NULL,
    "Rating" INT NOT NULL,
    "Comment" TEXT,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_ServiceReviews_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceReviews_ServiceRequests" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceReviews_Services" FOREIGN KEY ("ServiceId") REFERENCES "Services"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceReviews_ReviewerUsers" FOREIGN KEY ("ReviewerUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceReviews_ProviderUsers" FOREIGN KEY ("ProviderUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    
    -- CRITICAL: One review per ServiceRequest
    CONSTRAINT "UQ_ServiceReviews_ServiceRequest" UNIQUE ("ServiceRequestId"),
    
    -- Rating must be 1-5
    CONSTRAINT "CHK_ServiceReviews_Rating" CHECK ("Rating" >= 1 AND "Rating" <= 5)
);

CREATE INDEX "IX_ServiceReviews_TenantId" ON "ServiceReviews"("TenantId");
CREATE INDEX "IX_ServiceReviews_ServiceId" ON "ServiceReviews"("ServiceId");
CREATE INDEX "IX_ServiceReviews_ProviderUserId" ON "ServiceReviews"("ProviderUserId");
CREATE INDEX "IX_ServiceReviews_Rating" ON "ServiceReviews"("Rating") WHERE "IsDeleted" = FALSE;

-- =====================================================
-- Invariant Enforcement Function
-- =====================================================
-- Prevents review creation unless ServiceRequest is CompletedByBuyer
CREATE OR REPLACE FUNCTION enforce_review_requires_completed_request()
RETURNS TRIGGER AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "ServiceRequests"
        WHERE "Id" = NEW."ServiceRequestId"
        AND "Status" = 'CompletedByBuyer'
        AND "IsDeleted" = FALSE
    ) THEN
        RAISE EXCEPTION 'Cannot create review: ServiceRequest must be CompletedByBuyer';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_enforce_review_invariant
    BEFORE INSERT ON "ServiceReviews"
    FOR EACH ROW
    EXECUTE FUNCTION enforce_review_requires_completed_request();

-- =====================================================
-- State Machine Transition Validation Function
-- =====================================================
CREATE OR REPLACE FUNCTION validate_service_request_status_transition()
RETURNS TRIGGER AS $$
DECLARE
    old_status "ServiceRequestStatus";
BEGIN
    IF TG_OP = 'UPDATE' THEN
        old_status := OLD."Status";
        
        -- Valid transitions only
        IF old_status = 'Pending' AND NEW."Status" NOT IN ('Accepted', 'Cancelled') THEN
            RAISE EXCEPTION 'Invalid transition from Pending to %', NEW."Status";
        END IF;
        
        IF old_status = 'Accepted' AND NEW."Status" NOT IN ('CompletedByProvider', 'Cancelled') THEN
            RAISE EXCEPTION 'Invalid transition from Accepted to %', NEW."Status";
        END IF;
        
        IF old_status = 'CompletedByProvider' AND NEW."Status" != 'CompletedByBuyer' THEN
            RAISE EXCEPTION 'Invalid transition from CompletedByProvider to %', NEW."Status";
        END IF;
        
        -- Terminal states cannot transition
        IF old_status IN ('CompletedByBuyer', 'Cancelled') THEN
            RAISE EXCEPTION 'Cannot transition from terminal state %', old_status;
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_validate_status_transition
    BEFORE UPDATE ON "ServiceRequests"
    FOR EACH ROW
    WHEN (OLD."Status" IS DISTINCT FROM NEW."Status")
    EXECUTE FUNCTION validate_service_request_status_transition();
