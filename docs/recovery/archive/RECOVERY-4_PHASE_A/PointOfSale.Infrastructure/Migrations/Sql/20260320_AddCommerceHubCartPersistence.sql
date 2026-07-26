BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubCarts" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "ItemsJson" text NOT NULL DEFAULT '[]',
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubCarts" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubCarts_TenantId_OnlineCustomerId"
    ON "CommerceHubCarts" ("TenantId", "OnlineCustomerId")
    WHERE "IsDeleted" = false;

COMMIT;
