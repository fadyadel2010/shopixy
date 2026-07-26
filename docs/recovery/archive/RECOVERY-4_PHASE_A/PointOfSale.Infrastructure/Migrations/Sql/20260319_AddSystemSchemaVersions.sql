BEGIN;

-- ============================================================================
-- System Schema Versions
-- Tracks applied operational schema versions across PostgreSQL, MongoDB, and
-- Elasticsearch for startup validation and rollout visibility.
--
-- Safe to run multiple times from PgAdmin.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "SystemSchemaVersions"
(
    "Id" uuid NOT NULL,
    "Component" character varying(100) NOT NULL,
    "Version" character varying(200) NOT NULL,
    "AppliedAt" timestamp with time zone NOT NULL,
    "DetailsJson" text NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_SystemSchemaVersions" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_SystemSchemaVersions_Component"
    ON "SystemSchemaVersions" ("Component");

COMMIT;
