-- ═══════════════════════════════════════════════════════════════════
-- AF-30A FIX D-2: Enabled Policies Must Have Category Assigned
-- ═══════════════════════════════════════════════════════════════════
-- 
-- Purpose: Enforces at the database level that when a product's
--          affiliate marketing is enabled (IsEnabled = true),
--          an AffiliateCategoryId MUST be assigned.
--
-- Date: 2026-02-27
-- ═══════════════════════════════════════════════════════════════════

-- 1. Show violating rows (enabled without category)
SELECT "Id", "TenantId", "ProductId", "IsEnabled", "AffiliateCategoryId"
FROM "AffiliateProductPolicies"
WHERE "IsEnabled" = true AND "AffiliateCategoryId" IS NULL;

-- 2. Fix violations: disable policies that have no category assigned
UPDATE "AffiliateProductPolicies"
SET "IsEnabled" = false,
    "UpdatedAt" = NOW()
WHERE "IsEnabled" = true AND "AffiliateCategoryId" IS NULL;

-- 3. Add the CHECK constraint (now safe — no violations remain)
ALTER TABLE "AffiliateProductPolicies"
ADD CONSTRAINT "CK_AffiliateProductPolicies_EnabledRequiresCategory"
CHECK (NOT ("IsEnabled" = true AND "AffiliateCategoryId" IS NULL));

-- 4. Record migration in EF Core history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260227125054_AF30A_FIX_D2_EnabledRequiresCategory', '9.0.1')
ON CONFLICT DO NOTHING;
