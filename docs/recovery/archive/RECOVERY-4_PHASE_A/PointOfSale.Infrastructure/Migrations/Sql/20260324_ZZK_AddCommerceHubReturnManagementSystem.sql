BEGIN;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ReturnRequestId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "RefundExecutionTrigger" integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS "CommerceHubReturnRequests"
(
    "Id" uuid NOT NULL,
    "RefundId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "PickupScheduledAt" timestamp with time zone NULL,
    "PickedUpAt" timestamp with time zone NULL,
    "InTransitAt" timestamp with time zone NULL,
    "DeliveredAt" timestamp with time zone NULL,
    "ConfirmedAt" timestamp with time zone NULL,
    "TrackingNumber" character varying(200) NULL,
    "CourierName" character varying(200) NULL,
    "Notes" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_CommerceHubReturnRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubRefundRequests_RefundId"
        FOREIGN KEY ("RefundId")
        REFERENCES "CommerceHubRefundRequests" ("Id")
        ON DELETE RESTRICT,
    CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubSubOrderItems_OrderItemId"
        FOREIGN KEY ("OrderItemId")
        REFERENCES "CommerceHubSubOrderItems" ("Id")
        ON DELETE RESTRICT,
    CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubCustomers_CustomerId"
        FOREIGN KEY ("CustomerId")
        REFERENCES "CommerceHubCustomers" ("Id")
        ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_RefundId"
    ON "CommerceHubReturnRequests" ("RefundId")
    WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt"
    ON "CommerceHubReturnRequests" ("CustomerId", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt"
    ON "CommerceHubReturnRequests" ("VendorId", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt"
    ON "CommerceHubReturnRequests" ("OrderItemId", "Status", "CreatedAt" DESC);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_ReturnRequestId"
    ON "CommerceHubRefundRequests" ("ReturnRequestId")
    WHERE "ReturnRequestId" IS NOT NULL;

COMMIT;
