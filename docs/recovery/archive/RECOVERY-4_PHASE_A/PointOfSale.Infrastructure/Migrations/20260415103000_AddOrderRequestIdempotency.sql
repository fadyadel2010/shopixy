BEGIN;

CREATE TABLE IF NOT EXISTS "OrderRequestIdempotency"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "OrderId" uuid NULL,
    "ResponseStatusCode" integer NULL,
    -- ResponsePayload stores a minimal replay envelope (OrderIdempotencyReplayDto)
    -- that excludes sensitive PII like email, phone, addresses, and risk data.
    -- For 201 Created responses, the full order details are fetched from the Orders table during replay.
    "ResponsePayload" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "ClaimToken" character varying(32) NULL,
    "ClaimTimestampUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_OrderRequestIdempotency" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderRequestIdempotency_TenantId_Key"
    ON "OrderRequestIdempotency" ("TenantId", "Key");

CREATE INDEX IF NOT EXISTS "IX_OrderRequestIdempotency_TenantId_OrderId"
    ON "OrderRequestIdempotency" ("TenantId", "OrderId");

CREATE INDEX IF NOT EXISTS "IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted"
    ON "OrderRequestIdempotency" ("CompletedAtUtc", "IsDeleted")
    WHERE "CompletedAtUtc" IS NOT NULL;

COMMIT;

-- Optional rollback:
-- DROP TABLE IF EXISTS "OrderRequestIdempotency";
