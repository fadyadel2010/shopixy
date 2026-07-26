BEGIN;

-- ============================================================================
-- POS Tenant Category Recovery
-- Diagnose and repair tenant-owned POS categories after tenant isolation
-- hardening exposed legacy rows with missing or placeholder tenant assignment.
-- Review results first, then uncomment only the UPDATE block you need.
-- ============================================================================

-- 1. Inspect recent category rows
SELECT "Id", "Name", "TenantId", "ParentCategoryId", "CreatedAt", "UpdatedAt"
FROM "Categories"
ORDER BY "CreatedAt" DESC;

-- 2. Inspect tenant distribution.
-- NOTE: Guid.Empty usually means the row existed before tenant hardening and
-- was backfilled with the migration default instead of the real tenant.
SELECT "TenantId", COUNT(*) AS "Count"
FROM "Categories"
GROUP BY "TenantId"
ORDER BY "Count" DESC;

-- 3. Optional targeted inspection for legacy placeholder rows
SELECT "Id", "Name", "TenantId", "ParentCategoryId", "CreatedAt"
FROM "Categories"
WHERE "TenantId" = '00000000-0000-0000-0000-000000000000'::uuid
ORDER BY "CreatedAt" DESC;

-- 4. Recovery option A: assign placeholder rows to the correct tenant
-- UPDATE "Categories"
-- SET "TenantId" = '<correct-tenant-id>'::uuid
-- WHERE "TenantId" = '00000000-0000-0000-0000-000000000000'::uuid;

-- 5. Recovery option B: move specific rows from a wrong tenant to the correct one
-- UPDATE "Categories"
-- SET "TenantId" = '<correct-tenant-id>'::uuid
-- WHERE "Id" IN (
--     '<category-id-1>'::uuid,
--     '<category-id-2>'::uuid
-- );

COMMIT;
