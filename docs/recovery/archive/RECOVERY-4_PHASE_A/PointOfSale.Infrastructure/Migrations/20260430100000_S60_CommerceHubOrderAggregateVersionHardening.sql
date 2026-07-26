-- Migration: S60_CommerceHubOrderAggregateVersionHardening
-- Purpose: give CommerceHub marketplace orders/sub-orders monotonic projection
-- versions so cross-system consumers can reject stale payment/order events.

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

UPDATE "CommerceHubMarketplaceOrders"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" <= 0;

UPDATE "CommerceHubSubOrders"
SET "ProjectionMutationVersion" = 1
WHERE "ProjectionMutationVersion" <= 0;

ALTER TABLE "CommerceHubMarketplaceOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubMarketplaceOrders_ProjectionMutationVersion_Positive";

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD CONSTRAINT "CK_CommerceHubMarketplaceOrders_ProjectionMutationVersion_Positive"
    CHECK ("ProjectionMutationVersion" > 0) NOT VALID;

ALTER TABLE "CommerceHubMarketplaceOrders"
    VALIDATE CONSTRAINT "CK_CommerceHubMarketplaceOrders_ProjectionMutationVersion_Positive";

ALTER TABLE "CommerceHubSubOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubSubOrders_ProjectionMutationVersion_Positive";

ALTER TABLE "CommerceHubSubOrders"
    ADD CONSTRAINT "CK_CommerceHubSubOrders_ProjectionMutationVersion_Positive"
    CHECK ("ProjectionMutationVersion" > 0) NOT VALID;

ALTER TABLE "CommerceHubSubOrders"
    VALIDATE CONSTRAINT "CK_CommerceHubSubOrders_ProjectionMutationVersion_Positive";

CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_ProjectionMutationVersion"
    ON "CommerceHubMarketplaceOrders" ("Id", "ProjectionMutationVersion");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_MarketplaceOrderId_ProjectionMutationVersion"
    ON "CommerceHubSubOrders" ("MarketplaceOrderId", "ProjectionMutationVersion");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260430100000_S60_CommerceHubOrderAggregateVersionHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260430100000_S60_CommerceHubOrderAggregateVersionHardening'
);
