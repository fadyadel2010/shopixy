BEGIN;

ALTER TABLE "ProductVariantStocks"
    ADD COLUMN IF NOT EXISTS "DamagedQuantity" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "ReservedQuantityChange" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "DamagedQuantityChange" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "AvailableAfter" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "ReservedAfter" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "DamagedAfter" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "ReferenceLineId" uuid NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "ReservationId" uuid NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "InventoryEventId" uuid NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "InventoryEventType" integer NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "ReferenceType" character varying(50) NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "CorrelationId" character varying(100) NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(200) NULL;

ALTER TABLE "StockMovements"
    ADD COLUMN IF NOT EXISTS "Source" character varying(100) NULL;

ALTER TABLE "CommerceHubReturnRequests"
    ADD COLUMN IF NOT EXISTS "RequestedQuantity" integer NOT NULL DEFAULT 1;

ALTER TABLE "CommerceHubReturnRequests"
    ADD COLUMN IF NOT EXISTS "ReceivedResellableQuantity" integer NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubReturnRequests"
    ADD COLUMN IF NOT EXISTS "ReceivedDamagedQuantity" integer NOT NULL DEFAULT 0;

UPDATE "CommerceHubReturnRequests" rr
SET "RequestedQuantity" = COALESCE(fr."Quantity", rr."RequestedQuantity")
FROM "CommerceHubRefundRequests" fr
WHERE rr."RefundId" = fr."Id";

CREATE TABLE IF NOT EXISTS "InventoryReservations"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "SubOrderId" uuid NULL,
    "OrderItemId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ReservedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "CommittedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "ReleasedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "ReturnedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "DamagedReturnedQuantity" numeric(18,2) NOT NULL DEFAULT 0,
    "Status" integer NOT NULL,
    "ReservationEventId" uuid NOT NULL,
    "LastMutationEventId" uuid NULL,
    "ExpiresAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_InventoryReservations" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "InventoryEventInbox"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" integer NOT NULL,
    "ReferenceId" uuid NULL,
    "ReferenceLineId" uuid NULL,
    "PayloadHash" character varying(128) NULL,
    "FirstReceivedAtUtc" timestamp with time zone NOT NULL,
    "LastReceivedAtUtc" timestamp with time zone NULL,
    "AppliedAtUtc" timestamp with time zone NULL,
    "Status" integer NOT NULL,
    "ReplayCount" integer NOT NULL DEFAULT 0,
    "StockMovementId" uuid NULL,
    "CorrelationId" character varying(100) NULL,
    "IdempotencyKey" character varying(200) NULL,
    "Source" character varying(100) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_InventoryEventInbox" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_InventoryEventInbox_TenantId_EventId"
    ON "InventoryEventInbox" ("TenantId", "EventId");

CREATE INDEX IF NOT EXISTS "IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType"
    ON "InventoryEventInbox" ("TenantId", "ReferenceLineId", "EventType");

CREATE INDEX IF NOT EXISTS "IX_InventoryReservations_TenantId_OrderItemId_VariantId_WarehouseId"
    ON "InventoryReservations" ("TenantId", "OrderItemId", "VariantId", "WarehouseId");

CREATE INDEX IF NOT EXISTS "IX_InventoryReservations_TenantId_Status_ExpiresAtUtc"
    ON "InventoryReservations" ("TenantId", "Status", "ExpiresAtUtc");

CREATE INDEX IF NOT EXISTS "IX_StockMovements_TenantId_InventoryEventId"
    ON "StockMovements" ("TenantId", "InventoryEventId");

CREATE INDEX IF NOT EXISTS "IX_StockMovements_ReferenceLineId_InventoryEventType"
    ON "StockMovements" ("ReferenceLineId", "InventoryEventType");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_ProductVariantStocks_Quantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "CK_ProductVariantStocks_Quantity_NonNegative"
            CHECK ("Quantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_ProductVariantStocks_ReservedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_NonNegative"
            CHECK ("ReservedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_ProductVariantStocks_DamagedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "CK_ProductVariantStocks_DamagedQuantity_NonNegative"
            CHECK ("DamagedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_ProductVariantStocks_ReservedQuantity_Within_Quantity'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_Within_Quantity"
            CHECK ("Quantity" >= "ReservedQuantity");
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CommerceHubReturnRequests_RequestedQuantity_Positive'
    ) THEN
        ALTER TABLE "CommerceHubReturnRequests"
            ADD CONSTRAINT "CK_CommerceHubReturnRequests_RequestedQuantity_Positive"
            CHECK ("RequestedQuantity" > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNegative'
    ) THEN
        ALTER TABLE "CommerceHubReturnRequests"
            ADD CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNegative"
            CHECK ("ReceivedResellableQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "CommerceHubReturnRequests"
            ADD CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegative"
            CHECK ("ReceivedDamagedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested'
    ) THEN
        ALTER TABLE "CommerceHubReturnRequests"
            ADD CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested"
            CHECK ("ReceivedResellableQuantity" + "ReceivedDamagedQuantity" <= "RequestedQuantity");
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_ReservedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_ReservedQuantity_NonNegative"
            CHECK ("ReservedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_CommittedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_CommittedQuantity_NonNegative"
            CHECK ("CommittedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_ReleasedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_ReleasedQuantity_NonNegative"
            CHECK ("ReleasedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_ReturnedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_ReturnedQuantity_NonNegative"
            CHECK ("ReturnedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_DamagedReturnedQuantity_NonNegative'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_DamagedReturnedQuantity_NonNegative"
            CHECK ("DamagedReturnedQuantity" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_ReservedFlow_Valid'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_ReservedFlow_Valid"
            CHECK ("CommittedQuantity" + "ReleasedQuantity" <= "ReservedQuantity");
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryReservations_Returns_DoNotExceedCommitted'
    ) THEN
        ALTER TABLE "InventoryReservations"
            ADD CONSTRAINT "CK_InventoryReservations_Returns_DoNotExceedCommitted"
            CHECK ("ReturnedQuantity" + "DamagedReturnedQuantity" <= "CommittedQuantity");
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryEventInbox_ReplayCount_NonNegative'
    ) THEN
        ALTER TABLE "InventoryEventInbox"
            ADD CONSTRAINT "CK_InventoryEventInbox_ReplayCount_NonNegative"
            CHECK ("ReplayCount" >= 0);
    END IF;
END $$;

COMMIT;
