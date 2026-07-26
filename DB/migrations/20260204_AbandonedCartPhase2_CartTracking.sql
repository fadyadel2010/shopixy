-- =====================================================================
-- PHASE 2: SMART ABANDONED CART RECOVERY ENGINE - CART TRACKING
-- Migration: Add AbandonedCarts table and indexes
-- Date: 2026-02-04
-- =====================================================================

-- =====================================================================
-- 1. CREATE AbandonedCarts TABLE
-- =====================================================================

CREATE TABLE IF NOT EXISTS "AbandonedCarts" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "CartId" UUID NOT NULL,
    "CustomerId" UUID NULL,
    "PhoneNumber" VARCHAR(20) NOT NULL,
    "Email" VARCHAR(255) NULL,
    "CartSnapshotJson" JSONB NOT NULL DEFAULT '{}'::jsonb,
    "LastActivityAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "AbandonedAtUtc" TIMESTAMP NULL,
    "RecoveryStatus" VARCHAR(20) NOT NULL DEFAULT 'Active',
    "TriggeredAtUtc" TIMESTAMP NULL,
    "RecoveredAtUtc" TIMESTAMP NULL,
    "RecoveredOrderId" UUID NULL,
    "CreatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    
    -- Foreign key to Tenants table
    CONSTRAINT "FK_AbandonedCarts_Tenants_TenantId" 
        FOREIGN KEY ("TenantId") 
        REFERENCES "Tenants"("Id") 
        ON DELETE CASCADE,
    
    -- Foreign key to OnlineCustomers table (nullable)
    CONSTRAINT "FK_AbandonedCarts_OnlineCustomers_CustomerId" 
        FOREIGN KEY ("CustomerId") 
        REFERENCES "OnlineCustomers"("Id") 
        ON DELETE SET NULL,
    
    -- Foreign key to Orders table (nullable)
    CONSTRAINT "FK_AbandonedCarts_Orders_RecoveredOrderId" 
        FOREIGN KEY ("RecoveredOrderId") 
        REFERENCES "Orders"("Id") 
        ON DELETE SET NULL,
    
    -- Unique constraint: one record per cart per tenant
    CONSTRAINT "UQ_AbandonedCarts_TenantId_CartId" 
        UNIQUE ("TenantId", "CartId"),
    
    -- Check constraint: RecoveryStatus enum
    CONSTRAINT "CK_AbandonedCarts_RecoveryStatus" 
        CHECK ("RecoveryStatus" IN ('Active', 'Abandoned', 'Triggered', 'Recovered', 'Expired'))
);

-- =====================================================================
-- 2. CREATE INDEXES
-- =====================================================================

-- Fast tenant lookup
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_TenantId" 
    ON "AbandonedCarts"("TenantId");

-- Fast cart lookup
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_CartId" 
    ON "AbandonedCarts"("CartId");

-- Find carts ready for abandonment detection (used by background job)
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_ReadyForAbandonment" 
    ON "AbandonedCarts"("RecoveryStatus", "LastActivityAtUtc") 
    WHERE "RecoveryStatus" = 'Active';

-- Find triggered carts (for analytics)
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_Triggered" 
    ON "AbandonedCarts"("TenantId", "TriggeredAtUtc") 
    WHERE "RecoveryStatus" = 'Triggered';

-- Find recovered carts (for ROI calculation)
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_Recovered" 
    ON "AbandonedCarts"("TenantId", "RecoveredAtUtc") 
    WHERE "RecoveryStatus" = 'Recovered';

-- Customer lookup
CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_CustomerId" 
    ON "AbandonedCarts"("CustomerId") 
    WHERE "CustomerId" IS NOT NULL;

-- =====================================================================
-- 3. ADD COMMENTS
-- =====================================================================

COMMENT ON TABLE "AbandonedCarts" IS 
    'Tracks cart lifecycle from active → abandoned → triggered → recovered/expired. Phase: Smart Abandoned Cart Recovery Engine - Phase 2';

COMMENT ON COLUMN "AbandonedCarts"."CartId" IS 
    'Cart identifier from storefront session. Unique per tenant.';

COMMENT ON COLUMN "AbandonedCarts"."PhoneNumber" IS 
    'Customer phone number in E.164 format (required for WhatsApp recovery).';

COMMENT ON COLUMN "AbandonedCarts"."CartSnapshotJson" IS 
    'Snapshot of cart contents at last activity. JSON structure: { items: [...], total: 0, currency: "USD" }';

COMMENT ON COLUMN "AbandonedCarts"."LastActivityAtUtc" IS 
    'Last time customer interacted with cart. Used to calculate abandonment threshold.';

COMMENT ON COLUMN "AbandonedCarts"."AbandonedAtUtc" IS 
    'When cart was marked as abandoned. Null while cart is Active.';

COMMENT ON COLUMN "AbandonedCarts"."RecoveryStatus" IS 
    'Current recovery status: Active, Abandoned, Triggered, Recovered, Expired';

COMMENT ON COLUMN "AbandonedCarts"."TriggeredAtUtc" IS 
    'When recovery workflow was triggered (n8n webhook called). Used for idempotency.';

COMMENT ON COLUMN "AbandonedCarts"."RecoveredAtUtc" IS 
    'When cart was recovered (order completed). Used for success metrics.';

COMMENT ON COLUMN "AbandonedCarts"."RecoveredOrderId" IS 
    'Order ID if cart was recovered. Links abandoned cart to successful conversion.';

-- =====================================================================
-- 4. VERIFICATION QUERIES
-- =====================================================================

-- Verify table creation
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AbandonedCarts') THEN
        RAISE NOTICE '✓ AbandonedCarts table created successfully';
    ELSE
        RAISE EXCEPTION '✗ AbandonedCarts table creation failed';
    END IF;
    
    -- Verify indexes
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AbandonedCarts_ReadyForAbandonment') THEN
        RAISE NOTICE '✓ Abandonment detection index created successfully';
    ELSE
        RAISE EXCEPTION '✗ Abandonment detection index creation failed';
    END IF;
END $$;

-- =====================================================================
-- ROLLBACK SCRIPT (for reference - run manually if needed)
-- =====================================================================

/*
-- Drop indexes
DROP INDEX IF EXISTS "IX_AbandonedCarts_CustomerId";
DROP INDEX IF EXISTS "IX_AbandonedCarts_Recovered";
DROP INDEX IF EXISTS "IX_AbandonedCarts_Triggered";
DROP INDEX IF EXISTS "IX_AbandonedCarts_ReadyForAbandonment";
DROP INDEX IF EXISTS "IX_AbandonedCarts_CartId";
DROP INDEX IF EXISTS "IX_AbandonedCarts_TenantId";

-- Drop table
DROP TABLE IF EXISTS "AbandonedCarts";
*/
