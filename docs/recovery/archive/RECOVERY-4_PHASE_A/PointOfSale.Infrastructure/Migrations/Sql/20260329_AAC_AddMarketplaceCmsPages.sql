BEGIN;

CREATE TABLE IF NOT EXISTS "MarketplaceCmsPages"
(
    "Id" uuid NOT NULL,
    "PageGroupId" uuid NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Content" text NOT NULL,
    "ContentType" character varying(20) NOT NULL,
    "Version" integer NOT NULL DEFAULT 1,
    "IsDraft" boolean NOT NULL DEFAULT FALSE,
    "IsPublished" boolean NOT NULL DEFAULT FALSE,
    "PublishedAt" timestamp with time zone NULL,
    "MetaTitle" character varying(200) NULL,
    "MetaDescription" character varying(320) NULL,
    "OpenGraphImageUrl" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_MarketplaceCmsPages" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCmsPages_PageGroupId_Version"
    ON "MarketplaceCmsPages" ("PageGroupId", "Version");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCmsPages_PageGroupId_Draft"
    ON "MarketplaceCmsPages" ("PageGroupId")
    WHERE "IsDraft" = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCmsPages_PageGroupId_Published"
    ON "MarketplaceCmsPages" ("PageGroupId")
    WHERE "IsPublished" = TRUE;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceCmsPages_PageGroupId"
    ON "MarketplaceCmsPages" ("PageGroupId");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceCmsPages_IsPublished_Slug"
    ON "MarketplaceCmsPages" ("IsPublished", "Slug");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceCmsPages_IsDraft_Slug"
    ON "MarketplaceCmsPages" ("IsDraft", "Slug");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCmsPages_Draft_Slug"
    ON "MarketplaceCmsPages" ("Slug")
    WHERE "IsDraft" = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCmsPages_Published_Slug"
    ON "MarketplaceCmsPages" ("Slug")
    WHERE "IsPublished" = TRUE;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceCmsPages_Version')
    THEN
        ALTER TABLE "MarketplaceCmsPages"
            ADD CONSTRAINT "CK_MarketplaceCmsPages_Version" CHECK ("Version" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceCmsPages_State')
    THEN
        ALTER TABLE "MarketplaceCmsPages"
            ADD CONSTRAINT "CK_MarketplaceCmsPages_State" CHECK (NOT ("IsDraft" AND "IsPublished"));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceCmsPages_PublishedAt')
    THEN
        ALTER TABLE "MarketplaceCmsPages"
            ADD CONSTRAINT "CK_MarketplaceCmsPages_PublishedAt" CHECK (NOT "IsPublished" OR "PublishedAt" IS NOT NULL);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceCmsPages_ContentType')
    THEN
        ALTER TABLE "MarketplaceCmsPages"
            ADD CONSTRAINT "CK_MarketplaceCmsPages_ContentType" CHECK ("ContentType" IN ('RichText', 'Blocks'));
    END IF;
END $$;

COMMIT;
