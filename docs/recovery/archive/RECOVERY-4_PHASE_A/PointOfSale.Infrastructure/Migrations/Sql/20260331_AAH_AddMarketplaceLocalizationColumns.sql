BEGIN;

-- ============================================================================
-- Shopixy Marketplace Localization Columns
-- Adds localized JSONB payloads for marketplace CMS pages and footer content
-- while preserving the existing English-compatible flat fields.
-- Safe to run from pgAdmin 4.
-- ============================================================================

DO $$
BEGIN
    IF to_regclass('public."MarketplaceCmsPages"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."MarketplaceCmsPages" was not found.';
    END IF;

    IF to_regclass('public."MarketplaceFooterSections"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."MarketplaceFooterSections" was not found.';
    END IF;

    IF to_regclass('public."MarketplaceFooterLinks"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."MarketplaceFooterLinks" was not found.';
    END IF;
END $$;

ALTER TABLE "MarketplaceCmsPages"
    ADD COLUMN IF NOT EXISTS "TitleJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "ContentJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "MetaTitleJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "MetaDescriptionJson" jsonb NULL;

UPDATE "MarketplaceCmsPages"
SET
    "TitleJson" = jsonb_build_object('en', "Title"),
    "ContentJson" = CASE
        WHEN "ContentType" = 'RichText' THEN jsonb_build_object('en', COALESCE("Content", ''))
        ELSE jsonb_build_object('en', COALESCE("Content", '[]')::jsonb)
    END,
    "MetaTitleJson" = CASE
        WHEN "MetaTitle" IS NULL OR btrim("MetaTitle") = '' THEN NULL
        ELSE jsonb_build_object('en', "MetaTitle")
    END,
    "MetaDescriptionJson" = CASE
        WHEN "MetaDescription" IS NULL OR btrim("MetaDescription") = '' THEN NULL
        ELSE jsonb_build_object('en', "MetaDescription")
    END
WHERE ("TitleJson" IS NULL OR "TitleJson" = 'null'::jsonb)
   OR ("ContentJson" IS NULL OR "ContentJson" = 'null'::jsonb)
   OR ("MetaTitleJson" IS NULL OR "MetaTitleJson" = 'null'::jsonb)
   OR ("MetaDescriptionJson" IS NULL OR "MetaDescriptionJson" = 'null'::jsonb);

ALTER TABLE "MarketplaceFooterSections"
    ADD COLUMN IF NOT EXISTS "TitleJson" jsonb NULL;

UPDATE "MarketplaceFooterSections"
SET "TitleJson" = jsonb_build_object('en', "Title")
WHERE "TitleJson" IS NULL OR "TitleJson" = 'null'::jsonb;

ALTER TABLE "MarketplaceFooterLinks"
    ADD COLUMN IF NOT EXISTS "LabelJson" jsonb NULL;

UPDATE "MarketplaceFooterLinks"
SET "LabelJson" = jsonb_build_object('en', "Label")
WHERE "LabelJson" IS NULL OR "LabelJson" = 'null'::jsonb;

COMMIT;
