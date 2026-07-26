-- Migration: Add ClaimToken and ClaimTimestampUtc to SalesRequestIdempotency
-- Date: 2026-04-08
-- Description: Adds claim token tracking to prevent concurrent idempotency claim theft

-- Add ClaimToken column
ALTER TABLE "SalesRequestIdempotency"
ADD COLUMN IF NOT EXISTS "ClaimToken" character varying(32) NULL;

-- Add ClaimTimestampUtc column
ALTER TABLE "SalesRequestIdempotency"
ADD COLUMN IF NOT EXISTS "ClaimTimestampUtc" timestamp with time zone NULL;

-- Add to migrations history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260408120000_AddSalesIdempotencyClaimToken', '8.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;
