-- S35: Event ordering boundary hardening
-- Adds per-aggregate accounting outbox version metadata so financial lifecycle
-- processing is deterministic by entity/version instead of wall-clock time only.

ALTER TABLE "AccountingOutboxMessages"
    ADD COLUMN IF NOT EXISTS "AggregateVersion" integer NULL;

UPDATE "AccountingOutboxMessages"
SET "AggregateVersion" = ("PayloadJson"::jsonb ->> 'MutationVersion')::integer
WHERE "AggregateVersion" IS NULL
  AND "PayloadJson" IS NOT NULL
  AND "PayloadJson"::jsonb ? 'MutationVersion'
  AND ("PayloadJson"::jsonb ->> 'MutationVersion') ~ '^[0-9]+$';

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_AggregateVersionOrdering"
    ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc")
    WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_AggregateVersionBlockers"
    ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "ProcessedAtUtc", "IsFailed", "EventType")
    WHERE "ProcessedAtUtc" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_EntityOrdering"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "EntityId", "PublishedAtUtc", "OccurredAtUtc", "Version", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_EntityOrderingBlockers"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "EntityId", "OccurredAtUtc", "Version", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_VariantOrdering"
    ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "PublishedAtUtc", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_VariantOrderingBlockers"
    ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424183000_S35_EventOrderingBoundaryHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424183000_S35_EventOrderingBoundaryHardening'
);
