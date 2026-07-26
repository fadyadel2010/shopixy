-- ═══════════════════════════════════════════════════════════════════
-- AF-13B: Affiliate Guest Checkout Migration
-- 
-- Adds IsGuestCheckout column to Orders table.
-- Safe to apply multiple times (idempotent).
-- Historical orders default to false.
-- ═══════════════════════════════════════════════════════════════════

-- 1. Add IsGuestCheckout column (boolean, default false)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'IsGuestCheckout'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "IsGuestCheckout" boolean NOT NULL DEFAULT false;
        RAISE NOTICE 'AF-13B: Added IsGuestCheckout column to Orders table';
    ELSE
        RAISE NOTICE 'AF-13B: IsGuestCheckout column already exists — skipping';
    END IF;
END $$;

-- 2. Backfill: Any existing order with no CustomerId AND no OnlineCustomerId is a guest order
UPDATE "Orders"
SET "IsGuestCheckout" = true
WHERE "CustomerId" IS NULL 
  AND "OnlineCustomerId" IS NULL 
  AND "IsGuestCheckout" = false
  AND "GuestPhone" IS NOT NULL;

-- 3. Index for admin filtering (guest orders)
CREATE INDEX IF NOT EXISTS "IX_Orders_IsGuestCheckout"
ON "Orders" ("IsGuestCheckout")
WHERE "IsGuestCheckout" = true;

-- Done
SELECT 'AF-13B migration complete' AS status;
