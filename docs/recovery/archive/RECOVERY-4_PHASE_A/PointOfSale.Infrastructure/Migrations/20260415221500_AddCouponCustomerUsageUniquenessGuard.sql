-- Migration: AddCouponCustomerUsageUniquenessGuard
-- Date: 2026-04-15
-- PgAdmin4-ready PostgreSQL script
--
-- Purpose:
-- 1. Remove the legacy CouponUsages.CustomerId -> OnlineCustomers.Id foreign key
-- 2. Replace the old non-unique index with a partial unique index
-- 3. Enforce one active coupon usage per authenticated customer per coupon
--
-- All operations (cleanup + schema changes) are wrapped in a single transaction
-- to ensure atomicity: if the schema change fails, the data cleanup rolls back.

BEGIN;

-- Preflight: Check for duplicates and perform deterministic cleanup before creating unique index.
-- This prevents the CREATE UNIQUE INDEX from failing due to existing duplicates.
DO $$
DECLARE
    duplicate_count INTEGER;
    rows_soft_deleted INTEGER := 0;
BEGIN
    -- Check if duplicates exist
    SELECT COUNT(*)
    INTO duplicate_count
    FROM (
        SELECT "TenantId", "CouponId", "CustomerId"
        FROM "CouponUsages"
        WHERE "CustomerId" IS NOT NULL
          AND COALESCE("IsDeleted", false) = false
        GROUP BY "TenantId", "CouponId", "CustomerId"
        HAVING COUNT(*) > 1
    ) duplicates;

    -- If duplicates exist, perform deterministic cleanup
    IF duplicate_count > 0 THEN
        RAISE NOTICE 'Found % duplicate customer usage groups. Performing cleanup...', duplicate_count;
        
        -- Soft-delete older duplicate rows, keeping only the most recent one per group
        -- (most recent = highest "UsedAtUtc" or "CreatedAt" if "UsedAtUtc" is null)
        WITH ranked_usages AS (
            SELECT 
                "Id",
                ROW_NUMBER() OVER (
                    PARTITION BY "TenantId", "CouponId", "CustomerId"
                    ORDER BY COALESCE("UsedAtUtc", "CreatedAt") DESC, "Id" DESC
                ) AS rn
            FROM "CouponUsages"
            WHERE "CustomerId" IS NOT NULL
              AND COALESCE("IsDeleted", false) = false
        )
        UPDATE "CouponUsages"
        SET "IsDeleted" = true
        FROM ranked_usages
        WHERE "CouponUsages"."Id" = ranked_usages."Id"
          AND ranked_usages.rn > 1;
        
        GET DIAGNOSTICS rows_soft_deleted = ROW_COUNT;
        RAISE NOTICE 'Soft-deleted % duplicate CouponUsages rows to enforce uniqueness constraint', rows_soft_deleted;
    ELSE
        RAISE NOTICE 'No duplicate customer usage records found. Proceeding with unique index creation.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

ALTER TABLE "CouponUsages"
    DROP CONSTRAINT IF EXISTS "FK_CouponUsages_OnlineCustomers_CustomerId";

DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
    WHERE "CustomerId" IS NOT NULL
      AND "IsDeleted" = false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260415221500_AddCouponCustomerUsageUniquenessGuard', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

-- Optional rollback:
-- BEGIN;
-- DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";
-- ALTER TABLE "CouponUsages"
--     ADD CONSTRAINT "FK_CouponUsages_OnlineCustomers_CustomerId"
--     FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL;
-- CREATE INDEX IF NOT EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId"
--     ON "CouponUsages" ("TenantId", "CouponId", "CustomerId");
-- DELETE FROM "__EFMigrationsHistory"
-- WHERE "MigrationId" = '20260415221500_AddCouponCustomerUsageUniquenessGuard';
-- COMMIT;
