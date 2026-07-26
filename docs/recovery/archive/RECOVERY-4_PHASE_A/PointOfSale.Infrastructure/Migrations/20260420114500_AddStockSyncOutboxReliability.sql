BEGIN;

ALTER TABLE "ProductVariants"
ADD COLUMN IF NOT EXISTS "StockUpdatedAtUtc" timestamp with time zone NULL;

UPDATE "ProductVariants"
SET "StockUpdatedAtUtc" = COALESCE("UpdatedAt", "CreatedAt", NOW())
WHERE "StockUpdatedAtUtc" IS NULL;

CREATE TABLE IF NOT EXISTS "StockSyncOutboxMessages"
(
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "TenantId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "StockUpdatedAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone NULL,
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "LastError" character varying(2000) NULL,
    "NextRetryAtUtc" timestamp with time zone NULL,
    "IsFailed" boolean NOT NULL DEFAULT FALSE,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_StockSyncOutboxMessages" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockSyncOutbox_EventId"
    ON "StockSyncOutboxMessages" ("EventId");

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_OccurredAtUtc"
    ON "StockSyncOutboxMessages" ("OccurredAtUtc");

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_ProcessingStatus"
    ON "StockSyncOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_TenantId_VariantId"
    ON "StockSyncOutboxMessages" ("TenantId", "VariantId");

COMMIT;
