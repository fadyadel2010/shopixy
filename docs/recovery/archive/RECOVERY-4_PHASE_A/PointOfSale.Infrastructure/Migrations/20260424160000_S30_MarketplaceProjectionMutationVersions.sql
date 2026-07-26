-- ============================================================================
-- Migration: S30_MarketplaceProjectionMutationVersions
-- Description: Add monotonic source mutation versions for marketplace read-model
--              projections so stale events cannot win by timestamp ordering.
-- Date: 2026-04-24
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;

ALTER TABLE "ProductVariants"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;

ALTER TABLE "ProductVariantStocks"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;

UPDATE "Products"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" = 0;

UPDATE "ProductVariants"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" = 0;

UPDATE "ProductVariantStocks"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" = 0;

UPDATE "Orders"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" = 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424160000_S30_MarketplaceProjectionMutationVersions', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424160000_S30_MarketplaceProjectionMutationVersions'
);

COMMIT;
