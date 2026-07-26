BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

CREATE TABLE IF NOT EXISTS "SalesRequestIdempotency"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(100) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "ResponseStatusCode" integer NULL,
    "ResponseJson" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_SalesRequestIdempotency" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SalesRequestIdempotency_TenantId_Key_Operation"
    ON "SalesRequestIdempotency" ("TenantId", "Key", "Operation");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260407134756_AddSalesRequestIdempotency', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260407134756_AddSalesRequestIdempotency'
);

COMMIT;
