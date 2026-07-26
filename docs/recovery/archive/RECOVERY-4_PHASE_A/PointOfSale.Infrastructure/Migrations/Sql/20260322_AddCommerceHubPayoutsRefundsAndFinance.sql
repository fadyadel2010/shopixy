BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "ShippingMethodId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "ShippingMethodName" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "ShippingSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "PaymentMethod" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PaymentMethodCode" character varying(50) NOT NULL DEFAULT 'PAYMOB',
    ADD COLUMN IF NOT EXISTS "PaymentMethodName" character varying(100) NOT NULL DEFAULT 'Paymob',
    ADD COLUMN IF NOT EXISTS "PaymentSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "CommissionSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "IsEarningCreated" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "EarningCreatedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "VendorEarningAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" numeric(8,6) NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubVendorEarnings"
    ADD COLUMN IF NOT EXISTS "PaymentId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "PayoutId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "RefundId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "OriginalEarningId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "ReversedAtUtc" timestamp with time zone NULL;

CREATE TABLE IF NOT EXISTS "CommerceHubPayouts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Method" integer NOT NULL,
    "Reference" character varying(200) NULL,
    "ExternalReferenceId" character varying(200) NULL,
    "BankTransactionId" character varying(200) NULL,
    "ProcessedBy" character varying(200) NULL,
    "FailureReason" character varying(1000) NULL,
    "IdempotencyKey" character varying(200) NOT NULL,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPayouts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubPayoutItems" (
    "Id" uuid NOT NULL,
    "PayoutId" uuid NOT NULL,
    "EarningId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubPayoutItems" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "PaymentId" uuid NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CommissionReversalAmount" numeric(18,2) NOT NULL,
    "VendorEarningReversalAmount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Reference" character varying(200) NULL,
    "GatewayStatus" character varying(100) NULL,
    "FailureReason" character varying(1000) NULL,
    "IdempotencyKey" character varying(200) NOT NULL,
    "IsFullRefund" boolean NOT NULL DEFAULT false,
    "ProcessingStartedAtUtc" timestamp with time zone NULL,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubRefunds" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubFinancialAuditLogs"
    ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "PayoutId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "RefundId" uuid NULL;

ALTER TABLE "CommerceHubFinancialAuditLogs"
    ALTER COLUMN "Currency" TYPE character varying(10);

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
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId"
            FOREIGN KEY ("PayoutId")
            REFERENCES "CommerceHubPayouts" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId"
            FOREIGN KEY ("RefundId")
            REFERENCES "CommerceHubRefunds" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_OriginalEarningId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_OriginalEarningId"
            FOREIGN KEY ("OriginalEarningId")
            REFERENCES "CommerceHubVendorEarnings" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId'
    ) THEN
        ALTER TABLE "CommerceHubPayoutItems"
            ADD CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId"
            FOREIGN KEY ("PayoutId")
            REFERENCES "CommerceHubPayouts" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId'
    ) THEN
        ALTER TABLE "CommerceHubPayoutItems"
            ADD CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId"
            FOREIGN KEY ("EarningId")
            REFERENCES "CommerceHubVendorEarnings" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId'
    ) THEN
        ALTER TABLE "CommerceHubRefunds"
            ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId"
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
        WHERE conname = 'FK_CommerceHubRefunds_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubRefunds"
            ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

DROP INDEX IF EXISTS "IX_CommerceHubVendorEarnings_SubOrderId";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_SubOrderId"
    ON "CommerceHubVendorEarnings" ("SubOrderId")
    WHERE "OriginalEarningId" IS NULL AND "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_PaymentId"
    ON "CommerceHubVendorEarnings" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_PayoutId"
    ON "CommerceHubVendorEarnings" ("PayoutId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_RefundId"
    ON "CommerceHubVendorEarnings" ("RefundId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_OriginalEarningId"
    ON "CommerceHubVendorEarnings" ("OriginalEarningId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_ShippingMethodId"
    ON "CommerceHubSubOrders" ("ShippingMethodId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_TenantId_Status_CreatedAt"
    ON "CommerceHubPayouts" ("TenantId", "Status", "CreatedAt");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_TenantId_IdempotencyKey"
    ON "CommerceHubPayouts" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_ExternalReferenceId"
    ON "CommerceHubPayouts" ("ExternalReferenceId")
    WHERE "ExternalReferenceId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_BankTransactionId"
    ON "CommerceHubPayouts" ("BankTransactionId")
    WHERE "BankTransactionId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayoutItems_PayoutId"
    ON "CommerceHubPayoutItems" ("PayoutId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayoutItems_EarningId"
    ON "CommerceHubPayoutItems" ("EarningId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubPayoutItems_PayoutId_EarningId"
    ON "CommerceHubPayoutItems" ("PayoutId", "EarningId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_TenantId_CreatedAt"
    ON "CommerceHubRefunds" ("TenantId", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_TenantId_Status_CreatedAt"
    ON "CommerceHubRefunds" ("TenantId", "Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_SubOrderId_Status_CreatedAt"
    ON "CommerceHubRefunds" ("SubOrderId", "Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_PaymentId"
    ON "CommerceHubRefunds" ("PaymentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_TenantId_IdempotencyKey"
    ON "CommerceHubRefunds" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_TenantId"
    ON "CommerceHubFinancialAuditLogs" ("TenantId")
    WHERE "TenantId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_PaymentId"
    ON "CommerceHubFinancialAuditLogs" ("PaymentId")
    WHERE "PaymentId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_EarningId"
    ON "CommerceHubFinancialAuditLogs" ("EarningId")
    WHERE "EarningId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_PayoutId"
    ON "CommerceHubFinancialAuditLogs" ("PayoutId")
    WHERE "PayoutId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_RefundId"
    ON "CommerceHubFinancialAuditLogs" ("RefundId")
    WHERE "RefundId" IS NOT NULL;

COMMIT;
