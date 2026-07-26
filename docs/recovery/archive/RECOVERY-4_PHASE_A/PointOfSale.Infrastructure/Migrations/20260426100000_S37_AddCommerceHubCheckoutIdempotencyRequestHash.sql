-- ============================================================================
-- Migration: S37_AddCommerceHubCheckoutIdempotencyRequestHash
-- Description: Add RequestHash column to CommerceHubCheckoutIdempotency with
--              proper constraints to prevent empty/whitespace values.
--              Backfills existing rows with a sentinel hash to allow migration.
-- Date: 2026-04-26
-- ============================================================================

-- Step 1: Add RequestHash column as NULLABLE first (to allow backfill)
ALTER TABLE "CommerceHubCheckoutIdempotency"
    ADD COLUMN IF NOT EXISTS "RequestHash" character varying(64);

-- Step 2: Backfill existing rows with a sentinel hash value
-- This allows existing idempotency records to be migrated without breaking replays
-- The sentinel value 'LEGACY_MIGRATED_NO_HASH' will trigger the IDEMPOTENCY_REPLAY_UNBOUND
-- error in EnsureCheckoutIdempotencyRequestMatches, which is the correct behavior
-- (we cannot safely replay these requests since we don't know the original request hash)
UPDATE "CommerceHubCheckoutIdempotency"
SET "RequestHash" = 'LEGACY_MIGRATED_NO_HASH'
WHERE "RequestHash" IS NULL
   OR LENGTH(TRIM("RequestHash")) = 0;

ALTER TABLE "CommerceHubCheckoutIdempotency"
    ALTER COLUMN "RequestHash" DROP DEFAULT;

-- Step 3: Add NOT NULL constraint now that all rows have a value
ALTER TABLE "CommerceHubCheckoutIdempotency"
    ALTER COLUMN "RequestHash" SET NOT NULL;

-- Step 4: Add CHECK constraint to prevent empty/whitespace values
DO $s37_request_hash_check$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CHK_CommerceHubCheckoutIdempotency_RequestHash_NotEmpty'
    ) THEN
        ALTER TABLE "CommerceHubCheckoutIdempotency"
            ADD CONSTRAINT "CHK_CommerceHubCheckoutIdempotency_RequestHash_NotEmpty"
            CHECK (LENGTH(TRIM("RequestHash")) > 0);
    END IF;
END $s37_request_hash_check$;

-- Add index for RequestHash-based recovery lookups
CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash"
    ON "CommerceHubCheckoutIdempotency" ("TenantId", "RequestHash");

-- ============================================================================
-- Rollback Instructions (Down Migration)
-- ============================================================================
-- DROP INDEX "IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash";
-- ALTER TABLE "CommerceHubCheckoutIdempotency" DROP COLUMN "RequestHash";
