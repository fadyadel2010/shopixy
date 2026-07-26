BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubRefundRequests" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "Quantity" integer NOT NULL,
    "UnitPriceSnapshot" numeric(18,2) NOT NULL,
    "TotalAmount" numeric(18,2) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubRefundRequests" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_OrderId'
    ) THEN
        ALTER TABLE "CommerceHubRefundRequests"
            ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_OrderId"
            FOREIGN KEY ("OrderId")
            REFERENCES "CommerceHubMarketplaceOrders" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderItemId'
    ) THEN
        ALTER TABLE "CommerceHubRefundRequests"
            ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderItemId"
            FOREIGN KEY ("OrderItemId")
            REFERENCES "CommerceHubSubOrderItems" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId'
    ) THEN
        ALTER TABLE "CommerceHubRefundRequests"
            ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId"
            FOREIGN KEY ("CustomerId")
            REFERENCES "CommerceHubCustomers" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_CustomerId_CreatedAt"
    ON "CommerceHubRefundRequests" ("CustomerId", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt"
    ON "CommerceHubRefundRequests" ("CustomerId", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt"
    ON "CommerceHubRefundRequests" ("OrderItemId", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt"
    ON "CommerceHubRefundRequests" ("VendorId", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_OrderId"
    ON "CommerceHubRefundRequests" ("OrderId");

COMMIT;
