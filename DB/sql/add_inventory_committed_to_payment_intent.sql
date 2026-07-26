-- Migration: Add InventoryCommitted flag to PaymentIntent
-- Purpose: Track whether inventory has been committed for webhook retry recovery
-- Date: 2026-04-17

-- Add the column with default false
ALTER TABLE "PaymentIntents"
ADD COLUMN "InventoryCommitted" boolean NOT NULL DEFAULT false;

-- Backfill: Mark all existing paid intents as having inventory committed
-- (Assume historical payments already have inventory committed)
UPDATE "PaymentIntents"
SET "InventoryCommitted" = true
WHERE "Status" = 2; -- PaymentIntentStatus.Paid = 2

-- Add index for the retry query pattern (Status = Paid AND InventoryCommitted = false)
CREATE INDEX "IX_PaymentIntents_Status_InventoryCommitted" 
ON "PaymentIntents" ("Status", "InventoryCommitted")
WHERE "Status" = 2 AND "InventoryCommitted" = false;

COMMENT ON COLUMN "PaymentIntents"."InventoryCommitted" IS 
'Whether inventory has been committed for this payment. Used to ensure inventory commitment is recoverable on webhook retry.';
