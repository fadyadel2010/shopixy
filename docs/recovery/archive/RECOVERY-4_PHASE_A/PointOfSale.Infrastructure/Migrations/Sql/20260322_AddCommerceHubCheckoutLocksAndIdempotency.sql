BEGIN;

ALTER TABLE "CommerceHubCarts"
    ADD COLUMN IF NOT EXISTS "IsLocked" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "LockedAtUtc" timestamp with time zone NULL;

CREATE TABLE IF NOT EXISTS "CommerceHubCheckoutIdempotency" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "Key" character varying(100) NOT NULL,
    "TenantId" uuid NULL,
    "OnlineCustomerId" uuid NULL,
    "ResponseJson" text NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_CommerceHubCheckoutIdempotency" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_Key"
    ON "CommerceHubCheckoutIdempotency" ("Key");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId"
    ON "CommerceHubCheckoutIdempotency" ("TenantId", "OnlineCustomerId");

COMMIT;
