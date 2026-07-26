-- Migration: S65_CommerceHubProjectionRetryTracking
-- Purpose: Add retry tracking fields to CommerceHubMarketplaceOrders to prevent old failing projections from blocking newer ones
-- Author: Production Hardening
-- Date: 2026-05-01

-- =====================================================================================
-- PHASE 1: Add retry tracking columns
-- =====================================================================================

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "LastProjectionAttemptAt" timestamp with time zone NULL;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "ProjectionAttemptCount" integer NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "IsProjectionTerminalFailure" boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN "CommerceHubMarketplaceOrders"."LastProjectionAttemptAt" IS 
    'Timestamp of the last projection attempt. Used to prioritize newer failures over old stuck ones.';

COMMENT ON COLUMN "CommerceHubMarketplaceOrders"."ProjectionAttemptCount" IS 
    'Number of projection attempts. Used to identify terminal failures after threshold is exceeded.';

COMMENT ON COLUMN "CommerceHubMarketplaceOrders"."IsProjectionTerminalFailure" IS 
    'True if projection has failed repeatedly and should be skipped by the reconciliation worker.';

-- =====================================================================================
-- PHASE 2: Create index for efficient projection reconciliation queries
-- =====================================================================================

-- Index to support the new ordering strategy: terminal failures last, then by attempt time, then by creation time
CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_ProjectionReconciliation"
    ON "CommerceHubMarketplaceOrders" (
        "IsProjectionTerminalFailure" ASC,
        "LastProjectionAttemptAt" ASC NULLS FIRST,
        "CreatedAt" ASC
    )
    WHERE "IsDeleted" = false AND "IsProjected" = false;

COMMENT ON INDEX "IX_CommerceHubMarketplaceOrders_ProjectionReconciliation" IS 
    'Supports projection reconciliation queries. Orders unprojected orders by: non-terminal failures first, never-attempted first, then oldest attempt, then oldest creation. Prevents old stuck failures from blocking new orders.';

-- =====================================================================================
-- PHASE 3: Validation
-- =====================================================================================

DO $$
BEGIN
    -- Verify columns exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'CommerceHubMarketplaceOrders' 
        AND column_name = 'LastProjectionAttemptAt'
    ) THEN
        RAISE EXCEPTION 'Migration S65 failed: LastProjectionAttemptAt column was not created';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'CommerceHubMarketplaceOrders' 
        AND column_name = 'ProjectionAttemptCount'
    ) THEN
        RAISE EXCEPTION 'Migration S65 failed: ProjectionAttemptCount column was not created';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'CommerceHubMarketplaceOrders' 
        AND column_name = 'IsProjectionTerminalFailure'
    ) THEN
        RAISE EXCEPTION 'Migration S65 failed: IsProjectionTerminalFailure column was not created';
    END IF;

    -- Verify index exists
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes 
        WHERE indexname = 'IX_CommerceHubMarketplaceOrders_ProjectionReconciliation'
    ) THEN
        RAISE EXCEPTION 'Migration S65 failed: IX_CommerceHubMarketplaceOrders_ProjectionReconciliation index was not created';
    END IF;

    RAISE NOTICE 'Migration S65_CommerceHubProjectionRetryTracking completed successfully';
END $$;
