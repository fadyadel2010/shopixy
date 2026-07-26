BEGIN;

CREATE TABLE IF NOT EXISTS "PurchaseRequestIdempotency" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "PurchaseInvoiceId" uuid NULL,
    "PurchaseAttemptId" character varying(200) NULL,
    "RequestFingerprint" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ResponseStatusCode" integer NULL,
    "ResponseJson" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PurchaseRequestIdempotency" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PurchaseRequestIdempotency_TenantId_Key_Operation"
    ON "PurchaseRequestIdempotency" ("TenantId", "Key", "Operation");

CREATE INDEX IF NOT EXISTS "IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId"
    ON "PurchaseRequestIdempotency" ("TenantId", "PurchaseInvoiceId");

COMMIT;
