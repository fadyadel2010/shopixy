-- ============================================
-- Migration: Add PhoneVerified fields to OnlineCustomers
-- Phase: WhatsApp OTP Phone Verification
-- Date: 2026-01-29
-- ============================================

-- Add PhoneVerified column (boolean, defaults to false)
ALTER TABLE "OnlineCustomers"
ADD COLUMN IF NOT EXISTS "PhoneVerified" BOOLEAN NOT NULL DEFAULT false;

-- Add PhoneVerifiedAtUtc column (timestamp, nullable)
ALTER TABLE "OnlineCustomers"
ADD COLUMN IF NOT EXISTS "PhoneVerifiedAtUtc" TIMESTAMP WITH TIME ZONE NULL;

-- Add comment for documentation
COMMENT ON COLUMN "OnlineCustomers"."PhoneVerified" IS 'Whether the customer phone has been verified via WhatsApp OTP';
COMMENT ON COLUMN "OnlineCustomers"."PhoneVerifiedAtUtc" IS 'When the phone was verified via WhatsApp OTP';
