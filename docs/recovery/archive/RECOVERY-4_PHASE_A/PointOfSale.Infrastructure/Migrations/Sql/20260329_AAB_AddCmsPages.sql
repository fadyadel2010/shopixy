BEGIN;

CREATE TABLE IF NOT EXISTS "CmsPages"
(
    "Id" uuid NOT NULL,
    "PageGroupId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Content" text NOT NULL,
    "ContentType" character varying(20) NOT NULL,
    "Version" integer NOT NULL DEFAULT 1,
    "SchemaVersion" integer NOT NULL DEFAULT 1,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "IsDraft" boolean NOT NULL DEFAULT FALSE,
    "IsPublished" boolean NOT NULL DEFAULT FALSE,
    "PublishedAt" timestamp with time zone NULL,
    "MetaTitle" character varying(200) NULL,
    "MetaDescription" character varying(320) NULL,
    "MetaKeywords" character varying(500) NULL,
    "OpenGraphImageUrl" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_CmsPages" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CmsPages_PageGroupId_Version"
    ON "CmsPages" ("PageGroupId", "Version");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CmsPages_PageGroupId_Draft"
    ON "CmsPages" ("PageGroupId")
    WHERE "IsDraft" = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CmsPages_PageGroupId_Published"
    ON "CmsPages" ("PageGroupId")
    WHERE "IsPublished" = TRUE;

CREATE INDEX IF NOT EXISTS "IX_CmsPages_PageGroupId"
    ON "CmsPages" ("PageGroupId");

CREATE INDEX IF NOT EXISTS "IX_CmsPages_TenantId_IsPublished_Slug"
    ON "CmsPages" ("TenantId", "IsPublished", "Slug");

CREATE INDEX IF NOT EXISTS "IX_CmsPages_TenantId_IsDraft_Slug"
    ON "CmsPages" ("TenantId", "IsDraft", "Slug");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CmsPages_Tenant_Draft_Slug"
    ON "CmsPages" ("TenantId", "Slug")
    WHERE "IsDraft" = TRUE
      AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CmsPages_Tenant_Published_Slug"
    ON "CmsPages" ("TenantId", "Slug")
    WHERE "IsPublished" = TRUE
      AND "IsDeleted" = FALSE;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CmsPages_Version')
    THEN
        ALTER TABLE "CmsPages"
            ADD CONSTRAINT "CK_CmsPages_Version" CHECK ("Version" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CmsPages_SchemaVersion')
    THEN
        ALTER TABLE "CmsPages"
            ADD CONSTRAINT "CK_CmsPages_SchemaVersion" CHECK ("SchemaVersion" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CmsPages_State')
    THEN
        ALTER TABLE "CmsPages"
            ADD CONSTRAINT "CK_CmsPages_State" CHECK (NOT ("IsDraft" AND "IsPublished"));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CmsPages_PublishedAt')
    THEN
        ALTER TABLE "CmsPages"
            ADD CONSTRAINT "CK_CmsPages_PublishedAt" CHECK (NOT "IsPublished" OR "PublishedAt" IS NOT NULL);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CmsPages_ContentType')
    THEN
        ALTER TABLE "CmsPages"
            ADD CONSTRAINT "CK_CmsPages_ContentType" CHECK ("ContentType" IN ('RichText', 'Blocks'));
    END IF;
END $$;

COMMIT;
