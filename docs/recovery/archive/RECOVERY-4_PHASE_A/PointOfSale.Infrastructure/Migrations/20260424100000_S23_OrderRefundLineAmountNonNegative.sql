-- =====================================================================================
-- Migration: S23_OrderRefundLineAmountNonNegative
-- Description: Add check constraint to enforce non-negative Amount in OrderRefundLines
-- Author: System
-- Date: 2026-04-24
-- =====================================================================================

-- SAFETY: This migration adds a check constraint to prevent negative refund amounts.
-- Existing data should already be non-negative, but this enforces it at the DB level.

BEGIN;

-- Add check constraint to OrderRefundLines.Amount
ALTER TABLE "OrderRefundLines"
    ADD CONSTRAINT "CK_OrderRefundLines_Amount_NonNegative" 
    CHECK ("Amount" >= 0);

-- Verify constraint was created
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'CK_OrderRefundLines_Amount_NonNegative'
    ) THEN
        RAISE EXCEPTION 'Check constraint CK_OrderRefundLines_Amount_NonNegative was not created';
    END IF;
END $$;

COMMIT;

-- Rollback script (for reference):
-- ALTER TABLE "OrderRefundLines" DROP CONSTRAINT "CK_OrderRefundLines_Amount_NonNegative";
