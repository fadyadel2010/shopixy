BEGIN;

-- ============================================================================
-- Marketplace Product AI Content
-- Preserves structured marketplace-facing product content separately from the
-- legacy description and tag text fields.
-- Safe to run from pgAdmin 4.
-- ============================================================================

DO $$
BEGIN
    IF to_regclass('public."Products"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."Products" was not found.';
    END IF;
END $$;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "FeaturesJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "FaqJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "TagsJson" jsonb NULL,
    ADD COLUMN IF NOT EXISTS "AiGenerated" boolean NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS "AiMetadataJson" jsonb NULL;

UPDATE "Products" AS p
SET "TagsJson" = to_jsonb(normalized.tags)
FROM (
    SELECT
        p0."Id",
        ARRAY(
            SELECT DISTINCT trimmed_value
            FROM (
                SELECT NULLIF(btrim(value), '') AS trimmed_value
                FROM regexp_split_to_table(COALESCE(p0."Tags", ''), E'[,;\\n]+') AS value
            ) AS normalized_values
            WHERE trimmed_value IS NOT NULL
        ) AS tags
    FROM "Products" AS p0
) AS normalized
WHERE p."Id" = normalized."Id"
  AND (p."TagsJson" IS NULL OR p."TagsJson" = 'null'::jsonb)
  AND array_length(normalized.tags, 1) > 0;

COMMIT;
