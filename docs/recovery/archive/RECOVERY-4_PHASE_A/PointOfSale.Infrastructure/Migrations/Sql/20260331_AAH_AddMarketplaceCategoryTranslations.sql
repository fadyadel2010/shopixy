BEGIN;

-- ============================================================================
-- Marketplace Category Translations
-- Marketplace-only translation overlay for POS-managed categories.
-- Safe to run from pgAdmin 4.
-- ============================================================================

DO $$
BEGIN
    IF to_regclass('public."Categories"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."Categories" was not found.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "MarketplaceCategoryTranslations"
(
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Language" character varying(5) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Slug" character varying(180) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_MarketplaceCategoryTranslations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MarketplaceCategoryTranslations_Categories_CategoryId"
        FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCategoryTranslations_CategoryId_Language"
    ON "MarketplaceCategoryTranslations" ("CategoryId", "Language");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceCategoryTranslations_Language"
    ON "MarketplaceCategoryTranslations" ("Language");

COMMIT;
