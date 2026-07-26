CREATE TABLE IF NOT EXISTS "CommerceHubPaymentCompensationOutboxMessages"
(
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "TenantId" uuid NULL,
    "AffectedStoreIdsJson" character varying(2000) NOT NULL DEFAULT '[]',
    "Provider" integer NOT NULL,
    "ProviderIntentId" character varying(200) NULL,
    "ProviderTransactionId" character varying(200) NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "IdempotencyKey" character varying(200) NOT NULL,
    "CorrelationId" character varying(100) NULL,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "LastError" character varying(2000) NULL,
    "NextRetryAtUtc" timestamp with time zone NULL,
    "ProcessingLeaseId" uuid NULL,
    "ProcessingLeaseExpiresAtUtc" timestamp with time zone NULL,
    "IsFailed" boolean NOT NULL DEFAULT false,
    "GatewayReference" character varying(200) NULL,
    "GatewayStatus" character varying(100) NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPaymentCompensationOutboxMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CHPaymentCompOutbox_Amount" CHECK ("Amount" > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHPaymentCompOutbox_IdempotencyKey"
    ON "CommerceHubPaymentCompensationOutboxMessages" ("IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_Processing"
    ON "CommerceHubPaymentCompensationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc");

CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_ProcessingLease"
    ON "CommerceHubPaymentCompensationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ProcessingLeaseExpiresAtUtc", "CreatedAt", "Id");

CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_PaymentId"
    ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_PaymentOrdering"
    ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId", "ProcessedAtUtc", "CreatedAt", "Id")
    WHERE "ProcessedAtUtc" IS NULL AND "IsDeleted" = false;
