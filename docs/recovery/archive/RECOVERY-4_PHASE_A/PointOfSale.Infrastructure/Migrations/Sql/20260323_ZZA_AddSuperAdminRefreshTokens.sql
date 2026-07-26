BEGIN;

ALTER TABLE "SuperAdmins"
    ADD COLUMN IF NOT EXISTS "RefreshTokenHash" character varying(256) NULL,
    ADD COLUMN IF NOT EXISTS "RefreshTokenExpiresAtUtc" timestamp with time zone NULL;

CREATE INDEX IF NOT EXISTS "IX_SuperAdmins_RefreshTokenHash"
    ON "SuperAdmins" ("RefreshTokenHash")
    WHERE "RefreshTokenHash" IS NOT NULL;

COMMIT;
