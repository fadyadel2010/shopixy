-- =====================================================
-- Chat Hardening Phase CR-1
-- Date: February 13, 2026
-- Purpose: Eliminate architectural debt in chat subsystem
-- =====================================================

-- 1. ADD CHECK CONSTRAINTS (Type Safety Restoration)
-- Prevents invalid enum values at database level
DO $$
BEGIN
    -- Add SenderRole constraint if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CHK_SenderRole_Valid'
    ) THEN
        ALTER TABLE "ServiceRequestMessages"
        ADD CONSTRAINT "CHK_SenderRole_Valid" 
        CHECK ("SenderRole" IN ('Buyer', 'Provider'));
    END IF;

    -- Add MessageType constraint if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CHK_MessageType_Valid'
    ) THEN
        ALTER TABLE "ServiceRequestMessages"
        ADD CONSTRAINT "CHK_MessageType_Valid"
        CHECK ("MessageType" IN ('Text', 'System'));
    END IF;
END $$;

-- 2. CREATE AUDIT LOG TABLE
-- Replaces business logic trigger with audit-only logging
CREATE TABLE IF NOT EXISTS "ChatAuditLog" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "MessageId" UUID NOT NULL,
    "ServiceRequestId" UUID NOT NULL,
    "SenderUserId" UUID NOT NULL,
    "TenantId" UUID NOT NULL,
    "Action" TEXT NOT NULL,
    "Timestamp" TIMESTAMP NOT NULL DEFAULT NOW(),
    "Metadata" JSONB,
    
    CONSTRAINT "FK_ChatAuditLog_Messages" 
        FOREIGN KEY ("MessageId") 
        REFERENCES "ServiceRequestMessages"("Id") 
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_ChatAuditLog_ServiceRequestId" 
ON "ChatAuditLog"("ServiceRequestId");

CREATE INDEX IF NOT EXISTS "IX_ChatAuditLog_Timestamp" 
ON "ChatAuditLog"("Timestamp" DESC);

CREATE INDEX IF NOT EXISTS "IX_ChatAuditLog_TenantId" 
ON "ChatAuditLog"("TenantId");

-- 3. REPLACE BUSINESS LOGIC TRIGGER WITH AUDIT TRIGGER
-- Remove authorization/business logic from database layer
-- Business logic now lives ONLY in ChatService (single source of truth)
DROP TRIGGER IF EXISTS validate_message_trigger ON "ServiceRequestMessages";
DROP FUNCTION IF EXISTS validate_message_requires_active_request();

CREATE OR REPLACE FUNCTION audit_message_insert()
RETURNS TRIGGER AS $$
BEGIN
    -- Audit only - NO business logic, NO authorization checks
    INSERT INTO "ChatAuditLog" (
        "MessageId", "ServiceRequestId", "SenderUserId", "TenantId",
        "Action", "Timestamp", "Metadata"
    ) VALUES (
        NEW."Id", NEW."ServiceRequestId", NEW."SenderUserId", NEW."TenantId",
        'MESSAGE_SENT', NOW(),
        jsonb_build_object(
            'senderRole', NEW."SenderRole",
            'messageType', NEW."MessageType"
        )
    );
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS audit_message_trigger ON "ServiceRequestMessages";
CREATE TRIGGER audit_message_trigger
AFTER INSERT ON "ServiceRequestMessages"
FOR EACH ROW
EXECUTE FUNCTION audit_message_insert();

-- 3B. ADD IMMUTABILITY TRIGGER (Enforce message immutability at DB level)
CREATE OR REPLACE FUNCTION prevent_message_modification()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD."Body" IS DISTINCT FROM NEW."Body" OR
       OLD."SenderUserId" IS DISTINCT FROM NEW."SenderUserId" OR
       OLD."SenderRole" IS DISTINCT FROM NEW."SenderRole" OR
       OLD."MessageType" IS DISTINCT FROM NEW."MessageType" THEN
        RAISE EXCEPTION 'Messages are immutable - cannot modify Body, SenderUserId, SenderRole, or MessageType';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS prevent_message_modification_trigger ON "ServiceRequestMessages";
CREATE TRIGGER prevent_message_modification_trigger
BEFORE UPDATE ON "ServiceRequestMessages"
FOR EACH ROW
EXECUTE FUNCTION prevent_message_modification();

-- 4. ADD PERFORMANCE INDEXES
-- Improve query performance for filtered queries
CREATE INDEX IF NOT EXISTS "IX_ServiceRequestMessages_SenderRole" 
ON "ServiceRequestMessages"("SenderRole") 
WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_ServiceRequestMessages_MessageType" 
ON "ServiceRequestMessages"("MessageType") 
WHERE "IsDeleted" = FALSE;

-- 5. VERIFY CONSTRAINTS WORK
-- Test that invalid values are rejected
DO $$
BEGIN
    -- Test invalid SenderRole
    BEGIN
        INSERT INTO "ServiceRequestMessages" (
            "Id", "TenantId", "ServiceRequestId", "SenderUserId",
            "SenderRole", "MessageType", "Body", "CreatedAt"
        ) VALUES (
            gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
            'InvalidRole', 'Text', 'test', NOW()
        );
        RAISE EXCEPTION 'CHECK constraint failed - invalid SenderRole was accepted';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'CHECK constraint working - invalid SenderRole rejected ✓';
    END;
    
    -- Test invalid MessageType
    BEGIN
        INSERT INTO "ServiceRequestMessages" (
            "Id", "TenantId", "ServiceRequestId", "SenderUserId",
            "SenderRole", "MessageType", "Body", "CreatedAt"
        ) VALUES (
            gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
            'Buyer', 'InvalidType', 'test', NOW()
        );
        RAISE EXCEPTION 'CHECK constraint failed - invalid MessageType was accepted';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'CHECK constraint working - invalid MessageType rejected ✓';
    END;
    
    RAISE NOTICE 'All CHECK constraints verified successfully ✓';
END $$;

-- 6. SUMMARY
DO $$
BEGIN
    RAISE NOTICE '================================================';
    RAISE NOTICE 'Chat Hardening Phase CR-1 Complete';
    RAISE NOTICE '================================================';
    RAISE NOTICE '✓ CHECK constraints added (type safety)';
    RAISE NOTICE '✓ Audit log table created';
    RAISE NOTICE '✓ Business logic trigger removed';
    RAISE NOTICE '✓ Audit-only trigger installed';
    RAISE NOTICE '✓ Immutability trigger added';
    RAISE NOTICE '✓ Performance indexes added';
    RAISE NOTICE '✓ Constraints verified';
    RAISE NOTICE '================================================';
    RAISE NOTICE 'Business logic now lives ONLY in ChatService';
    RAISE NOTICE 'Database enforces data integrity only';
    RAISE NOTICE '================================================';
END $$;
