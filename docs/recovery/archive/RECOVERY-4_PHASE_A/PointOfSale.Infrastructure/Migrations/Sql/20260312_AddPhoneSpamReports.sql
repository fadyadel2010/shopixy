BEGIN;

CREATE TABLE IF NOT EXISTS "PhoneSpamReports"
(
    "Id" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "ReportedByUserId" uuid NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PhoneSpamReports" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PhoneSpamReports_TenantId_OrderId"
    ON "PhoneSpamReports" ("TenantId", "OrderId");

CREATE INDEX IF NOT EXISTS "IX_PhoneSpamReports_PhoneNumber"
    ON "PhoneSpamReports" ("PhoneNumber");

COMMIT;
