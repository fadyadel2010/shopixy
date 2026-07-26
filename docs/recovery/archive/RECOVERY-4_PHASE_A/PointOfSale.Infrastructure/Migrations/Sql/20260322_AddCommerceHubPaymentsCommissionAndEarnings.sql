BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "CommissionSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "IsEarningCreated" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "EarningCreatedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "VendorEarningAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" numeric(8,6) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS "CommerceHubPayments" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderReference" character varying(100) NOT NULL,
    "ProviderIntentId" character varying(200) NULL,
    "ProviderTransactionId" character varying(200) NULL,
    "CheckoutUrl" character varying(2000) NULL,
    "PaymentToken" character varying(500) NULL,
    "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "FailureReason" character varying(1000) NULL,
    "IsProcessed" boolean NOT NULL DEFAULT false,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "PaidAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPayments" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubPayments"
    ADD COLUMN IF NOT EXISTS "IsProcessed" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "ProcessedAtUtc" timestamp with time zone NULL;

CREATE TABLE IF NOT EXISTS "CommerceHubPaymentAttempts" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "AttemptNumber" integer NOT NULL,
    "ProviderReference" character varying(100) NOT NULL,
    "ProviderIntentId" character varying(200) NULL,
    "ProviderTransactionId" character varying(200) NULL,
    "Status" integer NOT NULL,
    "FailureReason" character varying(1000) NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPaymentAttempts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubPaymentItems" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPaymentItems" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubPaymentLogs" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "ProviderEventId" character varying(100) NULL,
    "RawResponse" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPaymentLogs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubVendorEarnings" (
    "Id" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "PaymentId" uuid NULL,
    "TenantId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "AvailableAtUtc" timestamp with time zone NULL,
    "PaidOutAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubVendorEarnings" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubVendorEarnings"
    ADD COLUMN IF NOT EXISTS "PaymentId" uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId'
    ) THEN
        ALTER TABLE "CommerceHubPayments"
            ADD CONSTRAINT "FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId"
            FOREIGN KEY ("OrderId")
            REFERENCES "CommerceHubMarketplaceOrders" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubPaymentAttempts"
            ADD CONSTRAINT "FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubPaymentItems"
            ADD CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId'
    ) THEN
        ALTER TABLE "CommerceHubPaymentItems"
            ADD CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId"
            FOREIGN KEY ("SubOrderId")
            REFERENCES "CommerceHubSubOrders" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubPaymentLogs"
            ADD CONSTRAINT "FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId"
            FOREIGN KEY ("SubOrderId")
            REFERENCES "CommerceHubSubOrders" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayments_OrderId"
    ON "CommerceHubPayments" ("OrderId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPayments_OrderId_SinglePaid"
    ON "CommerceHubPayments" ("OrderId")
    WHERE "Status" = 1 AND "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayments_OrderId_Status"
    ON "CommerceHubPayments" ("OrderId", "Status");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPayments_ProviderReference"
    ON "CommerceHubPayments" ("ProviderReference");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPayments_ProviderTransactionId"
    ON "CommerceHubPayments" ("ProviderTransactionId")
    WHERE "ProviderTransactionId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayments_Provider_ProviderIntentId"
    ON "CommerceHubPayments" ("Provider", "ProviderIntentId")
    WHERE "ProviderIntentId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentAttempts_PaymentId"
    ON "CommerceHubPaymentAttempts" ("PaymentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPaymentAttempts_PaymentId_AttemptNumber"
    ON "CommerceHubPaymentAttempts" ("PaymentId", "AttemptNumber");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentAttempts_Provider_ProviderIntentId"
    ON "CommerceHubPaymentAttempts" ("Provider", "ProviderIntentId")
    WHERE "ProviderIntentId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentAttempts_ProviderTransactionId"
    ON "CommerceHubPaymentAttempts" ("ProviderTransactionId")
    WHERE "ProviderTransactionId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentItems_PaymentId"
    ON "CommerceHubPaymentItems" ("PaymentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPaymentItems_PaymentId_SubOrderId"
    ON "CommerceHubPaymentItems" ("PaymentId", "SubOrderId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentItems_SubOrderId"
    ON "CommerceHubPaymentItems" ("SubOrderId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentLogs_PaymentId"
    ON "CommerceHubPaymentLogs" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPaymentLogs_PaymentId_ProviderEventId"
    ON "CommerceHubPaymentLogs" ("PaymentId", "ProviderEventId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_SubOrderId"
    ON "CommerceHubVendorEarnings" ("SubOrderId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_PaymentId"
    ON "CommerceHubVendorEarnings" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_TenantId_Status_CreatedAt"
    ON "CommerceHubVendorEarnings" ("TenantId", "Status", "CreatedAt");

COMMIT;
