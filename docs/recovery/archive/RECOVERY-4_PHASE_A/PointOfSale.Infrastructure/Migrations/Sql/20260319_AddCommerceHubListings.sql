BEGIN;

-- ============================================================================
-- CommerceHub Listings
-- Restores or repairs the relational write-side table used by:
--   - Admin CommerceHub publishing UI
--   - CommerceHub publish/update/unpublish handlers
--   - CommerceHub checkout validation
--
-- Safe to run multiple times from PgAdmin.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "CommerceHubListings"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "IsPublished" boolean NOT NULL DEFAULT false,
    "CommerceHubCategoryId" uuid NOT NULL,
    "CommerceHubPrice" numeric(18,2) NULL,
    "AttributesJson" text NULL,
    "AttributeSchemaSnapshotJson" text NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubListings" PRIMARY KEY ("Id")
);

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "ProductId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "VariantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "IsPublished" boolean NOT NULL DEFAULT false;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "CommerceHubCategoryId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "CommerceHubPrice" numeric(18,2) NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "AttributesJson" text NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "AttributeSchemaSnapshotJson" text NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;

ALTER TABLE IF EXISTS "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubListings_ProductId"
    ON "CommerceHubListings" ("ProductId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubListings_VariantId"
    ON "CommerceHubListings" ("VariantId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubListings_TenantId_IsPublished_CategoryId"
    ON "CommerceHubListings" ("TenantId", "IsPublished", "CommerceHubCategoryId");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CommerceHubListings_TenantId_VariantId"
    ON "CommerceHubListings" ("TenantId", "VariantId");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubListings_Products_ProductId')
    THEN
        ALTER TABLE "CommerceHubListings"
            ADD CONSTRAINT "FK_CommerceHubListings_Products_ProductId"
            FOREIGN KEY ("ProductId")
            REFERENCES "Products" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubListings_ProductVariants_VariantId')
    THEN
        ALTER TABLE "CommerceHubListings"
            ADD CONSTRAINT "FK_CommerceHubListings_ProductVariants_VariantId"
            FOREIGN KEY ("VariantId")
            REFERENCES "ProductVariants" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

COMMIT;
