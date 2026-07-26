ALTER TABLE "CommerceHubCarts"
    ADD COLUMN IF NOT EXISTS "CommerceHubCustomerId" uuid NULL;

ALTER TABLE "CommerceHubCheckoutIdempotency"
    ADD COLUMN IF NOT EXISTS "CommerceHubCustomerId" uuid NULL;

-- Do not backfill from OnlineCustomerId. Those IDs belong to the Storefront
-- identity space and must not be promoted to CommerceHub ownership.

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubCarts_CommerceHubCustomerId_Active"
    ON "CommerceHubCarts" ("CommerceHubCustomerId")
    WHERE "CommerceHubCustomerId" IS NOT NULL AND "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId"
    ON "CommerceHubCheckoutIdempotency" ("CommerceHubCustomerId")
    WHERE "CommerceHubCustomerId" IS NOT NULL;
