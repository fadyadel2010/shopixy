BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubMarketplaceOrders" (
    "Id" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "IdempotencyKey" character varying(100),
    "FullName" character varying(200) NOT NULL,
    "PhoneNumber" character varying(50) NOT NULL,
    "AddressLine" character varying(500) NOT NULL,
    "City" character varying(100) NOT NULL,
    "Area" character varying(100),
    "Notes" character varying(1000),
    "Currency" character varying(10) NOT NULL,
    "PaymentMethodCode" character varying(50) NOT NULL,
    "PaymentMethodName" character varying(100) NOT NULL,
    "PaymentReference" character varying(100),
    "PaymentReferenceId" character varying(100),
    "PaymentGateway" character varying(50),
    "Subtotal" numeric(18,2) NOT NULL,
    "ShippingTotal" numeric(18,2) NOT NULL,
    "TotalAmount" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "PaymentStatus" integer NOT NULL,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubMarketplaceOrders" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubSubOrders" (
    "Id" uuid NOT NULL,
    "MarketplaceOrderId" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "StoreId" uuid NOT NULL,
    "StoreName" character varying(200) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Subtotal" numeric(18,2) NOT NULL,
    "Shipping" numeric(18,2) NOT NULL,
    "Total" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "PaymentStatus" integer NOT NULL DEFAULT 0,
    "PaymentReferenceId" character varying(100),
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubSubOrders" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubSubOrderItems" (
    "Id" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "ListingId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "ProductName" character varying(250) NOT NULL,
    "VariantName" character varying(250),
    "VariantAttributesJson" text,
    "ImageUrl" character varying(1000),
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric(18,2) NOT NULL,
    "Total" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubSubOrderItems" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    ALTER TABLE "CommerceHubMarketplaceOrders"
        ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(100),
        ADD COLUMN IF NOT EXISTS "PaymentReferenceId" character varying(100),
        ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone;

    ALTER TABLE "CommerceHubSubOrders"
        ADD COLUMN IF NOT EXISTS "PaymentStatus" integer NOT NULL DEFAULT 0,
        ADD COLUMN IF NOT EXISTS "PaymentReferenceId" character varying(100),
        ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_MarketplaceOrderId'
    ) THEN
        ALTER TABLE "CommerceHubSubOrders"
            ADD CONSTRAINT "FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_MarketplaceOrderId"
            FOREIGN KEY ("MarketplaceOrderId")
            REFERENCES "CommerceHubMarketplaceOrders" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId'
    ) THEN
        ALTER TABLE "CommerceHubSubOrderItems"
            ADD CONSTRAINT "FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId"
            FOREIGN KEY ("SubOrderId")
            REFERENCES "CommerceHubSubOrders" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_OrderNumber"
    ON "CommerceHubMarketplaceOrders" ("OrderNumber");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_IdempotencyKey"
    ON "CommerceHubMarketplaceOrders" ("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_OrderNumber"
    ON "CommerceHubSubOrders" ("OrderNumber");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId"
    ON "CommerceHubSubOrders" ("MarketplaceOrderId", "StoreId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderItems_SubOrderId"
    ON "CommerceHubSubOrderItems" ("SubOrderId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderItems_ListingId"
    ON "CommerceHubSubOrderItems" ("ListingId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderItems_VariantId"
    ON "CommerceHubSubOrderItems" ("VariantId");

COMMIT;
