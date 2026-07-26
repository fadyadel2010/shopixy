-- S69: Production fixes for cross-system critical-risk closure.
-- 1. Persist unresolved CommerceHub provider-success tenant scope so real gateway
--    money can be routed to tenant suspense even for multi-vendor orders.
-- 2. Add durable routed-suspense scope for recovery and reconciliation audits.

CREATE TABLE IF NOT EXISTS "CommerceHubUnresolvedPaymentWebhooks" (
    "Id" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderEventId" character varying(200) NOT NULL,
    "ProviderReference" character varying(200) NULL,
    "LocalPaymentId" uuid NULL,
    "ProviderIntentId" character varying(200) NULL,
    "ProviderTransactionId" character varying(200) NULL,
    "TenantId" uuid NULL,
    "OrderId" uuid NULL,
    "AffectedStoreIdsJson" text NOT NULL DEFAULT '[]',
    "SuspenseRoutedStoreIdsJson" text NOT NULL DEFAULT '[]',
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
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubUnresolvedPaymentWebhooks" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative" CHECK ("Amount" >= 0)
);

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    ADD COLUMN IF NOT EXISTS "AffectedStoreIdsJson" text NOT NULL DEFAULT '[]';

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    ADD COLUMN IF NOT EXISTS "SuspenseRoutedStoreIdsJson" text NOT NULL DEFAULT '[]';

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    ADD COLUMN IF NOT EXISTS "LocalPaymentId" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event"
ON "CommerceHubUnresolvedPaymentWebhooks" ("Provider", "ProviderEventId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status"
ON "CommerceHubUnresolvedPaymentWebhooks" ("TenantId", "Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_OrderId"
ON "CommerceHubUnresolvedPaymentWebhooks" ("OrderId")
WHERE "OrderId" IS NOT NULL;

UPDATE "CommerceHubUnresolvedPaymentWebhooks"
SET "AffectedStoreIdsJson" = CASE
        WHEN "TenantId" IS NULL THEN '[]'
        ELSE jsonb_build_array("TenantId")::text
    END
WHERE NULLIF(btrim("AffectedStoreIdsJson"), '') IS NULL
   OR (
        "TenantId" IS NOT NULL
        AND jsonb_typeof("AffectedStoreIdsJson"::jsonb) = 'array'
        AND jsonb_array_length("AffectedStoreIdsJson"::jsonb) = 0
    );

UPDATE "CommerceHubUnresolvedPaymentWebhooks"
SET "SuspenseRoutedStoreIdsJson" = CASE
        WHEN "RoutedToSuspenseAtUtc" IS NULL OR "TenantId" IS NULL THEN '[]'
        ELSE jsonb_build_array("TenantId")::text
    END
WHERE NULLIF(btrim("SuspenseRoutedStoreIdsJson"), '') IS NULL
   OR (
        "RoutedToSuspenseAtUtc" IS NOT NULL
        AND "TenantId" IS NOT NULL
        AND jsonb_typeof("SuspenseRoutedStoreIdsJson"::jsonb) = 'array'
        AND jsonb_array_length("SuspenseRoutedStoreIdsJson"::jsonb) = 0
    );

-- Logical constraint names:
-- CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Array
-- CK_CommerceHubUnresolvedPaymentWebhooks_SuspenseRoutedStoreIdsJson_Array
-- CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusScope
-- CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolutionNoSuspenseRoute
-- PostgreSQL identifiers are capped at 63 bytes, so the actual constraint names below are explicit short forms.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Ar'
    ) THEN
        ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
            ADD CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Ar"
            CHECK (jsonb_typeof("AffectedStoreIdsJson"::jsonb) = 'array')
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubUnresolvedPaymentWebhooks_SuspenseRoutedStoreIdsJ'
    ) THEN
        ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
            ADD CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_SuspenseRoutedStoreIdsJ"
            CHECK (jsonb_typeof("SuspenseRoutedStoreIdsJson"::jsonb) = 'array')
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusSco'
    ) THEN
        ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
            ADD CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusSco"
            CHECK (
                "Status" <> 'RoutedToSuspense'
                OR (
                    "RoutedToSuspenseAtUtc" IS NOT NULL
                    AND jsonb_array_length("AffectedStoreIdsJson"::jsonb) > 0
                    AND "SuspenseRoutedStoreIdsJson"::jsonb = "AffectedStoreIdsJson"::jsonb
                )
            )
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolution'
    ) THEN
        ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
            ADD CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolution"
            CHECK (
                "Status" <> 'PendingTenantResolution'
                OR (
                    "RoutedToSuspenseAtUtc" IS NULL
                    AND "SuspenseRoutedStoreIdsJson"::jsonb = '[]'::jsonb
                )
            )
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    VALIDATE CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Ar";

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    VALIDATE CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_SuspenseRoutedStoreIdsJ";

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    VALIDATE CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusSco";

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    VALIDATE CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolution";

DROP INDEX IF EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreScope";

CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreScope"
ON "CommerceHubUnresolvedPaymentWebhooks" USING gin (("AffectedStoreIdsJson"::jsonb));

-- 3. Prevent duplicate external refunds after a worker/process crash between
--    gateway side effect and local outcome persistence. Once the external call
--    may have started without a durable gateway reference/status, automatic
--    retries are stopped and outward money remains blocked until reconciliation.

CREATE TABLE IF NOT EXISTS "CommerceHubPaymentCompensationOutboxMessages"
(
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "TenantId" uuid NULL,
    "AffectedStoreIdsJson" text NOT NULL DEFAULT '[]',
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
    "GatewayRequestStartedAtUtc" timestamp with time zone NULL,
    "GatewayOutcomeUncertainAtUtc" timestamp with time zone NULL,
    "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPaymentCompensationOutboxMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CHPaymentCompOutbox_Amount" CHECK ("Amount" > 0)
);

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    ADD COLUMN IF NOT EXISTS "GatewayRequestStartedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    ADD COLUMN IF NOT EXISTS "GatewayOutcomeUncertainAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    ADD COLUMN IF NOT EXISTS "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT false;

UPDATE "CommerceHubPaymentCompensationOutboxMessages"
SET "RequiresOperatorReconciliation" = true,
    "IsFailed" = true,
    "GatewayOutcomeUncertainAtUtc" = COALESCE("GatewayOutcomeUncertainAtUtc", NOW()),
    "NextRetryAtUtc" = NULL,
    "ProcessingLeaseId" = NULL,
    "ProcessingLeaseExpiresAtUtc" = NULL,
    "LastError" = COALESCE(
        NULLIF("LastError", ''),
        'Gateway refund outcome is uncertain and requires operator reconciliation before automatic retry.'
    )
WHERE "ProcessedAtUtc" IS NULL
  AND "GatewayRequestStartedAtUtc" IS NOT NULL
  AND NULLIF(btrim(COALESCE("GatewayReference", '')), '') IS NULL
  AND NULLIF(btrim(COALESCE("GatewayStatus", '')), '') IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CHPaymentCompOutbox_UncertainOutcomeRequiresOperator'
    ) THEN
        ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
            ADD CONSTRAINT "CK_CHPaymentCompOutbox_UncertainOutcomeRequiresOperator"
            CHECK (
                "GatewayOutcomeUncertainAtUtc" IS NULL
                OR (
                    "RequiresOperatorReconciliation" = true
                    AND "ProcessedAtUtc" IS NULL
                    AND "IsFailed" = true
                )
            )
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    VALIDATE CONSTRAINT "CK_CHPaymentCompOutbox_UncertainOutcomeRequiresOperator";

CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_OperatorReconciliation"
ON "CommerceHubPaymentCompensationOutboxMessages"
    ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt");

-- 3b. Support fail-closed readiness checks for external refunds whose provider
--     outcome is unknown or stuck in processing. These refunds can represent
--     real provider cash movement before local financial reversal is complete.

CREATE TABLE IF NOT EXISTS "CommerceHubRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "RefundRequestId" uuid NULL,
    "OrderItemId" uuid NULL,
    "Quantity" integer NULL,
    "PaymentId" uuid NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CommissionReversalAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "PromoDiscountReversalAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "VendorEarningReversalAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Reference" character varying(200) NULL,
    "GatewayStatus" character varying(100) NULL,
    "FailureReason" character varying(1000) NULL,
    "IdempotencyKey" character varying(200) NOT NULL,
    "IsFullRefund" boolean NOT NULL DEFAULT false,
    "IsPostPayoutAdjustment" boolean NOT NULL DEFAULT false,
    "ProcessingStartedAtUtc" timestamp with time zone NULL,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubRefunds" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "ProcessingStartedAtUtc" timestamp with time zone NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_ExternalReconciliation"
ON "CommerceHubRefunds" (
    "TenantId",
    "Status",
    "ProcessingStartedAtUtc",
    "CreatedAt"
)
WHERE "IsDeleted" = false
  AND "Status" = 1;

-- 4. Make critical integration-outbox poison durable and operator-visible.
--    Exhausted authoritative events remain fail-closed for ordering, while the
--    publisher continues slow replay so downstream systems are not left stale
--    forever after a transient outage.

ALTER TABLE "OutboxMessages"
    ADD COLUMN IF NOT EXISTS "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT false;

ALTER TABLE "OutboxMessages"
    ADD COLUMN IF NOT EXISTS "OperatorReconciliationRequiredAtUtc" timestamp with time zone NULL;

ALTER TABLE "OutboxMessages"
    ADD COLUMN IF NOT EXISTS "OperatorReconciliationReason" character varying(2000) NULL;

UPDATE "OutboxMessages"
SET "RequiresOperatorReconciliation" = true,
    "OperatorReconciliationRequiredAtUtc" = COALESCE("OperatorReconciliationRequiredAtUtc", NOW()),
    "OperatorReconciliationReason" = COALESCE(
        NULLIF("OperatorReconciliationReason", ''),
        'Critical authoritative event exhausted automatic publish retries and is blocking its aggregate stream.'
    )
WHERE "ProcessedAtUtc" IS NULL
  AND "RetryCount" >= 5
  AND (
        lower("EventType") LIKE '%order%'
     OR lower("EventType") LIKE '%payment%'
     OR lower("EventType") LIKE '%inventory%'
     OR lower("EventType") LIKE '%stock%'
     OR lower("EventType") LIKE '%product%'
     OR lower("EventType") LIKE '%variant%'
     OR lower("EventType") LIKE '%commercehub%'
     OR lower("EventType") LIKE '%marketplace%'
     OR lower("EventType") LIKE '%affiliate%'
     OR lower("EventType") LIKE '%coupon%'
     OR lower("EventType") LIKE '%promotion%'
     OR lower("EventType") LIKE '%tenant%'
     OR lower("EventType") LIKE '%storeconfig%'
     OR lower("EventType") LIKE '%shippingmethod%'
     OR lower("EventType") LIKE '%shipment%'
     OR lower("EventType") LIKE '%customer%'
     OR lower("EventType") LIKE '%checkout%'
     OR lower("EventType") LIKE '%refund%'
     OR lower("EventType") LIKE '%payout%'
     OR lower("EventType") LIKE '%settlement%'
     OR lower("EventType") LIKE '%ledger%'
     OR lower("EventType") LIKE '%accounting%'
     OR lower("EventType") LIKE '%wallet%'
     OR lower("EventType") LIKE '%cod%'
     OR lower("EventType") LIKE '%currency%'
     OR lower("EventType") LIKE '%providertrust%'
  )
  AND lower("EventType") NOT LIKE '%cartactivityevent%'
  AND lower("EventType") NOT LIKE '%abandonedcart%'
  AND lower("EventType") NOT LIKE '%lowstock%'
  AND lower("EventType") NOT LIKE '%email%'
  AND lower("EventType") NOT LIKE '%whatsapp%'
  AND lower("EventType") NOT LIKE '%notification%'
  AND lower("EventType") NOT LIKE '%click%'
  AND lower("EventType") NOT LIKE '%impression%';

CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_OperatorReconciliation"
ON "OutboxMessages"
    ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAtUtc")
WHERE "RequiresOperatorReconciliation" = true AND "ProcessedAtUtc" IS NULL;
