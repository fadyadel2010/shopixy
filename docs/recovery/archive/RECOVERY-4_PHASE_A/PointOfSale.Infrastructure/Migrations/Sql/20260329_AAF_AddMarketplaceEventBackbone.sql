BEGIN;

CREATE TABLE IF NOT EXISTS "MarketplaceEventOutboxMessages"
(
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "EntityId" uuid NOT NULL,
    "TenantId" uuid NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "Version" integer NOT NULL DEFAULT 1,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone NULL,
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "LastError" character varying(2000) NULL,
    "NextRetryAtUtc" timestamp with time zone NULL,
    "IsFailed" boolean NOT NULL DEFAULT FALSE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_MarketplaceEventOutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ProcessedEvents"
(
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "ConsumerName" character varying(200) NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "EntityId" uuid NOT NULL,
    "TenantId" uuid NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "ProcessedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_ProcessedEvents" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_EventId"
    ON "MarketplaceEventOutboxMessages" ("EventId");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_ProcessingStatus"
    ON "MarketplaceEventOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_OccurredAtUtc"
    ON "MarketplaceEventOutboxMessages" ("OccurredAtUtc");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_TenantId_EntityId"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "EntityId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProcessedEvents_EventId_ConsumerName"
    ON "ProcessedEvents" ("EventId", "ConsumerName");

CREATE INDEX IF NOT EXISTS "IX_ProcessedEvents_ProcessedAtUtc"
    ON "ProcessedEvents" ("ProcessedAtUtc");

CREATE INDEX IF NOT EXISTS "IX_ProcessedEvents_TenantId_EntityId"
    ON "ProcessedEvents" ("TenantId", "EntityId");

COMMIT;
