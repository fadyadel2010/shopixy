-- Migration: Add ContentHash column to CommerceHubVendorPaymentProofs
-- Purpose: Store SHA-256 hash of proof file content for idempotency validation
-- Date: 2026-04-09

-- Add ContentHash column if it doesn't exist (idempotent)
-- Note: This column may already exist from 20260409190921_AddDashboardPerformanceIndexes
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'CommerceHubVendorPaymentProofs' 
        AND column_name = 'ContentHash'
    ) THEN
        ALTER TABLE "CommerceHubVendorPaymentProofs"
        ADD COLUMN "ContentHash" VARCHAR(64) NULL;
    END IF;
END $$;

-- For existing records with empty or null ContentHash, set a distinguishable legacy sentinel
-- This allows ValidateVendorSubmissionReplay() to skip hash validation for legacy records
-- Format: 'LEGACY_' + 57 zeros to maintain 64-char SHA-256 format (7 + 57 = 64)
UPDATE "CommerceHubVendorPaymentProofs"
SET "ContentHash" = 'LEGACY_' || repeat('0', 57)
WHERE "ContentHash" IS NULL OR trim("ContentHash") = '';

-- Make the column required if it isn't already
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'CommerceHubVendorPaymentProofs' 
        AND column_name = 'ContentHash'
        AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE "CommerceHubVendorPaymentProofs"
        ALTER COLUMN "ContentHash" SET NOT NULL;
    END IF;
END $$;
