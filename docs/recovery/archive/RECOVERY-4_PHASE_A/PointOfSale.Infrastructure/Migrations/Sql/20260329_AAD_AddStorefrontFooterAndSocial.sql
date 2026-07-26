BEGIN;

CREATE TABLE IF NOT EXISTS "StorefrontFooterSections"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NULL,
    "Title" character varying(120) NOT NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_StorefrontFooterSections" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "StorefrontFooterLinks"
(
    "Id" uuid NOT NULL,
    "SectionId" uuid NOT NULL,
    "Label" character varying(160) NOT NULL,
    "LabelAr" character varying(160) NULL,
    "LinkType" character varying(20) NOT NULL,
    "Url" character varying(1000) NULL,
    "CmsPageSlug" character varying(160) NULL,
    "OpenInNewTab" boolean NOT NULL DEFAULT FALSE,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_StorefrontFooterLinks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_StorefrontFooterLinks_StorefrontFooterSections_SectionId"
        FOREIGN KEY ("SectionId") REFERENCES "StorefrontFooterSections" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "StorefrontSocialLinks"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NULL,
    "Platform" character varying(40) NOT NULL,
    "Url" character varying(1000) NOT NULL,
    "Icon" character varying(80) NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_StorefrontSocialLinks" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_StorefrontFooterSections_TenantId_SortOrder"
    ON "StorefrontFooterSections" ("TenantId", "SortOrder");

CREATE INDEX IF NOT EXISTS "IX_StorefrontFooterLinks_SectionId_SortOrder"
    ON "StorefrontFooterLinks" ("SectionId", "SortOrder");

CREATE INDEX IF NOT EXISTS "IX_StorefrontSocialLinks_TenantId_SortOrder"
    ON "StorefrontSocialLinks" ("TenantId", "SortOrder");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_StorefrontSocialLinks_TenantId_Platform"
    ON "StorefrontSocialLinks" ("TenantId", "Platform")
    WHERE "TenantId" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_StorefrontSocialLinks_Global_Platform"
    ON "StorefrontSocialLinks" ("Platform")
    WHERE "TenantId" IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_StorefrontFooterLinks_LinkType')
    THEN
        ALTER TABLE "StorefrontFooterLinks"
            ADD CONSTRAINT "CK_StorefrontFooterLinks_LinkType"
            CHECK ("LinkType" IN ('Internal', 'External', 'Cms'));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_StorefrontFooterLinks_Target')
    THEN
        ALTER TABLE "StorefrontFooterLinks"
            ADD CONSTRAINT "CK_StorefrontFooterLinks_Target"
            CHECK (
                ("LinkType" = 'Cms' AND "CmsPageSlug" IS NOT NULL AND LENGTH(TRIM("CmsPageSlug")) > 0) OR
                ("LinkType" <> 'Cms' AND "Url" IS NOT NULL AND LENGTH(TRIM("Url")) > 0)
            );
    END IF;
END $$;

COMMIT;
