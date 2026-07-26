WITH normalized_product_images AS (
    SELECT
        p."Id" AS "ProductId",
        p."TenantId",
        btrim(parsed.image_url) AS "Url",
        parsed.ordinality AS "OriginalPosition"
    FROM "Products" p
    CROSS JOIN LATERAL jsonb_array_elements_text(
        CASE
            WHEN p."Images" IS NULL OR btrim(p."Images") = '' THEN '[]'::jsonb
            WHEN left(btrim(p."Images"), 1) = '[' THEN p."Images"::jsonb
            ELSE to_jsonb(string_to_array(p."Images", ','))
        END
    ) WITH ORDINALITY AS parsed(image_url, ordinality)
    WHERE p."Images" IS NOT NULL
),
deduped_product_images AS (
    SELECT
        "ProductId",
        "TenantId",
        "Url",
        MIN("OriginalPosition") AS "OriginalPosition"
    FROM normalized_product_images
    WHERE "Url" <> ''
    GROUP BY "ProductId", "TenantId", "Url"
),
product_images_to_insert AS (
    SELECT
        dpi."TenantId",
        dpi."ProductId",
        dpi."Url",
        COALESCE(existing."BasePosition", 0)
            + ROW_NUMBER() OVER (PARTITION BY dpi."ProductId" ORDER BY dpi."OriginalPosition", dpi."Url") AS "Position"
    FROM deduped_product_images dpi
    LEFT JOIN LATERAL (
        SELECT MAX(pm."Position") AS "BasePosition"
        FROM "ProductMedia" pm
        WHERE pm."ProductId" = dpi."ProductId"
    ) existing ON TRUE
    WHERE NOT EXISTS (
        SELECT 1
        FROM "ProductMedia" pm
        WHERE pm."ProductId" = dpi."ProductId"
          AND pm."MediaType" = 'image'
          AND pm."Url" = dpi."Url"
    )
)
INSERT INTO "ProductMedia" (
    "Id",
    "TenantId",
    "ProductId",
    "MediaType",
    "Url",
    "Provider",
    "Position",
    "CreatedAtUtc"
)
SELECT
    gen_random_uuid(),
    pii."TenantId",
    pii."ProductId",
    'image',
    pii."Url",
    NULL,
    pii."Position",
    NOW()
FROM product_images_to_insert pii;

UPDATE "ProductVariants" v
SET "MediaId" = pm."Id"
FROM "ProductMedia" pm
WHERE v."ProductId" = pm."ProductId"
  AND pm."MediaType" = 'image'
  AND v."MediaId" IS NULL
  AND v."ImageUrl" IS NOT NULL
  AND btrim(v."ImageUrl") <> ''
  AND pm."Url" = btrim(v."ImageUrl");

WITH missing_variant_urls AS (
    SELECT
        v."ProductId",
        p."TenantId",
        btrim(v."ImageUrl") AS "Url"
    FROM "ProductVariants" v
    INNER JOIN "Products" p ON p."Id" = v."ProductId"
    WHERE v."MediaId" IS NULL
      AND v."ImageUrl" IS NOT NULL
      AND btrim(v."ImageUrl") <> ''
      AND NOT EXISTS (
          SELECT 1
          FROM "ProductMedia" pm
          WHERE pm."ProductId" = v."ProductId"
            AND pm."MediaType" = 'image'
            AND pm."Url" = btrim(v."ImageUrl")
      )
    GROUP BY v."ProductId", p."TenantId", btrim(v."ImageUrl")
),
variant_urls_to_insert AS (
    SELECT
        mvu."TenantId",
        mvu."ProductId",
        mvu."Url",
        COALESCE(existing."BasePosition", 0)
            + ROW_NUMBER() OVER (PARTITION BY mvu."ProductId" ORDER BY mvu."Url") AS "Position"
    FROM missing_variant_urls mvu
    LEFT JOIN LATERAL (
        SELECT MAX(pm."Position") AS "BasePosition"
        FROM "ProductMedia" pm
        WHERE pm."ProductId" = mvu."ProductId"
    ) existing ON TRUE
)
INSERT INTO "ProductMedia" (
    "Id",
    "TenantId",
    "ProductId",
    "MediaType",
    "Url",
    "Provider",
    "Position",
    "CreatedAtUtc"
)
SELECT
    gen_random_uuid(),
    vui."TenantId",
    vui."ProductId",
    'image',
    vui."Url",
    NULL,
    vui."Position",
    NOW()
FROM variant_urls_to_insert vui;

UPDATE "ProductVariants" v
SET "MediaId" = pm."Id"
FROM "ProductMedia" pm
WHERE v."ProductId" = pm."ProductId"
  AND pm."MediaType" = 'image'
  AND v."MediaId" IS NULL
  AND v."ImageUrl" IS NOT NULL
  AND btrim(v."ImageUrl") <> ''
  AND pm."Url" = btrim(v."ImageUrl");

UPDATE "Products"
SET "Images" = NULL
WHERE "Images" IS NOT NULL
  AND btrim("Images") <> '';

UPDATE "ProductVariants"
SET "ImageUrl" = NULL
WHERE "ImageUrl" IS NOT NULL
  AND btrim("ImageUrl") <> '';
