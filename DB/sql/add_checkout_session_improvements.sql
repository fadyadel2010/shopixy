-- ============================================================================
-- Migration: AddCheckoutSessionImprovements
-- Phase: Abandoned Checkout Production Hardening
--
-- Adds the following columns to CheckoutSessions:
--   RecoveryToken   - Secure token for checkout recovery links
--   DeviceType      - Mobile/Desktop/Tablet attribution
--   Source          - Traffic source attribution
--   FirstActivityAtUtc - First form field change timestamp
--
-- Adds unique index on RecoveryToken for fast lookup.
--
-- Safe to run multiple times (uses IF NOT EXISTS / IF NOT EXISTS).
-- ============================================================================

BEGIN;

-- 1. Add RecoveryToken column (NOT NULL with default for existing rows)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'CheckoutSessions' AND column_name = 'RecoveryToken'
    ) THEN
        ALTER TABLE "CheckoutSessions"
            ADD COLUMN "RecoveryToken" character varying(64) NOT NULL DEFAULT '';
        
        -- Backfill existing rows with unique tokens
        UPDATE "CheckoutSessions"
        SET "RecoveryToken" = replace(gen_random_uuid()::text, '-', '')
        WHERE "RecoveryToken" = '';
    END IF;
END $$;

-- 2. Add DeviceType column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'CheckoutSessions' AND column_name = 'DeviceType'
    ) THEN
        ALTER TABLE "CheckoutSessions"
            ADD COLUMN "DeviceType" character varying(20) NULL;
    END IF;
END $$;

-- 3. Add Source column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'CheckoutSessions' AND column_name = 'Source'
    ) THEN
        ALTER TABLE "CheckoutSessions"
            ADD COLUMN "Source" character varying(50) NULL;
    END IF;
END $$;

-- 4. Add FirstActivityAtUtc column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'CheckoutSessions' AND column_name = 'FirstActivityAtUtc'
    ) THEN
        ALTER TABLE "CheckoutSessions"
            ADD COLUMN "FirstActivityAtUtc" timestamp with time zone NULL;
    END IF;
END $$;

-- 5. Create unique index on RecoveryToken for fast recovery link lookup
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CheckoutSessions_RecoveryToken"
    ON "CheckoutSessions" ("RecoveryToken");

-- 6. Record migration in EF Core history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260312165800_AddCheckoutSessionImprovements', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260312165800_AddCheckoutSessionImprovements'
);

COMMIT;
