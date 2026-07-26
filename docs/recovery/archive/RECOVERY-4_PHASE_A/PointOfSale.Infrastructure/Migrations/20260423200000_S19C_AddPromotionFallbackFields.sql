-- ═══════════════════════════════════════════════════════════════════════════
-- S19C: Add Promotion Fallback Tracking Fields
-- ═══════════════════════════════════════════════════════════════════════════
-- CONTEXT:
-- When promotion calculation fails during checkout, the system falls back to
-- zero discounts and shows a warning banner. However, on duplicate order recovery
-- (idempotent checkout), the warning was being dropped because the promotion
-- fallback state wasn't persisted.
--
-- SOLUTION:
-- Add PromotionCalculationFailed and PromotionWarningMessage to Orders table
-- to preserve the fallback state for consistent duplicate order responses.
-- ═══════════════════════════════════════════════════════════════════════════

-- Add promotion fallback tracking columns
ALTER TABLE "Orders"
ADD COLUMN "PromotionCalculationFailed" boolean NOT NULL DEFAULT false;

ALTER TABLE "Orders"
ADD COLUMN "PromotionWarningMessage" text NULL;

-- Historical orders default to false (no fallback)
-- New orders will explicitly set this based on checkout context
