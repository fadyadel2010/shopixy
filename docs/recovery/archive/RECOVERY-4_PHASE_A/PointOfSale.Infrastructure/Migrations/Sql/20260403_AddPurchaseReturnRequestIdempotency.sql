BEGIN;

CREATE TABLE IF NOT EXISTS "PurchaseReturnRequestIdempotency" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "PurchaseAttemptId" character varying(200) NULL,
    "RequestFingerprint" character varying(64) NOT NULL,
    "ReturnId" uuid NULL,
    "ResponseStatusCode" integer NULL,
    "ResponseJson" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PurchaseReturnRequestIdempotency" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PurchaseReturnRequestIdempotency_TenantId_Key"
    ON "PurchaseReturnRequestIdempotency" ("TenantId", "Key");

CREATE INDEX IF NOT EXISTS "IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId"
    ON "PurchaseReturnRequestIdempotency" ("TenantId", "ReturnId");

COMMIT;
