-- ============================================================================
-- Migration: AddCheckoutSessions
-- Phase: Abandoned Checkout (Lost Orders)
-- Run this in PgAdmin4 against your PointOfSale database
-- ============================================================================

BEGIN;

-- 1. Create CheckoutSessions table
CREATE TABLE IF NOT EXISTS "CheckoutSessions" (
    "Id" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "SessionId" character varying(100),
    "OnlineCustomerId" uuid,
    "CustomerName" character varying(200),
    "Phone" character varying(20),
    "Email" character varying(255),
    "City" character varying(100),
    "Area" character varying(100),
    "Address" character varying(500),
    "Notes" character varying(500),
    "ItemsSnapshotJson" text NOT NULL,
    "CartTotal" numeric(18,4),
    "Currency" character varying(3),
    "ShippingMethodId" uuid,
    "PaymentMethodCode" character varying(50),
    "Status" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "LastActivityAtUtc" timestamp with time zone NOT NULL,
    "AbandonedAtUtc" timestamp with time zone,
    "ConvertedAtUtc" timestamp with time zone,
    "ConvertedOrderId" uuid,
    "ConvertedOrderNumber" character varying(50),
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_CheckoutSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CheckoutSessions_Tenants_TenantId" FOREIGN KEY ("TenantId")
        REFERENCES "Tenants" ("Id") ON DELETE CASCADE
);

-- 2. Create indexes for CheckoutSessions
CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_CartId"
    ON "CheckoutSessions" ("CartId");

CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_Status_LastActivity"
    ON "CheckoutSessions" ("Status", "LastActivityAtUtc");

CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_TenantId"
    ON "CheckoutSessions" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_TenantId_CartId"
    ON "CheckoutSessions" ("TenantId", "CartId");

-- 3. Record this migration in EF Core's history table so future migrations work correctly
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260312135219_AddCheckoutSessions', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260312135219_AddCheckoutSessions'
);

COMMIT;

-- ✅ Done! The CheckoutSessions table is now ready.
