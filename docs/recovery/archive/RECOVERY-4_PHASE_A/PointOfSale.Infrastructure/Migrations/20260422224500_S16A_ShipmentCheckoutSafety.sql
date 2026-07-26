-- S16-A: Shipment & Checkout Safety
-- PostgreSQL execution script
--
-- PURPOSE:
-- This script applies the same schema changes as the EF migration
-- 20260422224500_S16A_ShipmentCheckoutSafety while creating the shipment
-- external-id indexes without blocking writes.
--
-- IMPORTANT:
-- CREATE INDEX CONCURRENTLY and DROP INDEX CONCURRENTLY cannot run inside a
-- transaction block. Execute this companion script with transaction wrapping
-- disabled.

-- Step 1: Add columns (idempotent with IF NOT EXISTS)
ALTER TABLE "Shipments"
    ADD COLUMN IF NOT EXISTS "ExternalReferenceId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "CarrierRequestId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationState" character varying(50) NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationAttemptCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationRequestedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationLastAttemptAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationCompletedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationFailedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ExternalRegistrationError" character varying(500) NULL;

-- Step 2: Create indexes without taking a blocking table-write lock
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId"
    ON "Shipments" ("TenantId", "CarrierCode", "ExternalReferenceId")
    WHERE "IsDeleted" = false
      AND "ExternalReferenceId" IS NOT NULL;

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "IX_Shipments_TenantId_CarrierCode_CarrierRequestId"
    ON "Shipments" ("TenantId", "CarrierCode", "CarrierRequestId")
    WHERE "IsDeleted" = false
      AND "CarrierRequestId" IS NOT NULL;

-- Step 3: Validate indexes before marking migration as applied
-- Verify both indexes were created successfully and are valid
DO $$
DECLARE
    v_invalid_count INTEGER;
BEGIN
    -- Check if any of the expected indexes are invalid
    SELECT COUNT(*)
    INTO v_invalid_count
    FROM pg_class c
    JOIN pg_index i ON i.indexrelid = c.oid
    WHERE c.relname IN (
        'IX_Shipments_TenantId_CarrierCode_ExternalReferenceId',
        'IX_Shipments_TenantId_CarrierCode_CarrierRequestId'
    )
    AND i.indisvalid = false;

    -- If any indexes are invalid, raise an error
    IF v_invalid_count > 0 THEN
        RAISE EXCEPTION 'One or more indexes are invalid. Migration cannot be marked as applied. Run REINDEX to fix invalid indexes.';
    END IF;

    -- Verify both indexes exist
    IF NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_Shipments_TenantId_CarrierCode_ExternalReferenceId'
    ) THEN
        RAISE EXCEPTION 'Index IX_Shipments_TenantId_CarrierCode_ExternalReferenceId does not exist.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_Shipments_TenantId_CarrierCode_CarrierRequestId'
    ) THEN
        RAISE EXCEPTION 'Index IX_Shipments_TenantId_CarrierCode_CarrierRequestId does not exist.';
    END IF;
END $$;

-- Step 4: Sync EF migration history (only if indexes are valid)
-- This ensures 'dotnet ef database update' sees this migration as already applied
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260422224500_S16A_ShipmentCheckoutSafety', '9.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Optional verification query:
-- SELECT indexname, indexdef
-- FROM pg_indexes
-- WHERE schemaname = 'public'
--   AND tablename = 'Shipments'
--   AND indexname IN (
--       'IX_Shipments_TenantId_CarrierCode_ExternalReferenceId',
--       'IX_Shipments_TenantId_CarrierCode_CarrierRequestId');

-- Optional rollback:
-- Step 1: Remove migration history entry
-- DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260422224500_S16A_ShipmentCheckoutSafety';
--
-- Step 2: Drop indexes
-- DROP INDEX CONCURRENTLY IF EXISTS "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId";
-- DROP INDEX CONCURRENTLY IF EXISTS "IX_Shipments_TenantId_CarrierCode_CarrierRequestId";
--
-- Step 3: Drop columns
-- ALTER TABLE "Shipments"
--     DROP COLUMN IF EXISTS "ExternalReferenceId",
--     DROP COLUMN IF EXISTS "CarrierRequestId",
--     DROP COLUMN IF EXISTS "ExternalRegistrationState",
--     DROP COLUMN IF EXISTS "ExternalRegistrationAttemptCount",
--     DROP COLUMN IF EXISTS "ExternalRegistrationRequestedAtUtc",
--     DROP COLUMN IF EXISTS "ExternalRegistrationLastAttemptAtUtc",
--     DROP COLUMN IF EXISTS "ExternalRegistrationCompletedAtUtc",
--     DROP COLUMN IF EXISTS "ExternalRegistrationFailedAtUtc",
--     DROP COLUMN IF EXISTS "ExternalRegistrationError";
