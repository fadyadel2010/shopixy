-- ============================================================================
-- Migration: AddShipmentOutForDeliveryTimestamp
-- Date: 2026-01-22
-- Description: Adds OutForDeliveryAtUtc timestamp column to Shipments table
--              for consistent status timestamp tracking
-- ============================================================================

-- Add OutForDeliveryAtUtc column to Shipments table
ALTER TABLE "Shipments"
ADD COLUMN IF NOT EXISTS "OutForDeliveryAtUtc" timestamp with time zone NULL;

-- Add comment for documentation
COMMENT ON COLUMN "Shipments"."OutForDeliveryAtUtc" IS 'Timestamp when shipment status changed to OutForDelivery';

-- ============================================================================
-- Verification query (optional - run to confirm)
-- ============================================================================
-- SELECT column_name, data_type, is_nullable 
-- FROM information_schema.columns 
-- WHERE table_name = 'Shipments' AND column_name = 'OutForDeliveryAtUtc';
