BEGIN;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "SectionGroupId" uuid NULL;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 1;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "SchemaVersion" integer NOT NULL DEFAULT 1;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "IsDraft" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "IsPublished" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "PublishedAt" timestamp with time zone NULL;

ALTER TABLE "HomepageSections"
    ADD COLUMN IF NOT EXISTS "DraftGroupId" uuid NULL;

UPDATE "HomepageSections"
SET
    "SectionGroupId" = COALESCE("SectionGroupId", "Id"),
    "Version" = CASE WHEN "Version" < 1 THEN 1 ELSE "Version" END,
    "SchemaVersion" = CASE WHEN "SchemaVersion" < 1 THEN 1 ELSE "SchemaVersion" END,
    "IsDeleted" = COALESCE("IsDeleted", FALSE),
    "IsDraft" = FALSE,
    "IsPublished" = TRUE,
    "PublishedAt" = COALESCE("PublishedAt", "UpdatedAt", "CreatedAt", NOW()),
    "DraftGroupId" = NULL;

ALTER TABLE "HomepageSections"
    ALTER COLUMN "SectionGroupId" SET NOT NULL;

DROP INDEX IF EXISTS "IX_HomepageSections_TenantId_Order";
DROP INDEX IF EXISTS "IX_HomepageSections_TenantId_IsActive_Order";
DROP INDEX IF EXISTS "IX_HomepageSections_IsActive_Order";

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_SectionGroupId_Version"
    ON "HomepageSections" ("SectionGroupId", "Version");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_SectionGroupId_Draft"
    ON "HomepageSections" ("SectionGroupId")
    WHERE "IsDraft" = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_SectionGroupId_Published"
    ON "HomepageSections" ("SectionGroupId")
    WHERE "IsPublished" = TRUE;

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_SectionGroupId"
    ON "HomepageSections" ("SectionGroupId");

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_TenantId_IsPublished_Order"
    ON "HomepageSections" ("TenantId", "IsPublished", "Order");

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_TenantId_IsDraft_Order"
    ON "HomepageSections" ("TenantId", "IsDraft", "Order");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_Global_Draft_Order"
    ON "HomepageSections" ("Order")
    WHERE "TenantId" IS NULL
      AND "IsDraft" = TRUE
      AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_Tenant_Draft_Order"
    ON "HomepageSections" ("TenantId", "Order")
    WHERE "TenantId" IS NOT NULL
      AND "IsDraft" = TRUE
      AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_Global_Published_Order"
    ON "HomepageSections" ("Order")
    WHERE "TenantId" IS NULL
      AND "IsPublished" = TRUE
      AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_HomepageSections_Tenant_Published_Order"
    ON "HomepageSections" ("TenantId", "Order")
    WHERE "TenantId" IS NOT NULL
      AND "IsPublished" = TRUE
      AND "IsDeleted" = FALSE;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_HomepageSections_Version')
    THEN
        ALTER TABLE "HomepageSections"
            ADD CONSTRAINT "CK_HomepageSections_Version" CHECK ("Version" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_HomepageSections_SchemaVersion')
    THEN
        ALTER TABLE "HomepageSections"
            ADD CONSTRAINT "CK_HomepageSections_SchemaVersion" CHECK ("SchemaVersion" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_HomepageSections_State')
    THEN
        ALTER TABLE "HomepageSections"
            ADD CONSTRAINT "CK_HomepageSections_State" CHECK (NOT ("IsDraft" AND "IsPublished"));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_HomepageSections_PublishedAt')
    THEN
        ALTER TABLE "HomepageSections"
            ADD CONSTRAINT "CK_HomepageSections_PublishedAt" CHECK (NOT "IsPublished" OR "PublishedAt" IS NOT NULL);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_HomepageSections_DraftGroupId')
    THEN
        ALTER TABLE "HomepageSections"
            ADD CONSTRAINT "CK_HomepageSections_DraftGroupId" CHECK (NOT "IsDraft" OR "DraftGroupId" IS NOT NULL);
    END IF;
END $$;

COMMIT;
