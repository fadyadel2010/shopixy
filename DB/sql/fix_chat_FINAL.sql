DO $$ 
DECLARE 
    r RECORD;
    table_oid OID;
BEGIN 
    -- 1. FIX TABLE COLUMNS (SenderRole, MessageType)
    SELECT oid INTO table_oid FROM pg_class WHERE relname = 'ServiceRequestMessages';
    
    IF table_oid IS NOT NULL THEN
        -- Drop constraints on SenderRole and MessageType
        FOR r IN (
            SELECT conname 
            FROM pg_constraint 
            WHERE conrelid = table_oid
            AND (
                conkey @> ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = table_oid AND attname = 'SenderRole')]
                OR 
                conkey @> ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = table_oid AND attname = 'MessageType')]
            )
        ) LOOP
            EXECUTE 'ALTER TABLE "ServiceRequestMessages" DROP CONSTRAINT ' || quote_ident(r.conname) || ' CASCADE';
        END LOOP;
        
        -- Convert columns to TEXT
        ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "SenderRole" TYPE text USING "SenderRole"::text;
        ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "MessageType" TYPE text USING "MessageType"::text;
    END IF;

    -- 2. DROP OLD TYPES
    DROP TYPE IF EXISTS "SenderRole" CASCADE;
    DROP TYPE IF EXISTS "MessageType" CASCADE;
    
END $$;

-- 3. UPDATE TRIGGER FUNCTION (Fix for 42704 error)
CREATE OR REPLACE FUNCTION validate_message_requires_active_request()
RETURNS TRIGGER AS $func$
DECLARE
    request_status text; -- FIXED: Use text instead of "ServiceRequestStatus"
    request_buyer_id UUID;
    request_provider_id UUID;
BEGIN
    -- Load ServiceRequest (Status is now text)
    SELECT "Status", "BuyerUserId", "ProviderUserId"
    INTO request_status, request_buyer_id, request_provider_id
    FROM "ServiceRequests"
    WHERE "Id" = NEW."ServiceRequestId"
    AND "IsDeleted" = FALSE;

    -- Request must exist
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Cannot send message: ServiceRequest not found';
    END IF;

    -- Request must be in valid state
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
$func$ LANGUAGE plpgsql;
