BEGIN;

-- ============================================================================
-- CommerceHub Search Intelligence
-- Adds:
--   1. SearchClicks
--   2. SearchImpressions
--   3. SearchQueries
--   4. SearchSynonyms
--
-- Notes:
-- - Safe to run multiple times.
-- - Matches the current EF model introduced for CommerceHub search analytics.
-- - MongoDB popularityScore does not need SQL schema migration.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "SearchClicks"
(
    "Id" uuid NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NULL,
    "Position" integer NOT NULL,
    "ClickedAt" timestamp with time zone NOT NULL,
    "SessionId" character varying(100) NULL,
    "TenantId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SearchClicks" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "SearchQueries"
(
    "Id" uuid NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "ResultCount" bigint NOT NULL,
    "Intent" character varying(50) NOT NULL DEFAULT 'unknown',
    "AppliedExpansionsJson" text NULL,
    "SearchedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SearchQueries" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "SearchImpressions"
(
    "Id" uuid NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "ProductId" uuid NOT NULL,
    "Position" integer NOT NULL,
    "ImpressedAt" timestamp with time zone NOT NULL,
    "SessionId" character varying(100) NULL,
    "TenantId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SearchImpressions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "SearchSynonyms"
(
    "Id" uuid NOT NULL,
    "Term" character varying(200) NOT NULL,
    "SynonymsJson" text NOT NULL,
    "Direction" character varying(20) NOT NULL DEFAULT 'one_way',
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SearchSynonyms" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_SearchClicks_QueryText_ClickedAt"
    ON "SearchClicks" ("QueryText", "ClickedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchClicks_ProductId_ClickedAt"
    ON "SearchClicks" ("ProductId", "ClickedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchClicks_TenantId_ClickedAt"
    ON "SearchClicks" ("TenantId", "ClickedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchQueries_QueryText_SearchedAt"
    ON "SearchQueries" ("QueryText", "SearchedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchQueries_ResultCount_SearchedAt"
    ON "SearchQueries" ("ResultCount", "SearchedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchImpressions_QueryText_ImpressedAt"
    ON "SearchImpressions" ("QueryText", "ImpressedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchImpressions_ProductId_ImpressedAt"
    ON "SearchImpressions" ("ProductId", "ImpressedAt");

CREATE INDEX IF NOT EXISTS "IX_SearchImpressions_TenantId_ImpressedAt"
    ON "SearchImpressions" ("TenantId", "ImpressedAt");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_SearchSynonyms_Term"
    ON "SearchSynonyms" ("Term");

ALTER TABLE IF EXISTS "SearchSynonyms"
    ADD COLUMN IF NOT EXISTS "Direction" character varying(20) NOT NULL DEFAULT 'one_way';

ALTER TABLE IF EXISTS "SearchQueries"
    ADD COLUMN IF NOT EXISTS "Intent" character varying(50) NOT NULL DEFAULT 'unknown';

ALTER TABLE IF EXISTS "SearchQueries"
    ADD COLUMN IF NOT EXISTS "AppliedExpansionsJson" text NULL;

-- Optional seed examples.
-- Store terms normalized/lowercase to match runtime normalization.
-- Direction:
--   one_way: term -> synonyms only
--   two_way: term -> synonyms and synonym -> term
-- INSERT INTO "SearchSynonyms" ("Id", "Term", "SynonymsJson", "Direction", "CreatedAt", "IsDeleted")
-- VALUES
--     (gen_random_uuid(), 'tv', '["television"]', 'one_way', NOW(), false),
--     (gen_random_uuid(), 'iphone', '["apple phone","ios phone"]', 'two_way', NOW(), false)
-- ON CONFLICT ("Term") DO NOTHING;

COMMIT;
