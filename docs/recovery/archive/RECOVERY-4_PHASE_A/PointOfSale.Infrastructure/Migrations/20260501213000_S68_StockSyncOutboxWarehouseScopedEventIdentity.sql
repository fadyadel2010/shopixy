-- S68: Stock sync events are warehouse-scoped, so their durable identity must be
-- warehouse-scoped as well. A variant-level uniqueness key can reject legitimate
-- multi-warehouse stock updates that share the same product projection version.

ALTER TABLE "StockSyncOutboxMessages"
    ADD COLUMN IF NOT EXISTS "WarehouseId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

UPDATE "StockSyncOutboxMessages"
SET "WarehouseId" = ("PayloadJson"::jsonb ->> 'WarehouseId')::uuid
WHERE "WarehouseId" = '00000000-0000-0000-0000-000000000000'::uuid
  AND "PayloadJson" IS NOT NULL
  AND "PayloadJson" <> ''
  AND "PayloadJson"::jsonb ? 'WarehouseId'
  AND "PayloadJson"::jsonb ->> 'WarehouseId' IS NOT NULL
  AND "PayloadJson"::jsonb ->> 'WarehouseId' <> ''
  AND "PayloadJson"::jsonb ->> 'WarehouseId' ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "StockSyncOutboxMessages"
        WHERE "WarehouseId" = '00000000-0000-0000-0000-000000000000'::uuid
          AND "IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'Cannot add warehouse-scoped stock sync identity: active stock sync outbox rows are missing WarehouseId and require repair before migration.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "StockSyncOutboxMessages"
        WHERE "IsDeleted" = false
        GROUP BY "TenantId", "VariantId", "WarehouseId", "EventVersion"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion: duplicate stock sync event versions exist for the same warehouse.';
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_StockSyncOutboxMessages_WarehouseId_NotEmpty'
    ) THEN
        ALTER TABLE "StockSyncOutboxMessages"
            ADD CONSTRAINT "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty"
            CHECK ("WarehouseId" <> '00000000-0000-0000-0000-000000000000'::uuid)
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "StockSyncOutboxMessages"
    VALIDATE CONSTRAINT "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty";

DROP INDEX IF EXISTS "UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion";
DROP INDEX IF EXISTS "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion";

-- Drop the global EventId unique index as it conflicts with warehouse-scoped identity
-- (same EventId can legitimately appear across different warehouses)
DROP INDEX IF EXISTS "IX_StockSyncOutbox_EventId";

CREATE UNIQUE INDEX "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion"
ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "WarehouseId", "EventVersion")
WHERE "IsDeleted" = false;

-- Create warehouse-scoped EventId index to replace the global one
CREATE UNIQUE INDEX "IX_StockSyncOutbox_Warehouse_EventId"
ON "StockSyncOutboxMessages" ("TenantId", "WarehouseId", "EventId")
WHERE "IsDeleted" = false;

DROP INDEX IF EXISTS "IX_StockSyncOutboxMessages_Unpublished_EventOrdering";

CREATE INDEX "IX_StockSyncOutboxMessages_Unpublished_EventOrdering"
ON "StockSyncOutboxMessages" (
    "TenantId",
    "VariantId",
    "WarehouseId",
    "EventVersion",
    "StockUpdatedAtUtc",
    "OccurredAtUtc",
    "CreatedAt",
    "Id"
)
WHERE "PublishedAtUtc" IS NULL
  AND "IsFailed" = false
  AND "IsDeleted" = false;

-- Recreate VariantOrdering index with WarehouseId for warehouse-scoped queries
DROP INDEX IF EXISTS "IX_StockSyncOutbox_VariantOrdering";

CREATE INDEX "IX_StockSyncOutbox_VariantOrdering"
ON "StockSyncOutboxMessages" (
    "TenantId",
    "VariantId",
    "WarehouseId",
    "PublishedAtUtc",
    "EventVersion",
    "StockUpdatedAtUtc",
    "OccurredAtUtc",
    "CreatedAt",
    "Id"
)
WHERE "PublishedAtUtc" IS NULL
  AND "IsFailed" = false;

-- Recreate VariantOrderingBlockers index with WarehouseId for warehouse-scoped queries
DROP INDEX IF EXISTS "IX_StockSyncOutbox_VariantOrderingBlockers";

CREATE INDEX "IX_StockSyncOutbox_VariantOrderingBlockers"
ON "StockSyncOutboxMessages" (
    "TenantId",
    "VariantId",
    "WarehouseId",
    "EventVersion",
    "StockUpdatedAtUtc",
    "OccurredAtUtc",
    "CreatedAt",
    "Id"
)
WHERE "PublishedAtUtc" IS NULL;
