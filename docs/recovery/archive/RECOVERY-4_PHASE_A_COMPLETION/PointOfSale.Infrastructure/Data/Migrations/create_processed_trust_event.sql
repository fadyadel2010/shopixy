-- Phase MR-1: Create ProcessedTrustEvent table for consumer idempotency
-- This table prevents duplicate processing of ServiceReviewCreatedEvent
-- in the ProviderTrustConsumer.

CREATE TABLE IF NOT EXISTS "ProcessedTrustEvent" (
    "Id"                uuid NOT NULL,
    "TenantId"          uuid NOT NULL,
    "EventId"           uuid NOT NULL,
    "EventType"         varchar(100) NOT NULL,
    "ProviderProfileId" uuid NOT NULL,
    "ProcessedAt"       timestamp without time zone NOT NULL DEFAULT (now() at time zone 'utc'),
    "CreatedAt"         timestamp without time zone NOT NULL DEFAULT (now() at time zone 'utc'),
    "UpdatedAt"         timestamp without time zone NULL,
    "CreatedBy"         uuid NULL,
    "UpdatedBy"         uuid NULL,
    "IsDeleted"         boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_ProcessedTrustEvent" PRIMARY KEY ("Id")
);

-- Unique index on EventId to enforce idempotency
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProcessedTrustEvent_EventId"
    ON "ProcessedTrustEvent" ("EventId");

-- Index for querying by provider
CREATE INDEX IF NOT EXISTS "IX_ProcessedTrustEvent_ProviderProfileId"
    ON "ProcessedTrustEvent" ("ProviderProfileId");

-- Index for tenant queries
CREATE INDEX IF NOT EXISTS "IX_ProcessedTrustEvent_TenantId"
    ON "ProcessedTrustEvent" ("TenantId");
