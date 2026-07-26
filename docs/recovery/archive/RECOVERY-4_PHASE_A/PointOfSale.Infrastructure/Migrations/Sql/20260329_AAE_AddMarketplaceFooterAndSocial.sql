BEGIN;

CREATE TABLE IF NOT EXISTS "MarketplaceFooterSections"
(
    "Id" uuid NOT NULL,
    "Title" character varying(120) NOT NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_MarketplaceFooterSections" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "MarketplaceFooterLinks"
(
    "Id" uuid NOT NULL,
    "SectionId" uuid NOT NULL,
    "Label" character varying(160) NOT NULL,
    "LinkType" character varying(20) NOT NULL,
    "Url" character varying(1000) NULL,
    "CmsPageSlug" character varying(160) NULL,
    "OpenInNewTab" boolean NOT NULL DEFAULT FALSE,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_MarketplaceFooterLinks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MarketplaceFooterLinks_MarketplaceFooterSections_SectionId"
        FOREIGN KEY ("SectionId") REFERENCES "MarketplaceFooterSections" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "MarketplaceSocialLinks"
(
    "Id" uuid NOT NULL,
    "Platform" character varying(40) NOT NULL,
    "Url" character varying(1000) NOT NULL,
    "Icon" character varying(80) NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_MarketplaceSocialLinks" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_MarketplaceFooterSections_SortOrder"
    ON "MarketplaceFooterSections" ("SortOrder");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceFooterLinks_SectionId_SortOrder"
    ON "MarketplaceFooterLinks" ("SectionId", "SortOrder");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceSocialLinks_SortOrder"
    ON "MarketplaceSocialLinks" ("SortOrder");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceSocialLinks_Platform"
    ON "MarketplaceSocialLinks" ("Platform");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceFooterLinks_LinkType')
    THEN
        ALTER TABLE "MarketplaceFooterLinks"
            ADD CONSTRAINT "CK_MarketplaceFooterLinks_LinkType"
            CHECK ("LinkType" IN ('Internal', 'External', 'Cms'));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_MarketplaceFooterLinks_Target')
    THEN
        ALTER TABLE "MarketplaceFooterLinks"
            ADD CONSTRAINT "CK_MarketplaceFooterLinks_Target"
            CHECK (
                ("LinkType" = 'Cms' AND "CmsPageSlug" IS NOT NULL AND LENGTH(TRIM("CmsPageSlug")) > 0) OR
                ("LinkType" <> 'Cms' AND "Url" IS NOT NULL AND LENGTH(TRIM("Url")) > 0)
            );
    END IF;
END $$;

COMMIT;
