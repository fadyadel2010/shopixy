-- Migration: S39_AddPaymentMethodVersion
-- Purpose: Add Version column to TenantPaymentMethods for event ordering
-- Date: 2026-04-26
-- Issue: Events published without version tracking can cause projection staleness

-- Add Version column with default value of 1
ALTER TABLE "TenantPaymentMethods"
ADD COLUMN "Version" INTEGER NOT NULL DEFAULT 1;

-- Create index for efficient version-based queries
CREATE INDEX "IX_TenantPaymentMethods_Version" 
ON "TenantPaymentMethods" ("Version");

COMMENT ON COLUMN "TenantPaymentMethods"."Version" IS 
'Event version counter for projection ordering. Incremented on every mutation that publishes an event.';
