CREATE OR REPLACE FUNCTION validate_message_requires_active_request()
RETURNS TRIGGER AS $$
DECLARE
    request_status text; -- Changed from "ServiceRequestStatus" to text
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
    -- Since request_status is now text, this string comparison works correctly
    IF request_status NOT IN ('Accepted', 'CompletedByProvider', 'CompletedByBuyer') THEN  
        RAISE EXCEPTION 'Cannot send message: ServiceRequest status is %. Must be Accepted, CompletedByProvider, or CompletedByBuyer', request_status;
    END IF;

    -- Sender must be participant
    IF NEW."SenderUserId" != request_buyer_id AND NEW."SenderUserId" != request_provider_id THEN
        RAISE EXCEPTION 'Cannot send message: User is not a participant in this ServiceRequest';
    END IF;

    -- Validate SenderRole matches actual role
    -- Comparing NEW."SenderRole" (which should be text now) with string literals works fine
    IF NEW."SenderRole" = 'Buyer' AND NEW."SenderUserId" != request_buyer_id THEN
        RAISE EXCEPTION 'Cannot send message: SenderRole is Buyer but user is not the buyer';
    END IF;

    IF NEW."SenderRole" = 'Provider' AND NEW."SenderUserId" != request_provider_id THEN    
        RAISE EXCEPTION 'Cannot send message: SenderRole is Provider but user is not the provider';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
