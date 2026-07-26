CREATE TABLE IF NOT EXISTS "OrderRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "RefundId" character varying(120) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "IsFullRefund" boolean NOT NULL DEFAULT false,
    "Reason" character varying(1000) NOT NULL,
    "PaymentMethod" character varying(100) NOT NULL,
    "IsPostSettlementRefund" boolean NOT NULL DEFAULT false,
    "SettlementBatchId" uuid NULL,
    "SettlementType" character varying(32) NULL,
    "SettlementId" character varying(200) NULL,
    "PayoutBatchId" character varying(200) NULL,
    "SettledAtUtc" timestamp with time zone NULL,
    "RefundedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_OrderRefunds" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "OrderRefundLines" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderRefundId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_OrderRefundLines" PRIMARY KEY ("Id")
);

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "IsPostSettlementRefund" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "SettlementBatchId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "SettlementType" character varying(32) NULL,
    ADD COLUMN IF NOT EXISTS "SettlementId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "PayoutBatchId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "SettledAtUtc" timestamp with time zone NULL;

ALTER TABLE "OrderRefundLines"
    ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "OrderId" uuid NULL;

UPDATE "OrderRefundLines" AS refund_line
SET
    "TenantId" = COALESCE(refund_line."TenantId", refund."TenantId"),
    "OrderId" = COALESCE(refund_line."OrderId", refund."OrderId"),
    "UpdatedAt" = NOW()
FROM "OrderRefunds" AS refund
WHERE refund_line."OrderRefundId" = refund."Id"
  AND (refund_line."TenantId" IS NULL OR refund_line."OrderId" IS NULL);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_OrderRefunds_Orders_OrderId'
    ) THEN
        ALTER TABLE "OrderRefunds"
            ADD CONSTRAINT "FK_OrderRefunds_Orders_OrderId"
            FOREIGN KEY ("OrderId")
            REFERENCES "Orders" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_OrderRefundLines_OrderRefunds_OrderRefundId'
    ) THEN
        ALTER TABLE "OrderRefundLines"
            ADD CONSTRAINT "FK_OrderRefundLines_OrderRefunds_OrderRefundId"
            FOREIGN KEY ("OrderRefundId")
            REFERENCES "OrderRefunds" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_OrderRefundLines_OrderItems_OrderItemId'
    ) THEN
        ALTER TABLE "OrderRefundLines"
            ADD CONSTRAINT "FK_OrderRefundLines_OrderItems_OrderItemId"
            FOREIGN KEY ("OrderItemId")
            REFERENCES "OrderItems" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_OrderRefunds_TenantId_OrderId"
    ON "OrderRefunds" ("TenantId", "OrderId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderRefunds_TenantId_OrderId_RefundId"
    ON "OrderRefunds" ("TenantId", "OrderId", "RefundId")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_OrderRefunds_TenantId_SettlementBatchId"
    ON "OrderRefunds" ("TenantId", "SettlementBatchId")
    WHERE "SettlementBatchId" IS NOT NULL AND "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_OrderRefundLines_OrderRefundId"
    ON "OrderRefundLines" ("OrderRefundId");

CREATE INDEX IF NOT EXISTS "IX_OrderRefundLines_OrderItemId"
    ON "OrderRefundLines" ("OrderItemId");

CREATE INDEX IF NOT EXISTS "IX_OrderRefundLines_TenantId_IsDeleted"
    ON "OrderRefundLines" ("TenantId", "IsDeleted");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderRefundLines_Refund_OrderItem"
    ON "OrderRefundLines" ("OrderRefundId", "OrderItemId")
    WHERE "IsDeleted" = false;
