-- ═══════════════════════════════════════════════════════════════════════════
-- FULFILLMENT OWNERSHIP ARCHITECTURE - POSTGRESQL MIGRATION
-- ═══════════════════════════════════════════════════════════════════════════
-- 
-- MISSION: Make fulfillment ownership immutable and impossible to bypass.
-- 
-- INVARIANTS ENFORCED:
-- 1. FulfillmentMode is snapshotted at Order creation (NEVER inferred later)
-- 2. FulfillmentMode is IMMUTABLE (cannot be updated after insert)
-- 3. Manual orders → FulfillmentCarrierCode MUST be NULL
-- 4. Carrier orders → FulfillmentCarrierCode MUST NOT be NULL
-- 5. UI/API must rely ONLY on snapshot fields (never ShippingOption)
-- 
-- ARCHITECTURAL GUARANTEE:
-- After this migration, it becomes IMPOSSIBLE to:
-- - Change FulfillmentMode after order creation
-- - Have carrier orders without carrier code
-- - Have manual orders with carrier code
-- - Bypass ownership checks via direct SQL
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN TRANSACTION;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 1: Add FulfillmentMode column (NOT NULL with default) - IDEMPOTENT
-- ═══════════════════════════════════════════════════════════════════════════
-- FulfillmentMode values:
-- 0 = Manual (admin-controlled fulfillment)
-- 1 = Carrier (carrier webhook-controlled fulfillment)

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' 
        AND column_name = 'FulfillmentMode'
    ) THEN
        ALTER TABLE "Orders"
        ADD COLUMN "FulfillmentMode" INT NOT NULL DEFAULT 0;
        
        COMMENT ON COLUMN "Orders"."FulfillmentMode" IS 
        'Fulfillment ownership mode: 0=Manual (admin), 1=Carrier (webhook). IMMUTABLE after creation.';
        
        RAISE NOTICE 'Column FulfillmentMode added successfully';
    ELSE
        RAISE NOTICE 'Column FulfillmentMode already exists, skipping';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 2: Add FulfillmentCarrierCode column (nullable) - IDEMPOTENT
-- ═══════════════════════════════════════════════════════════════════════════
-- Stores the carrier code (e.g., "bosta", "aramex") for carrier-managed orders.
-- NULL for manual fulfillment orders.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' 
        AND column_name = 'FulfillmentCarrierCode'
    ) THEN
        ALTER TABLE "Orders"
        ADD COLUMN "FulfillmentCarrierCode" VARCHAR(50) NULL;
        
        COMMENT ON COLUMN "Orders"."FulfillmentCarrierCode" IS 
        'Carrier code for carrier-managed orders (e.g., "bosta", "aramex"). NULL for manual orders.';
        
        RAISE NOTICE 'Column FulfillmentCarrierCode added successfully';
    ELSE
        RAISE NOTICE 'Column FulfillmentCarrierCode already exists, skipping';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 3: Backfill existing orders based on ShippingMethod.CarrierCode
-- ═══════════════════════════════════════════════════════════════════════════
-- LOGIC:
-- - If ShippingMethod has CarrierCode → FulfillmentMode = 1 (Carrier)
-- - Otherwise → FulfillmentMode = 0 (Manual)

UPDATE "Orders" o
SET 
    "FulfillmentMode" = CASE 
        WHEN sm."CarrierCode" IS NOT NULL AND sm."CarrierCode" != '' AND sm."CarrierCode" != 'manual' 
        THEN 1  -- Carrier
        ELSE 0  -- Manual
    END,
    "FulfillmentCarrierCode" = CASE 
        WHEN sm."CarrierCode" IS NOT NULL AND sm."CarrierCode" != '' AND sm."CarrierCode" != 'manual' 
        THEN sm."CarrierCode"
        ELSE NULL
    END
FROM "ShippingMethods" sm
WHERE o."ShippingMethodId" = sm."Id";

-- Handle orders without ShippingMethod (edge case: manual/guest orders)
UPDATE "Orders"
SET 
    "FulfillmentMode" = 0,  -- Manual
    "FulfillmentCarrierCode" = NULL
WHERE "ShippingMethodId" IS NULL;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 4: Add CHECK constraint for ownership invariant - IDEMPOTENT
-- ═══════════════════════════════════════════════════════════════════════════
-- ENFORCES:
-- - Manual orders (FulfillmentMode = 0) → FulfillmentCarrierCode MUST be NULL
-- - Carrier orders (FulfillmentMode = 1) → FulfillmentCarrierCode MUST NOT be NULL

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'CK_Orders_FulfillmentOwnership'
    ) THEN
        ALTER TABLE "Orders"
        ADD CONSTRAINT "CK_Orders_FulfillmentOwnership" CHECK (
            (
                "FulfillmentMode" = 0 AND "FulfillmentCarrierCode" IS NULL
            ) OR (
                "FulfillmentMode" = 1 AND "FulfillmentCarrierCode" IS NOT NULL
            )
        );
        
        COMMENT ON CONSTRAINT "CK_Orders_FulfillmentOwnership" ON "Orders" IS
        'Enforces fulfillment ownership invariant: Manual orders have no carrier code, Carrier orders must have carrier code.';
        
        RAISE NOTICE 'CHECK constraint CK_Orders_FulfillmentOwnership added successfully';
    ELSE
        RAISE NOTICE 'CHECK constraint CK_Orders_FulfillmentOwnership already exists, skipping';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 5: Create trigger to prevent FulfillmentMode updates - IDEMPOTENT
-- ═══════════════════════════════════════════════════════════════════════════
-- IMMUTABILITY ENFORCEMENT:
-- FulfillmentMode can ONLY be set during INSERT.
-- Any UPDATE attempt is REJECTED.

CREATE OR REPLACE FUNCTION prevent_fulfillment_mode_update()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD."FulfillmentMode" IS DISTINCT FROM NEW."FulfillmentMode" THEN
        RAISE EXCEPTION 'FulfillmentMode is immutable and cannot be changed after order creation. OrderId: %, Old: %, New: %',
            OLD."Id", OLD."FulfillmentMode", NEW."FulfillmentMode"
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    
    IF OLD."FulfillmentCarrierCode" IS DISTINCT FROM NEW."FulfillmentCarrierCode" THEN
        RAISE EXCEPTION 'FulfillmentCarrierCode is immutable and cannot be changed after order creation. OrderId: %, Old: %, New: %',
            OLD."Id", OLD."FulfillmentCarrierCode", NEW."FulfillmentCarrierCode"
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger 
        WHERE tgname = 'trg_prevent_fulfillment_mode_update'
    ) THEN
        CREATE TRIGGER trg_prevent_fulfillment_mode_update
        BEFORE UPDATE ON "Orders"
        FOR EACH ROW
        EXECUTE FUNCTION prevent_fulfillment_mode_update();
        
        COMMENT ON TRIGGER trg_prevent_fulfillment_mode_update ON "Orders" IS
        'Prevents any updates to FulfillmentMode or FulfillmentCarrierCode after order creation.';
        
        RAISE NOTICE 'Trigger trg_prevent_fulfillment_mode_update created successfully';
    ELSE
        RAISE NOTICE 'Trigger trg_prevent_fulfillment_mode_update already exists, skipping';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 6: Create index for fulfillment mode queries - IDEMPOTENT
-- ═══════════════════════════════════════════════════════════════════════════

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes 
        WHERE indexname = 'IX_Orders_TenantId_FulfillmentMode'
    ) THEN
        CREATE INDEX "IX_Orders_TenantId_FulfillmentMode" 
        ON "Orders" ("TenantId", "FulfillmentMode");
        
        COMMENT ON INDEX "IX_Orders_TenantId_FulfillmentMode" IS
        'Optimizes queries filtering orders by fulfillment mode (manual vs carrier).';
        
        RAISE NOTICE 'Index IX_Orders_TenantId_FulfillmentMode created successfully';
    ELSE
        RAISE NOTICE 'Index IX_Orders_TenantId_FulfillmentMode already exists, skipping';
    END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════════════

-- Count orders by fulfillment mode
SELECT 
    "TenantId",
    "FulfillmentMode",
    COUNT(*) as "OrderCount"
FROM "Orders"
GROUP BY "TenantId", "FulfillmentMode"
ORDER BY "TenantId", "FulfillmentMode";

-- Verify constraint enforcement (should return 0 rows)
SELECT 
    "Id",
    "OrderNumber",
    "FulfillmentMode",
    "FulfillmentCarrierCode"
FROM "Orders"
WHERE 
    ("FulfillmentMode" = 0 AND "FulfillmentCarrierCode" IS NOT NULL) OR
    ("FulfillmentMode" = 1 AND "FulfillmentCarrierCode" IS NULL);

COMMIT;

-- ═══════════════════════════════════════════════════════════════════════════
-- ROLLBACK SCRIPT (if needed)
-- ═══════════════════════════════════════════════════════════════════════════
/*
BEGIN TRANSACTION;

DROP TRIGGER IF EXISTS trg_prevent_fulfillment_mode_update ON "Orders";
DROP FUNCTION IF EXISTS prevent_fulfillment_mode_update();
DROP INDEX IF EXISTS "IX_Orders_TenantId_FulfillmentMode";
ALTER TABLE "Orders" DROP CONSTRAINT IF EXISTS "CK_Orders_FulfillmentOwnership";
ALTER TABLE "Orders" DROP COLUMN IF EXISTS "FulfillmentCarrierCode";
ALTER TABLE "Orders" DROP COLUMN IF EXISTS "FulfillmentMode";

COMMIT;
*/
