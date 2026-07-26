-- ============================================================
-- Migration: Add TitleAr column to StorefrontFooterSections
-- Purpose:   Support Arabic title localization for storefront footer sections
-- Date:      2026-04-01
-- Safe:      Yes — nullable column, no data migration needed
-- ============================================================

-- Add TitleAr column (nullable, matching Title's max length of 120)
ALTER TABLE "StorefrontFooterSections"
    ADD COLUMN IF NOT EXISTS "TitleAr" character varying(120) NULL;

-- Verify
-- SELECT "Id", "Title", "TitleAr" FROM "StorefrontFooterSections" LIMIT 5;
