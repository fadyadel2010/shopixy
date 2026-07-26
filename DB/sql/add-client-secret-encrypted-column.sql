-- Migration: Add ClientSecretEncrypted column to PaymentIntents table
-- Purpose: Store payment provider client secrets (e.g., Stripe client_secret) securely
-- Security: This field will be encrypted at the application layer using ASP.NET Core Data Protection API
-- Date: 2026-04-22

-- Add the new column (idempotent - safe to re-run)
ALTER TABLE "PaymentIntents"
ADD COLUMN IF NOT EXISTS "ClientSecretEncrypted" text NULL;

-- Add comment for documentation
COMMENT ON COLUMN "PaymentIntents"."ClientSecretEncrypted" IS 'Encrypted client secret for payment providers (e.g., Stripe client_secret). Encrypted using ASP.NET Core Data Protection API.';

-- Note: RedirectUrl should no longer be used for storing sensitive credentials
COMMENT ON COLUMN "PaymentIntents"."RedirectUrl" IS 'Redirect URL for hosted payment pages (e.g., Paymob iframe URL). Should NOT contain sensitive credentials.';
