-- S51: Cross-system critical risk hardening
-- Marketplace lifecycle ordering must be rooted in the business aggregate, not the incidental event entity.

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD COLUMN IF NOT EXISTS "AggregateRootId" uuid;

UPDATE "MarketplaceEventOutboxMessages"
SET "AggregateRootId" = "EntityId"
WHERE "AggregateRootId" IS NULL;

ALTER TABLE "MarketplaceEventOutboxMessages"
    ALTER COLUMN "AggregateRootId" SET NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_TenantId_AggregateRootId"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId");

DROP INDEX IF EXISTS "IX_MarketplaceEventOutbox_AggregateOrdering";
CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrdering"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "PublishedAtUtc", "OccurredAtUtc", "Version", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;

DROP INDEX IF EXISTS "IX_MarketplaceEventOutbox_AggregateOrderingBlockers";
CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrderingBlockers"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "OccurredAtUtc", "Version", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429123000_S51_CrossSystemCriticalRiskHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429123000_S51_CrossSystemCriticalRiskHardening'
);
