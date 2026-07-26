-- Migration: Add Country column to CheckoutSessions table
-- Date: 2026-04-19
-- Description: Adds Country column to store ISO 3166-1 alpha-2 country codes (e.g., "EG", "US", "SA")

-- Add Country column
ALTER TABLE "CheckoutSessions"
ADD COLUMN "Country" character varying(2) NULL;

-- Optional: Add comment for documentation
COMMENT ON COLUMN "CheckoutSessions"."Country" IS 'ISO 3166-1 alpha-2 country code (e.g., EG, US, SA)';
