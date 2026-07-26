BEGIN;

-- ============================================================================
-- Safe auto-variant backfill for legacy products
--
-- Purpose
--   1. Find active products that still have no active ProductVariants rows.
--   2. Back up the affected Products and WarehouseStocks rows.
--   3. Create one deterministic default variant per missing product.
--   4. Mirror legacy WarehouseStocks rows into ProductVariantStocks.
--   5. Link WarehouseStocks.ProductVariantId to the default variant for simple
--      products so old and new read paths point to the same SKU row.
--
-- Notes
--   - This script is idempotent and safe to re-run.
--   - It does not delete or overwrite custom variants.
--   - It does not force-rewrite products that already have variants.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. Persistent backups and action log
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS "__DefaultVariantBackfill_Backup_Products"
AS
SELECT *
FROM "Products"
WITH NO DATA;

CREATE TABLE IF NOT EXISTS "__DefaultVariantBackfill_Backup_WarehouseStocks"
AS
SELECT *
FROM "WarehouseStocks"
WITH NO DATA;

CREATE TABLE IF NOT EXISTS "__DefaultVariantBackfill_CreatedVariants"
(
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK___DefaultVariantBackfill_CreatedVariants"
        PRIMARY KEY ("ProductId")
);

-- ----------------------------------------------------------------------------
-- 2. Detect products that still have no active variants
-- ----------------------------------------------------------------------------

CREATE TEMP TABLE "_products_without_variants" ON COMMIT DROP AS
SELECT p.*
FROM "Products" p
WHERE COALESCE(p."IsDeleted", FALSE) = FALSE
  AND NOT EXISTS
  (
      SELECT 1
      FROM "ProductVariants" pv
      WHERE pv."ProductId" = p."Id"
        AND COALESCE(pv."IsDeleted", FALSE) = FALSE
  );

INSERT INTO "__DefaultVariantBackfill_Backup_Products"
SELECT p.*
FROM "_products_without_variants" p
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__DefaultVariantBackfill_Backup_Products" b
    WHERE b."Id" = p."Id"
);

INSERT INTO "__DefaultVariantBackfill_Backup_WarehouseStocks"
SELECT ws.*
FROM "WarehouseStocks" ws
INNER JOIN "_products_without_variants" p
    ON p."Id" = ws."ProductId"
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__DefaultVariantBackfill_Backup_WarehouseStocks" b
    WHERE b."Id" = ws."Id"
);

-- ----------------------------------------------------------------------------
-- 3. Create one safe default variant per missing product
-- ----------------------------------------------------------------------------

CREATE TEMP TABLE "_default_variant_stage" ON COMMIT DROP AS
WITH candidates AS
(
    SELECT
        p."Id" AS "ProductId",
        p."TenantId",
        p."Barcode",
        p."SalePrice",
        p."OnlinePrice",
        p."PurchasePrice",
        p."IsActive",
        p."IsDeleted",
        COALESCE(
            NULLIF(BTRIM(p."SKU"), ''),
            NULLIF(BTRIM(p."Barcode"), ''),
            'AUTO-' || UPPER(SUBSTRING(REPLACE(p."Id"::text, '-', '') FROM 1 FOR 8))
        ) AS "BaseSku",
        MD5('default-variant:' || p."Id"::text) AS "VariantHash"
    FROM "_products_without_variants" p
),
ranked AS
(
    SELECT
        c.*,
        ROW_NUMBER() OVER (PARTITION BY c."BaseSku" ORDER BY c."ProductId") AS "BaseSkuRank"
    FROM candidates c
),
prepared AS
(
    SELECT
        r."ProductId",
        r."TenantId",
        (
            SUBSTRING(r."VariantHash", 1, 8) || '-' ||
            SUBSTRING(r."VariantHash", 9, 4) || '-' ||
            SUBSTRING(r."VariantHash", 13, 4) || '-' ||
            SUBSTRING(r."VariantHash", 17, 4) || '-' ||
            SUBSTRING(r."VariantHash", 21, 12)
        )::uuid AS "VariantId",
        CASE
            WHEN r."BaseSkuRank" = 1
             AND NOT EXISTS
             (
                 SELECT 1
                 FROM "ProductVariants" existing
                 WHERE COALESCE(existing."IsDeleted", FALSE) = FALSE
                   AND existing."SKU" = r."BaseSku"
             )
            THEN r."BaseSku"
            ELSE r."BaseSku" || '-' || LOWER(SUBSTRING(REPLACE(r."ProductId"::text, '-', '') FROM 1 FOR 8))
        END AS "FinalSku",
        r."Barcode",
        r."SalePrice",
        r."OnlinePrice",
        r."PurchasePrice",
        r."IsActive",
        r."IsDeleted"
    FROM ranked r
)
SELECT *
FROM prepared;

INSERT INTO "ProductVariants"
(
    "Id",
    "TenantId",
    "ProductId",
    "SKU",
    "Barcode",
    "Price",
    "OnlinePrice",
    "CostPrice",
    "IsDefault",
    "IsActive",
    "SortOrder",
    "CreatedAt",
    "UpdatedAt",
    "IsDeleted"
)
SELECT
    s."VariantId",
    s."TenantId",
    s."ProductId",
    s."FinalSku",
    s."Barcode",
    s."SalePrice",
    s."OnlinePrice",
    s."PurchasePrice",
    TRUE AS "IsDefault",
    s."IsActive",
    0 AS "SortOrder",
    NOW() AS "CreatedAt",
    NULL::timestamp with time zone AS "UpdatedAt",
    s."IsDeleted"
FROM "_default_variant_stage" s
ON CONFLICT ("Id") DO UPDATE
SET
    "TenantId" = EXCLUDED."TenantId",
    "ProductId" = EXCLUDED."ProductId",
    "SKU" = EXCLUDED."SKU",
    "Barcode" = EXCLUDED."Barcode",
    "Price" = EXCLUDED."Price",
    "OnlinePrice" = EXCLUDED."OnlinePrice",
    "CostPrice" = EXCLUDED."CostPrice",
    "IsDefault" = TRUE,
    "IsActive" = EXCLUDED."IsActive",
    "SortOrder" = 0,
    "UpdatedAt" = NOW(),
    "IsDeleted" = FALSE;

INSERT INTO "__DefaultVariantBackfill_CreatedVariants"
(
    "ProductId",
    "VariantId"
)
SELECT
    s."ProductId",
    s."VariantId"
FROM "_default_variant_stage" s
ON CONFLICT ("ProductId") DO UPDATE
SET
    "VariantId" = EXCLUDED."VariantId";

-- ----------------------------------------------------------------------------
-- 4. Backfill ProductVariantStocks from legacy WarehouseStocks
-- ----------------------------------------------------------------------------

CREATE TEMP TABLE "_default_variant_stock_stage" ON COMMIT DROP AS
WITH source_rows AS
(
    SELECT
        ws."Id" AS "WarehouseStockId",
        ws."TenantId",
        pv."Id" AS "VariantId",
        ws."WarehouseId",
        ws."Quantity",
        ws."ReservedQuantity",
        ws."IsDeleted",
        (
            SUBSTRING(MD5('default-variant-stock:' || pv."Id"::text || ':' || ws."WarehouseId"::text), 1, 8) || '-' ||
            SUBSTRING(MD5('default-variant-stock:' || pv."Id"::text || ':' || ws."WarehouseId"::text), 9, 4) || '-' ||
            SUBSTRING(MD5('default-variant-stock:' || pv."Id"::text || ':' || ws."WarehouseId"::text), 13, 4) || '-' ||
            SUBSTRING(MD5('default-variant-stock:' || pv."Id"::text || ':' || ws."WarehouseId"::text), 17, 4) || '-' ||
            SUBSTRING(MD5('default-variant-stock:' || pv."Id"::text || ':' || ws."WarehouseId"::text), 21, 12)
        )::uuid AS "VariantStockId",
        ROW_NUMBER() OVER
        (
            PARTITION BY ws."TenantId", pv."Id", ws."WarehouseId"
            ORDER BY ws."UpdatedAt" DESC NULLS LAST, ws."CreatedAt" DESC NULLS LAST, ws."Id" DESC
        ) AS "RowRank"
    FROM "WarehouseStocks" ws
    INNER JOIN "Products" p
        ON p."Id" = ws."ProductId"
    INNER JOIN "ProductVariants" pv
        ON pv."ProductId" = p."Id"
       AND pv."IsDefault" = TRUE
       AND COALESCE(pv."IsDeleted", FALSE) = FALSE
    WHERE COALESCE(ws."IsDeleted", FALSE) = FALSE
      AND COALESCE(p."HasVariants", FALSE) = FALSE
)
SELECT
    sr."WarehouseStockId",
    sr."TenantId",
    sr."VariantId",
    sr."WarehouseId",
    sr."Quantity",
    sr."ReservedQuantity",
    sr."IsDeleted",
    sr."VariantStockId"
FROM source_rows sr
WHERE sr."RowRank" = 1;

INSERT INTO "ProductVariantStocks"
(
    "Id",
    "TenantId",
    "VariantId",
    "WarehouseId",
    "Quantity",
    "ReservedQuantity",
    "CreatedAt",
    "UpdatedAt",
    "IsDeleted"
)
SELECT
    s."VariantStockId",
    s."TenantId",
    s."VariantId",
    s."WarehouseId",
    s."Quantity",
    s."ReservedQuantity",
    NOW() AS "CreatedAt",
    NULL::timestamp with time zone AS "UpdatedAt",
    s."IsDeleted"
FROM "_default_variant_stock_stage" s
ON CONFLICT ("TenantId", "VariantId", "WarehouseId") DO UPDATE
SET
    "Quantity" = EXCLUDED."Quantity",
    "ReservedQuantity" = EXCLUDED."ReservedQuantity",
    "UpdatedAt" = NOW(),
    "IsDeleted" = FALSE;

-- ----------------------------------------------------------------------------
-- 5. Link legacy WarehouseStocks rows to the simple-product default variant
-- ----------------------------------------------------------------------------

UPDATE "WarehouseStocks" ws
SET
    "ProductVariantId" = pv."Id",
    "UpdatedAt" = NOW()
FROM "Products" p
INNER JOIN "ProductVariants" pv
    ON pv."ProductId" = p."Id"
   AND pv."IsDefault" = TRUE
   AND COALESCE(pv."IsDeleted", FALSE) = FALSE
WHERE ws."ProductId" = p."Id"
  AND COALESCE(ws."IsDeleted", FALSE) = FALSE
  AND COALESCE(p."HasVariants", FALSE) = FALSE
  AND ws."ProductVariantId" IS DISTINCT FROM pv."Id";

-- ----------------------------------------------------------------------------
-- 6. Verification queries
-- ----------------------------------------------------------------------------

SELECT COUNT(*) AS "ProductsBackfilledThisRun"
FROM "_products_without_variants";

SELECT COUNT(*) AS "CreatedOrRepairedDefaultVariants"
FROM "_default_variant_stage";

SELECT COUNT(*) AS "RemainingProductsWithoutActiveVariants"
FROM "Products" p
WHERE COALESCE(p."IsDeleted", FALSE) = FALSE
  AND NOT EXISTS
  (
      SELECT 1
      FROM "ProductVariants" pv
      WHERE pv."ProductId" = p."Id"
        AND COALESCE(pv."IsDeleted", FALSE) = FALSE
  );

SELECT
    p."Id" AS "ProductId",
    p."Name",
    COUNT(*) FILTER (WHERE pv."IsDefault" = TRUE AND COALESCE(pv."IsDeleted", FALSE) = FALSE) AS "DefaultVariantCount",
    COUNT(*) FILTER (WHERE COALESCE(pv."IsDeleted", FALSE) = FALSE) AS "ActiveVariantCount"
FROM "Products" p
LEFT JOIN "ProductVariants" pv
    ON pv."ProductId" = p."Id"
GROUP BY p."Id", p."Name"
HAVING COUNT(*) FILTER (WHERE pv."IsDefault" = TRUE AND COALESCE(pv."IsDeleted", FALSE) = FALSE) <> 1
ORDER BY p."Name", p."Id";

SELECT
    p."Id" AS "ProductId",
    p."Name",
    pv."Id" AS "VariantId",
    pv."SKU",
    pv."Barcode",
    pv."Price",
    pv."OnlinePrice",
    pv."CostPrice"
FROM "_default_variant_stage" s
INNER JOIN "Products" p
    ON p."Id" = s."ProductId"
INNER JOIN "ProductVariants" pv
    ON pv."Id" = s."VariantId"
ORDER BY p."Name", p."Id";

COMMIT;
