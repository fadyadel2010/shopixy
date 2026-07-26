BEGIN;

-- ============================================================================
-- CommerceHub Search Filter Events
-- Adds SearchFilterEvents for dynamic filter analytics feedback loops.
-- Safe to run multiple times.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "SearchFilterEvents"
(
    "Id" uuid NOT NULL,
    "FilterKey" character varying(200) NOT NULL,
    "FilterValue" character varying(500) NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "SessionId" character varying(100) NULL,
    "TenantId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SearchFilterEvents" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_SearchFilterEvents_FilterKey_OccurredAt"
    ON "SearchFilterEvents" ("FilterKey", "OccurredAt");

CREATE INDEX IF NOT EXISTS "IX_SearchFilterEvents_QueryText_OccurredAt"
    ON "SearchFilterEvents" ("QueryText", "OccurredAt");

CREATE INDEX IF NOT EXISTS "IX_SearchFilterEvents_TenantId_OccurredAt"
    ON "SearchFilterEvents" ("TenantId", "OccurredAt");

COMMIT;
