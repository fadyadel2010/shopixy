-- ============================================================================
-- PROMOTIONS ENGINE MIGRATION
-- Phase: Promotions Engine (Offers + Unified Discounts)
-- 
-- This migration adds:
-- 1. Promotions table (auto-applied offers)
-- 2. PromotionUsages table (tracks offer applications per order)
-- 3. Order fields for tracking applied promotions
-- 
-- Run this in PgAdmin4 against your PointOfSale database
-- ============================================================================

-- ============================================================================
-- 1. CREATE PROMOTIONS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS "Promotions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(500),
    "DiscountType" integer NOT NULL DEFAULT 0,
    "DiscountValue" numeric(18,2) NOT NULL,
    "MaxDiscountAmount" numeric(18,2),
    "Scope" integer NOT NULL DEFAULT 0,
    "AppliesToId" uuid,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone NOT NULL,
    "Priority" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "IsExclusive" boolean NOT NULL DEFAULT FALSE,
    "MinOrderAmount" numeric(18,2),
    "MaxUsage" integer,
    "CurrentUsage" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_Promotions" PRIMARY KEY ("Id")
);

-- Indexes for Promotions
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Promotions_TenantId_Name" 
ON "Promotions" ("TenantId", "Name") 
WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_Promotions_TenantId_IsActive_StartDate_EndDate" 
ON "Promotions" ("TenantId", "IsActive", "StartDate", "EndDate");

CREATE INDEX IF NOT EXISTS "IX_Promotions_TenantId_Scope_AppliesToId_IsActive" 
ON "Promotions" ("TenantId", "Scope", "AppliesToId", "IsActive");

CREATE INDEX IF NOT EXISTS "IX_Promotions_Priority" 
ON "Promotions" ("Priority");

-- ============================================================================
-- 2. CREATE PROMOTIONUSAGES TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS "PromotionUsages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "CustomerId" uuid,
    "PromotionName" character varying(200) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "OrderSubtotal" numeric(18,2) NOT NULL,
    "AppliedToItemsJson" text,
    "AppliedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PromotionUsages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PromotionUsages_Promotions_PromotionId" FOREIGN KEY ("PromotionId")
        REFERENCES "Promotions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionUsages_Orders_OrderId" FOREIGN KEY ("OrderId")
        REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId")
        REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL
);

-- Indexes for PromotionUsages
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PromotionUsages_TenantId_PromotionId_OrderId"
ON "PromotionUsages" ("TenantId", "PromotionId", "OrderId")
WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_PromotionUsages_TenantId_PromotionId"
ON "PromotionUsages" ("TenantId", "PromotionId");

-- ============================================================================
-- 3. ADD PROMOTION FIELDS TO ORDERS TABLE
-- ============================================================================
-- These fields track which promotion (offer) was applied to an order

ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "AppliedPromotionId" uuid;

ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "AppliedPromotionType" character varying(20);

ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "AppliedPromotionName" character varying(200);

ALTER TABLE "Orders" 
ADD COLUMN IF NOT EXISTS "AppliedPromotionDescription" character varying(500);

-- Add foreign key for AppliedPromotionId
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_Orders_Promotions_AppliedPromotionId'
    ) THEN
        ALTER TABLE "Orders"
        ADD CONSTRAINT "FK_Orders_Promotions_AppliedPromotionId" 
        FOREIGN KEY ("AppliedPromotionId")
        REFERENCES "Promotions" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- Index for promotion lookups on orders
CREATE INDEX IF NOT EXISTS "IX_Orders_AppliedPromotionId"
ON "Orders" ("AppliedPromotionId");

-- ============================================================================
-- 4. VERIFICATION QUERIES (run these to verify the migration)
-- ============================================================================
-- SELECT COUNT(*) as promotions_table FROM "Promotions";
-- SELECT COUNT(*) as promotion_usages_table FROM "PromotionUsages";
-- SELECT column_name FROM information_schema.columns WHERE table_name = 'Orders' AND column_name LIKE 'AppliedPromotion%';

-- ============================================================================
-- MIGRATION COMPLETE
-- ============================================================================
-- Next steps:
-- 1. Run this SQL in PgAdmin4
-- 2. Verify tables created with the verification queries above
-- 3. Restart the API to pick up the new schema
-- ============================================================================
