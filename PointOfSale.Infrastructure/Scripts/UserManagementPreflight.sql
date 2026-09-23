-- =====================================================================
-- Shopixy Users Management (Waves 1-3) — migration preflight, READ-ONLY.
-- Blueprint §12.1. Run against a production-equivalent database BEFORE
-- applying migration PosUserManagementAuthority.
--
-- STOP on ANY returned row and report the exact blocker. Do NOT silently
-- repair production data: no renames, no deletes, no ad hoc corrections.
-- The migration itself re-runs these guards fail-closed and aborts with
-- USER_MGMT_PREFLIGHT_BLOCKER if a violation exists at deploy time.
--
-- NOTE: UserRole.Owner is stored as integer 0 (first enum member).
-- Confirm against PointOfSale.Core/Enums/UserRole.cs before running.
-- =====================================================================

-- 0. Identity normalizer compatibility (must return zero rows).
-- PostgreSQL and .NET normalization are exactly equivalent for printable ASCII.
SELECT "Id", "Username", "Email"
FROM "Users"
WHERE "Username" !~ '^[ -~]+$'
   OR "Email" !~ '^[ -~]+$'
   OR btrim("Username") = ''
   OR btrim("Email") = '';

-- 1. Case-insensitive username collisions (must return zero rows)
SELECT upper(btrim("Username")) AS normalized_username, count(*)
FROM "Users"
GROUP BY upper(btrim("Username"))
HAVING count(*) > 1;

-- 2. Case-insensitive email collisions (must return zero rows)
SELECT upper(btrim("Email")) AS normalized_email, count(*)
FROM "Users"
GROUP BY upper(btrim("Email"))
HAVING count(*) > 1;

-- 3. Orphan TenantId values (must return zero rows)
SELECT u."Id", u."TenantId"
FROM "Users" u
LEFT JOIN "Tenants" t ON t."Id" = u."TenantId"
WHERE t."Id" IS NULL;

-- 4. Owner cardinality: exactly one non-deleted Owner per tenant
-- (must return zero rows)
SELECT "TenantId", count(*)
FROM "Users"
WHERE NOT "IsDeleted" AND "Role" = 0
GROUP BY "TenantId"
HAVING count(*) <> 1;

-- 5. Tenants with no Owner row at all (must return zero rows; blocker)
SELECT t."Id" AS tenant_id, t."Name"
FROM "Tenants" t
WHERE NOT EXISTS (
    SELECT 1 FROM "Users" u
    WHERE u."TenantId" = t."Id" AND NOT u."IsDeleted" AND u."Role" = 0
);
