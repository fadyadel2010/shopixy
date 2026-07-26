BEGIN;

CREATE TABLE IF NOT EXISTS "PhoneRiskProfiles"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "IsBlocked" boolean NOT NULL DEFAULT FALSE,
    "BlockedReason" character varying(500) NULL,
    "BlockedByUserId" uuid NULL,
    "BlockedAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PhoneRiskProfiles" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PhoneRiskProfiles_TenantId_PhoneNumber"
    ON "PhoneRiskProfiles" ("TenantId", "PhoneNumber");

CREATE INDEX IF NOT EXISTS "IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc"
    ON "PhoneRiskProfiles" ("TenantId", "IsBlocked", "BlockedAtUtc");

COMMIT;
