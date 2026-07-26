-- Migration: S34_DataAuthorityBoundaryHardening
-- Purpose:
--   1. Make CommerceHub listing prices strictly POS-variant authoritative.
--   2. Tenant-scope canonical identity uniqueness and backfill historical subjects.
-- Database: PostgreSQL
-- Note: For SQL Server, adapt filter syntax (use square brackets and 0 for false)

BEGIN;

UPDATE "CommerceHubListings"
SET "CommerceHubPrice" = NULL,
    "UpdatedAt" = COALESCE("UpdatedAt", NOW())
WHERE "CommerceHubPrice" IS NOT NULL;

DROP INDEX IF EXISTS "UX_CanonicalIdentityLinks_Subject_Active";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Tenant_Subject_Active"
    ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId")
    WHERE "TenantId" IS NOT NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active"
    ON "CanonicalIdentityLinks" ("SubjectType", "SubjectId")
    WHERE "TenantId" IS NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:PosUser:' || "Id"::text)::uuid,
       COALESCE(NULLIF("Id", '00000000-0000-0000-0000-000000000000'::uuid), md5('CanonicalIdentity:PosUser:' || "Id"::text)::uuid),
       0,
       "Id",
       NULLIF("TenantId", '00000000-0000-0000-0000-000000000000'::uuid),
       'POS',
       NULLIF(COALESCE(NULLIF("Email", ''), NULLIF("Username", '')), ''),
       NOW(),
       NOW(),
       false
FROM "Users"
WHERE "Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND COALESCE("IsDeleted", false) = false
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:PosCustomer:' || "Id"::text)::uuid,
       "Id",
       1,
       "Id",
       NULLIF("TenantId", '00000000-0000-0000-0000-000000000000'::uuid),
       'POS',
       NULLIF(COALESCE(NULLIF("Email", ''), NULLIF("Phone", ''), NULLIF("Name", '')), ''),
       NOW(),
       NOW(),
       false
FROM "Customers"
WHERE "Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND COALESCE("IsDeleted", false) = false
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:OnlineCustomer:' || oc."Id"::text)::uuid,
       COALESCE((
           SELECT o."CustomerId"
           FROM "Orders" o
           WHERE o."OnlineCustomerId" = oc."Id"
             AND o."CustomerId" IS NOT NULL
           ORDER BY o."CreatedAt"
           LIMIT 1
       ), oc."Id"),
       2,
       oc."Id",
       NULLIF(oc."TenantId", '00000000-0000-0000-0000-000000000000'::uuid),
       'Storefront',
       NULLIF(oc."Email", ''),
       NOW(),
       NOW(),
       false
FROM "OnlineCustomers" oc
WHERE oc."Id" <> '00000000-0000-0000-0000-000000000000'::uuid
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:CommerceHubCustomer:' || c."Id"::text)::uuid,
       c."Id",
       3,
       c."Id",
       NULL,
       'CommerceHub',
       NULLIF(c."Email", ''),
       NOW(),
       NOW(),
       false
FROM "CommerceHubCustomers" c
WHERE c."Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND COALESCE(c."IsDeleted", false) = false
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:AffiliateAccount:' || a."Id"::text)::uuid,
       COALESCE(NULLIF(a."UserId", '00000000-0000-0000-0000-000000000000'::uuid), a."Id"),
       4,
       a."Id",
       NULLIF(a."TenantId", '00000000-0000-0000-0000-000000000000'::uuid),
       'Affiliate',
       NULLIF(COALESCE(NULLIF(a."ReferralCode", ''), NULLIF(a."DisplayName", '')), ''),
       NOW(),
       NOW(),
       false
FROM "AffiliateAccounts" a
WHERE a."Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND COALESCE(a."IsDeleted", false) = false
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:AffiliateUser:' || u."Id"::text)::uuid,
       u."AffiliateAccountId",
       5,
       u."Id",
       NULLIF(u."TenantId", '00000000-0000-0000-0000-000000000000'::uuid),
       'Affiliate',
       NULLIF(u."Email", ''),
       NOW(),
       NOW(),
       false
FROM "AffiliateUsers" u
WHERE u."Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND u."AffiliateAccountId" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND COALESCE(u."IsDeleted", false) = false
ON CONFLICT DO NOTHING;

INSERT INTO "CanonicalIdentityLinks"
("Id", "CanonicalIdentityId", "SubjectType", "SubjectId", "TenantId", "SourceSystem", "DisplayKey", "LinkedAtUtc", "CreatedAt", "IsDeleted")
SELECT md5('CanonicalIdentityLink:StoreTenant:' || t."Id"::text)::uuid,
       t."Id",
       6,
       t."Id",
       t."Id",
       'POS',
       NULLIF(COALESCE(NULLIF(t."Slug", ''), NULLIF(t."ContactEmail", ''), NULLIF(t."Name", '')), ''),
       NOW(),
       NOW(),
       false
FROM "Tenants" t
WHERE t."Id" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND t."DeletedAtUtc" IS NULL
ON CONFLICT DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424180000_S34_DataAuthorityBoundaryHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424180000_S34_DataAuthorityBoundaryHardening'
);

COMMIT;
