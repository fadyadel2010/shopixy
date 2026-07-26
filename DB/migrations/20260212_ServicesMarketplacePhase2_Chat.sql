-- =====================================================
-- Services Marketplace Phase 2 - Real-Time Chat
-- =====================================================
-- Purpose: Interaction-verified chat with immutable message log
-- Core Invariant: No ServiceRequest → No Chat

-- =====================================================
-- 1. Enums
-- =====================================================
CREATE TYPE "SenderRole" AS ENUM (
    'Buyer',
    'Provider'
);

CREATE TYPE "MessageType" AS ENUM (
    'Text',
    'System'
);

-- =====================================================
-- 2. ServiceRequestMessages
-- =====================================================
CREATE TABLE "ServiceRequestMessages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ServiceRequestId" UUID NOT NULL,
    "SenderUserId" UUID NOT NULL,
    "SenderRole" "SenderRole" NOT NULL,
    "MessageType" "MessageType" NOT NULL DEFAULT 'Text',
    "Body" TEXT NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP,
    "CreatedBy" UUID,
    "UpdatedBy" UUID,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "FK_ServiceRequestMessages_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceRequestMessages_ServiceRequests" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceRequestMessages_Users" FOREIGN KEY ("SenderUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    
    -- Body must not be empty
    CONSTRAINT "CHK_ServiceRequestMessages_Body" CHECK (LENGTH(TRIM("Body")) > 0)
);

CREATE INDEX "IX_ServiceRequestMessages_TenantId" ON "ServiceRequestMessages"("TenantId");
CREATE INDEX "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt" ON "ServiceRequestMessages"("ServiceRequestId", "CreatedAt");
CREATE INDEX "IX_ServiceRequestMessages_SenderUserId" ON "ServiceRequestMessages"("SenderUserId");

-- =====================================================
-- 3. Invariant Enforcement: Message Requires Active Request
-- =====================================================
CREATE OR REPLACE FUNCTION validate_message_requires_active_request()
RETURNS TRIGGER AS $$
DECLARE
    request_status "ServiceRequestStatus";
    request_buyer_id UUID;
    request_provider_id UUID;
BEGIN
    -- Load ServiceRequest
    SELECT "Status", "BuyerUserId", "ProviderUserId"
    INTO request_status, request_buyer_id, request_provider_id
    FROM "ServiceRequests"
    WHERE "Id" = NEW."ServiceRequestId"
    AND "IsDeleted" = FALSE;
    
    -- Request must exist
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Cannot send message: ServiceRequest not found';
    END IF;
    
    -- Request must be in valid state for interaction
    IF request_status NOT IN ('Accepted', 'CompletedByProvider', 'CompletedByBuyer') THEN
        RAISE EXCEPTION 'Cannot send message: ServiceRequest status is %. Must be Accepted, CompletedByProvider, or CompletedByBuyer', request_status;
    END IF;
    
    -- Sender must be participant
    IF NEW."SenderUserId" != request_buyer_id AND NEW."SenderUserId" != request_provider_id THEN
        RAISE EXCEPTION 'Cannot send message: User is not a participant in this ServiceRequest';
    END IF;
    
    -- Validate SenderRole matches actual role
    IF NEW."SenderRole" = 'Buyer' AND NEW."SenderUserId" != request_buyer_id THEN
        RAISE EXCEPTION 'Cannot send message: SenderRole is Buyer but user is not the buyer';
    END IF;
    
    IF NEW."SenderRole" = 'Provider' AND NEW."SenderUserId" != request_provider_id THEN
        RAISE EXCEPTION 'Cannot send message: SenderRole is Provider but user is not the provider';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_validate_message_invariant
    BEFORE INSERT ON "ServiceRequestMessages"
    FOR EACH ROW
    EXECUTE FUNCTION validate_message_requires_active_request();

-- =====================================================
-- 4. Prevent Message Updates (Immutability)
-- =====================================================
CREATE OR REPLACE FUNCTION prevent_message_modification()
RETURNS TRIGGER AS $$
BEGIN
    -- Allow soft delete only
    IF OLD."Body" IS DISTINCT FROM NEW."Body" OR
       OLD."SenderUserId" IS DISTINCT FROM NEW."SenderUserId" OR
       OLD."SenderRole" IS DISTINCT FROM NEW."SenderRole" OR
       OLD."MessageType" IS DISTINCT FROM NEW."MessageType" OR
       OLD."ServiceRequestId" IS DISTINCT FROM NEW."ServiceRequestId" THEN
        RAISE EXCEPTION 'Messages are immutable and cannot be modified';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_message_modification
    BEFORE UPDATE ON "ServiceRequestMessages"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_message_modification();
