-- =====================================================================
-- ABANDONED CART RECOVERY ENGINE - HARDENING PHASE
-- Migration: Critical fixes for production readiness
-- Date: 2026-02-04
-- Phase: Hardening - All Critical & High Priority Fixes
-- =====================================================================

-- =====================================================================
-- 1. ADD PENDING STATUS AND EXPIRATION SUPPORT
-- =====================================================================

-- Drop and recreate check constraint to add new statuses
ALTER TABLE "AbandonedCarts"
DROP CONSTRAINT IF EXISTS "CK_AbandonedCarts_RecoveryStatus";

ALTER TABLE "AbandonedCarts"
ADD CONSTRAINT "CK_AbandonedCarts_RecoveryStatus"
CHECK ("RecoveryStatus" IN ('Active', 'Abandoned', 'Pending', 'Triggered', 'Recovered', 'Expired', 'Failed'));

-- Add ExpiredAtUtc column for lifecycle management
ALTER TABLE "AbandonedCarts"
ADD COLUMN IF NOT EXISTS "ExpiredAtUtc" TIMESTAMP NULL;

-- Add PendingAtUtc for tracking when cart entered Pending state
ALTER TABLE "AbandonedCarts"
ADD COLUMN IF NOT EXISTS "PendingAtUtc" TIMESTAMP NULL;

-- Add RetryCount for tracking n8n call failures
ALTER TABLE "AbandonedCarts"
ADD COLUMN IF NOT EXISTS "RetryCount" INTEGER NOT NULL DEFAULT 0;

-- Add LastErrorMessage for debugging
ALTER TABLE "AbandonedCarts"
ADD COLUMN IF NOT EXISTS "LastErrorMessage" VARCHAR(2000) NULL;

-- =====================================================================
-- 2. CREATE OUTBOX TABLE FOR GUARANTEED EVENT DELIVERY
-- =====================================================================

CREATE TABLE IF NOT EXISTS "OutboxMessages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "EventType" VARCHAR(500) NOT NULL,
    "EventPayload" JSONB NOT NULL,
    "TenantId" UUID NULL,
    "AggregateId" UUID NULL,
    "CreatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "ProcessedAtUtc" TIMESTAMP NULL,
    "RetryCount" INTEGER NOT NULL DEFAULT 0,
    "LastErrorMessage" VARCHAR(2000) NULL,
    "LastErrorAtUtc" TIMESTAMP NULL
);

-- Index for unprocessed messages (publisher job)
CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_Unprocessed"
    ON "OutboxMessages"("CreatedAtUtc")
    WHERE "ProcessedAtUtc" IS NULL;

-- Index for cleanup job
CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_Processed"
    ON "OutboxMessages"("ProcessedAtUtc")
    WHERE "ProcessedAtUtc" IS NOT NULL;

-- Index by tenant for tenant-specific queries
CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_TenantId"
    ON "OutboxMessages"("TenantId")
    WHERE "TenantId" IS NOT NULL;

COMMENT ON TABLE "OutboxMessages" IS 
    'Transactional outbox for guaranteed event delivery. Events written in same transaction as domain changes. Phase: Abandoned Cart Hardening.';

-- =====================================================================
-- 3. CREATE ABANDONED CART RECOVERY LOG TABLE
-- =====================================================================

CREATE TABLE IF NOT EXISTS "AbandonedCartRecoveryLogs" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "AbandonedCartId" UUID NOT NULL,
    "CartId" UUID NOT NULL,
    "PhoneNumber" VARCHAR(20) NOT NULL,
    
    -- First message tracking
    "FirstMessageSent" BOOLEAN NOT NULL DEFAULT FALSE,
    "FirstMessageSentAtUtc" TIMESTAMP NULL,
    "FirstMessageError" VARCHAR(2000) NULL,
    
    -- Second message tracking
    "SecondMessageSent" BOOLEAN NOT NULL DEFAULT FALSE,
    "SecondMessageSentAtUtc" TIMESTAMP NULL,
    "SecondMessageError" VARCHAR(2000) NULL,
    
    -- Workflow tracking
    "WorkflowStartedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "WorkflowCompletedAtUtc" TIMESTAMP NULL,
    "IdempotencyKey" VARCHAR(100) NULL,
    
    -- Timestamps
    "CreatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    
    -- Foreign keys
    CONSTRAINT "FK_AbandonedCartRecoveryLogs_AbandonedCarts" 
        FOREIGN KEY ("AbandonedCartId") 
        REFERENCES "AbandonedCarts"("Id") 
        ON DELETE CASCADE,
    CONSTRAINT "FK_AbandonedCartRecoveryLogs_Tenants" 
        FOREIGN KEY ("TenantId") 
        REFERENCES "Tenants"("Id") 
        ON DELETE CASCADE
);

-- Indexes
CREATE INDEX IF NOT EXISTS "IX_AbandonedCartRecoveryLogs_TenantId"
    ON "AbandonedCartRecoveryLogs"("TenantId");

CREATE INDEX IF NOT EXISTS "IX_AbandonedCartRecoveryLogs_AbandonedCartId"
    ON "AbandonedCartRecoveryLogs"("AbandonedCartId");

CREATE INDEX IF NOT EXISTS "IX_AbandonedCartRecoveryLogs_IdempotencyKey"
    ON "AbandonedCartRecoveryLogs"("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_AbandonedCartRecoveryLogs_CreatedAtUtc"
    ON "AbandonedCartRecoveryLogs"("TenantId", "CreatedAtUtc");

COMMENT ON TABLE "AbandonedCartRecoveryLogs" IS 
    'Tracks all recovery workflow executions for analytics and debugging. One record per n8n workflow run.';

-- =====================================================================
-- 4. ADD WHATSAPP RATE LIMITING TABLE
-- =====================================================================

CREATE TABLE IF NOT EXISTS "WhatsAppRateLimits" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "PhoneNumber" VARCHAR(20) NOT NULL,
    "MessageCount" INTEGER NOT NULL DEFAULT 0,
    "WindowStartUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "LastMessageAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "OptedOut" BOOLEAN NOT NULL DEFAULT FALSE,
    "OptedOutAtUtc" TIMESTAMP NULL,
    
    -- Unique constraint: one record per phone per tenant
    CONSTRAINT "UQ_WhatsAppRateLimits_TenantId_PhoneNumber" 
        UNIQUE ("TenantId", "PhoneNumber"),
    
    -- Foreign key
    CONSTRAINT "FK_WhatsAppRateLimits_Tenants" 
        FOREIGN KEY ("TenantId") 
        REFERENCES "Tenants"("Id") 
        ON DELETE CASCADE
);

-- Index for rate limit checks
CREATE INDEX IF NOT EXISTS "IX_WhatsAppRateLimits_TenantId_PhoneNumber"
    ON "WhatsAppRateLimits"("TenantId", "PhoneNumber");

-- Index for opted-out phones
CREATE INDEX IF NOT EXISTS "IX_WhatsAppRateLimits_OptedOut"
    ON "WhatsAppRateLimits"("TenantId")
    WHERE "OptedOut" = TRUE;

COMMENT ON TABLE "WhatsAppRateLimits" IS 
    'Tracks WhatsApp message rate limits and opt-out status per phone number per tenant. Phase: Abandoned Cart Hardening.';

-- =====================================================================
-- 5. ADD N8N WEBHOOK API KEY TO TENANT SETTINGS
-- =====================================================================

ALTER TABLE "TenantAutomationSettings"
ADD COLUMN IF NOT EXISTS "N8nWebhookApiKey" VARCHAR(100) NULL;

COMMENT ON COLUMN "TenantAutomationSettings"."N8nWebhookApiKey" IS 
    'API key for authenticating n8n webhook callbacks. Auto-generated on first use.';

-- =====================================================================
-- 6. ADD INDEXES FOR HARDENING
-- =====================================================================

-- Index for pending carts (timeout recovery)
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_Pending"
    ON "AbandonedCarts"("PendingAtUtc")
    WHERE "RecoveryStatus" = 'Pending';

-- Index for expiration job
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_Expiration"
    ON "AbandonedCarts"("TriggeredAtUtc")
    WHERE "RecoveryStatus" = 'Triggered';

-- Index for retry job
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_Failed"
    ON "AbandonedCarts"("RetryCount", "UpdatedAtUtc")
    WHERE "RecoveryStatus" = 'Failed' AND "RetryCount" < 3;

-- =====================================================================
-- 7. VERIFICATION
-- =====================================================================

DO $$
BEGIN
    -- Verify OutboxMessages table
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'OutboxMessages') THEN
        RAISE NOTICE '✓ OutboxMessages table created successfully';
    ELSE
        RAISE EXCEPTION '✗ OutboxMessages table creation failed';
    END IF;
    
    -- Verify AbandonedCartRecoveryLogs table
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AbandonedCartRecoveryLogs') THEN
        RAISE NOTICE '✓ AbandonedCartRecoveryLogs table created successfully';
    ELSE
        RAISE EXCEPTION '✗ AbandonedCartRecoveryLogs table creation failed';
    END IF;
    
    -- Verify WhatsAppRateLimits table
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'WhatsAppRateLimits') THEN
        RAISE NOTICE '✓ WhatsAppRateLimits table created successfully';
    ELSE
        RAISE EXCEPTION '✗ WhatsAppRateLimits table creation failed';
    END IF;
    
    -- Verify new AbandonedCarts columns
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AbandonedCarts' AND column_name = 'ExpiredAtUtc') THEN
        RAISE NOTICE '✓ ExpiredAtUtc column added successfully';
    ELSE
        RAISE EXCEPTION '✗ ExpiredAtUtc column creation failed';
    END IF;
END $$;

-- =====================================================================
-- ROLLBACK SCRIPT (for reference - run manually if needed)
-- =====================================================================

/*
-- Drop new tables
DROP TABLE IF EXISTS "WhatsAppRateLimits";
DROP TABLE IF EXISTS "AbandonedCartRecoveryLogs";
DROP TABLE IF EXISTS "OutboxMessages";

-- Remove new columns from AbandonedCarts
ALTER TABLE "AbandonedCarts" DROP COLUMN IF EXISTS "ExpiredAtUtc";
ALTER TABLE "AbandonedCarts" DROP COLUMN IF EXISTS "PendingAtUtc";
ALTER TABLE "AbandonedCarts" DROP COLUMN IF EXISTS "RetryCount";
ALTER TABLE "AbandonedCarts" DROP COLUMN IF EXISTS "LastErrorMessage";

-- Remove new column from TenantAutomationSettings
ALTER TABLE "TenantAutomationSettings" DROP COLUMN IF EXISTS "N8nWebhookApiKey";

-- Restore original check constraint
ALTER TABLE "AbandonedCarts" DROP CONSTRAINT IF EXISTS "CK_AbandonedCarts_RecoveryStatus";
ALTER TABLE "AbandonedCarts" ADD CONSTRAINT "CK_AbandonedCarts_RecoveryStatus"
CHECK ("RecoveryStatus" IN ('Active', 'Abandoned', 'Triggered', 'Recovered', 'Expired'));
*/
