BEGIN;

ALTER TABLE "PhoneRiskProfiles"
    ADD COLUMN IF NOT EXISTS "SpamReports" integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS "GlobalPhoneReputation"
(
    "Id" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "SpamReports" integer NOT NULL DEFAULT 0,
    "FirstReportedAtUtc" timestamp with time zone NOT NULL,
    "LastReportedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_GlobalPhoneReputation" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_GlobalPhoneReputation_PhoneNumber"
    ON "GlobalPhoneReputation" ("PhoneNumber");

COMMIT;
