BEGIN;

-- ============================================================================
-- CommerceHub / POS category separation
--
-- Purpose
--   1. Normalize CommerceHubCategories schema to the current app model
--      (CreatedAtUtc / UpdatedAtUtc).
--   2. Detect legacy shared/global rows that still live inside Categories.
--   3. Back up every affected category and product row before any mutation.
--   4. Clone tenant-safe POS copies for any Products that still reference the
--      legacy shared rows.
--   5. Move the legacy shared rows into CommerceHubCategories.
--   6. Delete only the old Categories rows that are no longer referenced.
--
-- Notes
--   - This script is designed for PgAdmin / PostgreSQL and is rerunnable.
--   - If you already know some Guid.Empty tenant rows are actually POS rows
--     that should remain tenant-owned, fix them first with:
--       PointOfSale.Infrastructure/Migrations/Sql/20260320_RecoverTenantCategories.sql
--   - Backup tables are persistent and intentionally kept after COMMIT.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. Ensure global CommerceHub tables exist in the current shape.
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS "CommerceHubCategories"
(
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200) NULL,
    "Slug" character varying(200) NOT NULL,
    "ParentId" uuid NULL,
    "Level" integer NOT NULL DEFAULT 0,
    "IconEmoji" character varying(10) NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" timestamp with time zone NULL,
    CONSTRAINT "PK_CommerceHubCategories" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubCategoryAttributes"
(
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200) NULL,
    "Type" character varying(20) NOT NULL,
    "IsRequired" boolean NOT NULL DEFAULT false,
    "IsFilterable" boolean NOT NULL DEFAULT true,
    "IsSearchable" boolean NOT NULL DEFAULT true,
    "OptionsJson" text NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    CONSTRAINT "PK_CommerceHubCategoryAttributes" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'CommerceHubCategories'
          AND column_name = 'CreatedAt'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'CommerceHubCategories'
          AND column_name = 'CreatedAtUtc'
    )
    THEN
        ALTER TABLE "CommerceHubCategories"
            RENAME COLUMN "CreatedAt" TO "CreatedAtUtc";
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'CommerceHubCategories'
          AND column_name = 'UpdatedAt'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'CommerceHubCategories'
          AND column_name = 'UpdatedAtUtc'
    )
    THEN
        ALTER TABLE "CommerceHubCategories"
            RENAME COLUMN "UpdatedAt" TO "UpdatedAtUtc";
    END IF;
END $$;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Name" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "NameAr" character varying(200) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Slug" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "ParentId" uuid NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Level" integer NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "IconEmoji" character varying(10) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW();

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamp with time zone NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "CategoryId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "Name" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "NameAr" character varying(200) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "Type" character varying(20) NOT NULL DEFAULT 'text';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsRequired" boolean NOT NULL DEFAULT false;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsFilterable" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsSearchable" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "OptionsJson" text NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubCategories_CommerceHubCategories_ParentId'
    )
    THEN
        ALTER TABLE "CommerceHubCategories"
            ADD CONSTRAINT "FK_CommerceHubCategories_CommerceHubCategories_ParentId"
            FOREIGN KEY ("ParentId")
            REFERENCES "CommerceHubCategories" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubCategoryAttributes_CommerceHubCategories_CategoryId'
    )
    THEN
        ALTER TABLE "CommerceHubCategoryAttributes"
            ADD CONSTRAINT "FK_CommerceHubCategoryAttributes_CommerceHubCategories_CategoryId"
            FOREIGN KEY ("CategoryId")
            REFERENCES "CommerceHubCategories" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategories_ParentId_SortOrder"
    ON "CommerceHubCategories" ("ParentId", "SortOrder");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CommerceHubCategories_Slug"
    ON "CommerceHubCategories" ("Slug");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder"
    ON "CommerceHubCategoryAttributes" ("CategoryId", "SortOrder");

-- ----------------------------------------------------------------------------
-- 2. Persistent backups and migration bookkeeping.
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS "__CommerceHubCategorySeparation_Backup_Categories"
AS
SELECT *
FROM "Categories"
WITH NO DATA;

CREATE TABLE IF NOT EXISTS "__CommerceHubCategorySeparation_Backup_Products"
AS
SELECT *
FROM "Products"
WITH NO DATA;

CREATE TABLE IF NOT EXISTS "__CommerceHubCategorySeparation_CloneMap"
(
    "LegacyCategoryId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ClonedCategoryId" uuid NOT NULL,
    "MappedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK___CommerceHubCategorySeparation_CloneMap"
        PRIMARY KEY ("LegacyCategoryId", "TenantId")
);

CREATE TABLE IF NOT EXISTS "__CommerceHubCategorySeparation_MigratedCategories"
(
    "LegacyCategoryId" uuid NOT NULL,
    "DetectedReason" text NOT NULL,
    "MigratedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK___CommerceHubCategorySeparation_MigratedCategories"
        PRIMARY KEY ("LegacyCategoryId")
);

-- ----------------------------------------------------------------------------
-- 3. Detect legacy shared/global categories still stored in Categories.
-- ----------------------------------------------------------------------------
-- If you know a specific legacy "global tenant" GUID was used in older data,
-- add it to the WHERE block below next to Guid.Empty.

CREATE TEMP TABLE "_candidate_category_reasons" ON COMMIT DROP AS
WITH multi_tenant_product_refs AS
(
    SELECT p."CategoryId"
    FROM "Products" p
    WHERE COALESCE(p."IsDeleted", FALSE) = FALSE
    GROUP BY p."CategoryId"
    HAVING COUNT(DISTINCT p."TenantId") > 1
),
tenant_mismatch_product_refs AS
(
    SELECT DISTINCT c."Id" AS "CategoryId"
    FROM "Categories" c
    INNER JOIN "Products" p
        ON p."CategoryId" = c."Id"
    WHERE COALESCE(c."IsDeleted", FALSE) = FALSE
      AND COALESCE(p."IsDeleted", FALSE) = FALSE
      AND c."TenantId" IS NOT NULL
      AND c."TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid
      AND p."TenantId" <> c."TenantId"
)
SELECT
    c."Id",
    CONCAT_WS(
        '; ',
        CASE WHEN c."TenantId" IS NULL THEN 'TenantId is NULL' END,
        CASE WHEN c."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid THEN 'TenantId is Guid.Empty' END,
        CASE WHEN m."CategoryId" IS NOT NULL THEN 'Category is referenced by products from multiple tenants' END,
        CASE WHEN x."CategoryId" IS NOT NULL THEN 'Category tenant does not match product tenant' END
    ) AS "DetectedReason"
FROM "Categories" c
LEFT JOIN multi_tenant_product_refs m
    ON m."CategoryId" = c."Id"
LEFT JOIN tenant_mismatch_product_refs x
    ON x."CategoryId" = c."Id"
WHERE COALESCE(c."IsDeleted", FALSE) = FALSE
  AND
  (
      c."TenantId" IS NULL
      OR c."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid
      OR m."CategoryId" IS NOT NULL
      OR x."CategoryId" IS NOT NULL
  );

CREATE TEMP TABLE "_candidate_categories" ON COMMIT DROP AS
SELECT
    c.*,
    r."DetectedReason"
FROM "Categories" c
INNER JOIN "_candidate_category_reasons" r
    ON r."Id" = c."Id";

-- ----------------------------------------------------------------------------
-- 4. Back up every affected row before mutation.
-- ----------------------------------------------------------------------------

INSERT INTO "__CommerceHubCategorySeparation_Backup_Categories"
SELECT source_categories.*
FROM "Categories" source_categories
INNER JOIN "_candidate_categories" c
    ON c."Id" = source_categories."Id"
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__CommerceHubCategorySeparation_Backup_Categories" b
    WHERE b."Id" = source_categories."Id"
);

INSERT INTO "__CommerceHubCategorySeparation_Backup_Products"
SELECT p.*
FROM "Products" p
INNER JOIN "_candidate_categories" c
    ON c."Id" = p."CategoryId"
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__CommerceHubCategorySeparation_Backup_Products" b
    WHERE b."Id" = p."Id"
);

INSERT INTO "__CommerceHubCategorySeparation_Backup_Categories"
SELECT child_categories.*
FROM "Categories" child_categories
INNER JOIN "_candidate_categories" legacy_parent
    ON legacy_parent."Id" = child_categories."ParentCategoryId"
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__CommerceHubCategorySeparation_Backup_Categories" b
    WHERE b."Id" = child_categories."Id"
);

INSERT INTO "__CommerceHubCategorySeparation_MigratedCategories"
(
    "LegacyCategoryId",
    "DetectedReason"
)
SELECT
    c."Id",
    COALESCE(NULLIF(c."DetectedReason", ''), 'Detected by migration heuristics')
FROM "_candidate_categories" c
ON CONFLICT ("LegacyCategoryId") DO UPDATE
SET
    "DetectedReason" = EXCLUDED."DetectedReason";

-- ----------------------------------------------------------------------------
-- 5. Create tenant-safe POS clones for every Product still pointing at a
--    legacy shared category. Ancestor categories in the same legacy tree are
--    cloned too, so tenant POS hierarchy stays valid.
-- ----------------------------------------------------------------------------

WITH RECURSIVE tenant_category_tree AS
(
    SELECT DISTINCT
        p."TenantId",
        c."Id" AS "LegacyCategoryId",
        c."ParentCategoryId"
    FROM "Products" p
    INNER JOIN "_candidate_categories" c
        ON c."Id" = p."CategoryId"
    WHERE COALESCE(p."IsDeleted", FALSE) = FALSE

    UNION

    SELECT DISTINCT
        child."TenantId",
        legacy_parent."Id" AS "LegacyCategoryId",
        legacy_parent."ParentCategoryId"
    FROM "Categories" child
    INNER JOIN "_candidate_categories" legacy_parent
        ON legacy_parent."Id" = child."ParentCategoryId"
    WHERE COALESCE(child."IsDeleted", FALSE) = FALSE
      AND child."TenantId" IS NOT NULL
      AND child."TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid

    UNION

    SELECT
        tree."TenantId",
        parent."Id" AS "LegacyCategoryId",
        parent."ParentCategoryId"
    FROM tenant_category_tree tree
    INNER JOIN "_candidate_categories" parent
        ON parent."Id" = tree."ParentCategoryId"
),
dedup AS
(
    SELECT DISTINCT
        tree."LegacyCategoryId",
        tree."TenantId"
    FROM tenant_category_tree tree
),
hashed AS
(
    SELECT
        d."LegacyCategoryId",
        d."TenantId",
        MD5(d."TenantId"::text || ':' || d."LegacyCategoryId"::text || ':tenant-pos-category') AS "Hash"
    FROM dedup d
)
INSERT INTO "__CommerceHubCategorySeparation_CloneMap"
(
    "LegacyCategoryId",
    "TenantId",
    "ClonedCategoryId"
)
SELECT
    h."LegacyCategoryId",
    h."TenantId",
    (
        SUBSTRING(h."Hash", 1, 8) || '-' ||
        SUBSTRING(h."Hash", 9, 4) || '-' ||
        SUBSTRING(h."Hash", 13, 4) || '-' ||
        SUBSTRING(h."Hash", 17, 4) || '-' ||
        SUBSTRING(h."Hash", 21, 12)
    )::uuid AS "ClonedCategoryId"
FROM hashed h
ON CONFLICT ("LegacyCategoryId", "TenantId") DO NOTHING;

INSERT INTO "Categories"
(
    "Id",
    "TenantId",
    "Name",
    "Description",
    "ParentCategoryId",
    "CreatedAt",
    "UpdatedAt",
    "CreatedBy",
    "UpdatedBy",
    "IsDeleted"
)
SELECT
    map."ClonedCategoryId" AS "Id",
    map."TenantId",
    c."Name",
    c."Description",
    parent_map."ClonedCategoryId" AS "ParentCategoryId",
    COALESCE(c."CreatedAt", NOW()) AS "CreatedAt",
    NOW() AS "UpdatedAt",
    c."CreatedBy",
    c."UpdatedBy",
    FALSE AS "IsDeleted"
FROM "__CommerceHubCategorySeparation_CloneMap" map
INNER JOIN "_candidate_categories" c
    ON c."Id" = map."LegacyCategoryId"
LEFT JOIN "__CommerceHubCategorySeparation_CloneMap" parent_map
    ON parent_map."LegacyCategoryId" = c."ParentCategoryId"
   AND parent_map."TenantId" = map."TenantId"
WHERE NOT EXISTS
(
    SELECT 1
    FROM "Categories" existing
    WHERE existing."Id" = map."ClonedCategoryId"
);

UPDATE "Products" p
SET
    "CategoryId" = map."ClonedCategoryId",
    "UpdatedAt" = NOW()
FROM "__CommerceHubCategorySeparation_CloneMap" map
WHERE p."CategoryId" = map."LegacyCategoryId"
  AND p."TenantId" = map."TenantId"
  AND p."CategoryId" <> map."ClonedCategoryId";

UPDATE "Categories" child
SET
    "ParentCategoryId" = map."ClonedCategoryId",
    "UpdatedAt" = NOW()
FROM "__CommerceHubCategorySeparation_CloneMap" map
WHERE child."ParentCategoryId" = map."LegacyCategoryId"
  AND child."TenantId" = map."TenantId"
  AND child."Id" <> map."ClonedCategoryId";

-- ----------------------------------------------------------------------------
-- 6. Move the legacy shared rows into CommerceHubCategories.
-- ----------------------------------------------------------------------------

CREATE TEMP TABLE "_candidate_global_categories_stage" ON COMMIT DROP AS
WITH normalized_candidates AS
(
    SELECT
        c."Id",
        c."Name",
        NULL::character varying(200) AS "NameAr",
        CASE
            WHEN parent."Id" IS NOT NULL THEN c."ParentCategoryId"
            ELSE NULL
        END AS "ParentId",
        NULL::character varying(10) AS "IconEmoji",
        TRUE AS "IsActive",
        ROW_NUMBER() OVER
        (
            PARTITION BY
                CASE
                    WHEN parent."Id" IS NOT NULL THEN c."ParentCategoryId"
                    ELSE NULL
                END
            ORDER BY c."Name", c."Id"
        ) AS "SortOrder",
        COALESCE(c."CreatedAt", NOW()) AS "CreatedAtUtc",
        c."UpdatedAt" AS "UpdatedAtUtc",
        NULLIF(
            TRIM(BOTH '-' FROM REGEXP_REPLACE(
                LOWER(REGEXP_REPLACE(COALESCE(c."Name", 'category'), '[^a-zA-Z0-9]+', '-', 'g')),
                '-+',
                '-',
                'g'
            )),
            ''
        ) AS "BaseSlug"
    FROM "_candidate_categories" c
    LEFT JOIN "_candidate_categories" parent
        ON parent."Id" = c."ParentCategoryId"
),
ranked_slugs AS
(
    SELECT
        n.*,
        ROW_NUMBER() OVER (PARTITION BY COALESCE(n."BaseSlug", 'category') ORDER BY n."Id") AS "BaseSlugRank"
    FROM normalized_candidates n
),
prepared AS
(
    SELECT
        s."Id",
        s."Name",
        s."NameAr",
        s."ParentId",
        s."IconEmoji",
        s."IsActive",
        s."SortOrder",
        s."CreatedAtUtc",
        s."UpdatedAtUtc",
        CASE
            WHEN s."BaseSlug" IS NULL THEN 'category-' || LEFT(REPLACE(s."Id"::text, '-', ''), 8)
            WHEN s."BaseSlugRank" > 1 THEN s."BaseSlug" || '-' || LEFT(REPLACE(s."Id"::text, '-', ''), 8)
            WHEN EXISTS
            (
                SELECT 1
                FROM "CommerceHubCategories" existing
                WHERE existing."Slug" = s."BaseSlug"
                  AND existing."Id" <> s."Id"
            )
            THEN s."BaseSlug" || '-' || LEFT(REPLACE(s."Id"::text, '-', ''), 8)
            ELSE s."BaseSlug"
        END AS "Slug"
    FROM ranked_slugs s
),
hierarchy AS
(
    WITH RECURSIVE tree AS
    (
        SELECT
            p."Id",
            0 AS "Level"
        FROM prepared p
        WHERE p."ParentId" IS NULL

        UNION ALL

        SELECT
            child."Id",
            tree."Level" + 1 AS "Level"
        FROM prepared child
        INNER JOIN tree
            ON tree."Id" = child."ParentId"
    )
    SELECT
        t."Id",
        t."Level"
    FROM tree t
)
SELECT
    p."Id",
    p."Name",
    p."NameAr",
    p."Slug",
    p."ParentId",
    COALESCE(h."Level", 0) AS "Level",
    p."IconEmoji",
    p."IsActive",
    p."SortOrder",
    p."CreatedAtUtc",
    p."UpdatedAtUtc"
FROM prepared p
LEFT JOIN hierarchy h
    ON h."Id" = p."Id";

INSERT INTO "CommerceHubCategories"
(
    "Id",
    "Name",
    "NameAr",
    "Slug",
    "ParentId",
    "Level",
    "IconEmoji",
    "IsActive",
    "SortOrder",
    "CreatedAtUtc",
    "UpdatedAtUtc"
)
SELECT
    s."Id",
    s."Name",
    s."NameAr",
    s."Slug",
    s."ParentId",
    s."Level",
    s."IconEmoji",
    s."IsActive",
    s."SortOrder",
    s."CreatedAtUtc",
    s."UpdatedAtUtc"
FROM "_candidate_global_categories_stage" s
ORDER BY s."Level", s."SortOrder", s."Id"
ON CONFLICT ("Id") DO UPDATE
SET
    "Name" = EXCLUDED."Name",
    "NameAr" = EXCLUDED."NameAr",
    "Slug" = CASE
        WHEN COALESCE("CommerceHubCategories"."Slug", '') <> '' THEN "CommerceHubCategories"."Slug"
        ELSE EXCLUDED."Slug"
    END,
    "ParentId" = EXCLUDED."ParentId",
    "Level" = EXCLUDED."Level",
    "IconEmoji" = COALESCE("CommerceHubCategories"."IconEmoji", EXCLUDED."IconEmoji"),
    "IsActive" = EXCLUDED."IsActive",
    "SortOrder" = EXCLUDED."SortOrder",
    "CreatedAtUtc" = COALESCE("CommerceHubCategories"."CreatedAtUtc", EXCLUDED."CreatedAtUtc"),
    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";

-- ----------------------------------------------------------------------------
-- 7. Remove the old shared rows from Categories only after Products were moved.
-- ----------------------------------------------------------------------------

UPDATE "Categories" c
SET
    "ParentCategoryId" = NULL,
    "UpdatedAt" = NOW()
WHERE c."Id" IN
(
    SELECT candidate."Id"
    FROM "_candidate_categories" candidate
)
  AND c."ParentCategoryId" IS NOT NULL;

DELETE FROM "Categories" c
WHERE c."Id" IN
(
    SELECT candidate."Id"
    FROM "_candidate_categories" candidate
)
  AND NOT EXISTS
  (
      SELECT 1
      FROM "Products" p
      WHERE p."CategoryId" = c."Id"
        AND COALESCE(p."IsDeleted", FALSE) = FALSE
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM "Categories" child
      WHERE child."ParentCategoryId" = c."Id"
  );

-- ----------------------------------------------------------------------------
-- 8. Verification queries.
-- ----------------------------------------------------------------------------

SELECT
    COUNT(*) AS "DetectedLegacyCategoryCount"
FROM "_candidate_categories";

SELECT
    COALESCE(NULLIF("DetectedReason", ''), 'Detected by migration heuristics') AS "DetectedReason",
    COUNT(*) AS "Count"
FROM "_candidate_categories"
GROUP BY COALESCE(NULLIF("DetectedReason", ''), 'Detected by migration heuristics')
ORDER BY "Count" DESC, "DetectedReason";

SELECT
    COUNT(*) AS "TenantCloneCount"
FROM "__CommerceHubCategorySeparation_CloneMap";

SELECT
    COUNT(*) AS "RemainingProductRefsToLegacyCategoryRows"
FROM "Products" p
INNER JOIN "_candidate_categories" c
    ON c."Id" = p."CategoryId"
WHERE COALESCE(p."IsDeleted", FALSE) = FALSE;

SELECT
    c."Id",
    c."Name",
    c."TenantId",
    c."ParentCategoryId",
    c."CreatedAt",
    c."UpdatedAt"
FROM "Categories" c
INNER JOIN "_candidate_categories" legacy
    ON legacy."Id" = c."Id"
ORDER BY c."Name", c."Id";

SELECT
    g."Id",
    g."Name",
    g."Slug",
    g."ParentId",
    g."Level",
    g."CreatedAtUtc",
    g."UpdatedAtUtc"
FROM "CommerceHubCategories" g
INNER JOIN "_candidate_categories" legacy
    ON legacy."Id" = g."Id"
ORDER BY g."Level", g."SortOrder", g."Name";

COMMIT;
