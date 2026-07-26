-- ═══════════════════════════════════════════════════════════════════
-- AF-30B FIX: Add missing CreatedBy/UpdatedBy columns to AffiliateCategories
-- ═══════════════════════════════════════════════════════════════════
-- 
-- Root cause: AffiliateCategories table was created without the
-- CreatedBy and UpdatedBy columns that BaseEntity defines.
-- EF Core expects these columns when materializing AffiliateCategory entities.
--
-- Safe to run: YES (IF NOT EXISTS prevents errors if already present)
-- ═══════════════════════════════════════════════════════════════════

ALTER TABLE "AffiliateCategories"
ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "AffiliateCategories"
ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;
