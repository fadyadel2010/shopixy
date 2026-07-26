-- Migration: S66_CommerceHubUnresolvedPaymentWebhookRecovery
-- Purpose: Preserve provider-success CommerceHub webhooks that cannot be linked to a local payment row.

CREATE TABLE IF NOT EXISTS "CommerceHubUnresolvedPaymentWebhooks" (
    "Id" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderEventId" character varying(200) NOT NULL,
    "ProviderReference" character varying(200) NULL,
    "ProviderIntentId" character varying(200) NULL,
    "ProviderTransactionId" character varying(200) NULL,
    "TenantId" uuid NULL,
    "OrderId" uuid NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "PayloadHash" character varying(64) NOT NULL,
    "RawPayload" text NOT NULL,
    "Status" character varying(50) NOT NULL DEFAULT 'PendingResolution',
    "ResolutionAttemptCount" integer NOT NULL DEFAULT 0,
    "LastResolutionAttemptAtUtc" timestamp with time zone NULL,
    "RoutedToSuspenseAtUtc" timestamp with time zone NULL,
    "CorrelationId" character varying(100) NULL,
    "LastError" character varying(2000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubUnresolvedPaymentWebhooks" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative" CHECK ("Amount" >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event"
    ON "CommerceHubUnresolvedPaymentWebhooks" ("Provider", "ProviderEventId")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status"
    ON "CommerceHubUnresolvedPaymentWebhooks" ("TenantId", "Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_OrderId"
    ON "CommerceHubUnresolvedPaymentWebhooks" ("OrderId")
    WHERE "OrderId" IS NOT NULL;

COMMENT ON TABLE "CommerceHubUnresolvedPaymentWebhooks" IS
    'Durable evidence for provider-success CommerceHub webhooks that could not be linked to a local payment row at receipt time.';
