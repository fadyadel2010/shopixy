-- Migration: S61_DataAuthorityAndEventScopeClosure
-- Purpose: close remaining data-authority ambiguity for CommerceHub ownership
-- and ensure persisted legacy cart aliases cannot diverge from the CommerceHub
-- customer authority.

UPDATE "CommerceHubMarketplaceOrders"
SET "OnlineCustomerId" = NULL
WHERE "OnlineCustomerId" IS NOT NULL;

UPDATE "CommerceHubCarts"
SET "CommerceHubCustomerId" = "OnlineCustomerId"
WHERE ("CommerceHubCustomerId" IS NULL OR "CommerceHubCustomerId" = '00000000-0000-0000-0000-000000000000')
  AND "OnlineCustomerId" <> '00000000-0000-0000-0000-000000000000';

UPDATE "CommerceHubCarts"
SET "OnlineCustomerId" = "CommerceHubCustomerId"
WHERE "CommerceHubCustomerId" IS NOT NULL
  AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000'
  AND "OnlineCustomerId" <> "CommerceHubCustomerId";

ALTER TABLE "CommerceHubMarketplaceOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary";

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD CONSTRAINT "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary"
    CHECK (
        "CustomerId" <> '00000000-0000-0000-0000-000000000000'
        AND "OnlineCustomerId" IS NULL
    ) NOT VALID;

ALTER TABLE "CommerceHubMarketplaceOrders"
    VALIDATE CONSTRAINT "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary";

ALTER TABLE "CommerceHubCarts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubCarts_CustomerAuthorityBoundary";

ALTER TABLE "CommerceHubCarts"
    ADD CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary"
    CHECK (
        "CommerceHubCustomerId" IS NOT NULL
        AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000'
        AND "OnlineCustomerId" IS NOT NULL
        AND "OnlineCustomerId" = "CommerceHubCustomerId"
    ) NOT VALID;

ALTER TABLE "CommerceHubCarts"
    VALIDATE CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260430110000_S61_DataAuthorityAndEventScopeClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260430110000_S61_DataAuthorityAndEventScopeClosure'
);
