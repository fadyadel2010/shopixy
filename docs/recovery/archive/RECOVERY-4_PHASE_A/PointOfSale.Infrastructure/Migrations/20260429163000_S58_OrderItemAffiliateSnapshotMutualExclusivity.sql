-- Migration: S58_OrderItemAffiliateSnapshotMutualExclusivity
-- Date: 2026-04-29 16:30:00
-- Purpose: Enforce mutually exclusive states for affiliate commission snapshots in OrderItems:
--   State 1: AffiliateCommissionEligible = true WITH all snapshot fields populated and valid
--   State 2: AffiliateCommissionEligible = false WITH all snapshot fields cleared/null
--
-- This prevents contradictory states where items are marked as ineligible but still have
-- snapshot data, which could lead to incorrect commission calculations or data inconsistencies.

-- Drop the existing constraint
ALTER TABLE "OrderItems" DROP CONSTRAINT IF EXISTS "CK_OrderItems_AffiliateCommissionSnapshot_Valid";

-- Recreate with mutually exclusive state enforcement
ALTER TABLE "OrderItems" ADD CONSTRAINT "CK_OrderItems_AffiliateCommissionSnapshot_Valid" CHECK (
    (
        -- State 1: Eligible = true requires all snapshot fields populated and valid
        "AffiliateCommissionEligible" = true AND
        "AffiliateCommissionSnapshotCapturedAtUtc" IS NOT NULL AND
        "AffiliateCommissionPolicyIdSnapshot" IS NOT NULL AND
        "AffiliateCommissionRateSnapshot" > 0 AND
        "AffiliateCommissionRateSnapshot" <= 0.500000 AND
        "AffiliateCommissionBaseAmountSnapshot" > 0 AND
        "AffiliateCommissionAmountSnapshot" > 0 AND
        ("AffiliateMaxCommissionPerItemSnapshot" IS NULL OR "AffiliateMaxCommissionPerItemSnapshot" > 0)
    ) OR (
        -- State 2: Eligible = false requires all snapshot fields cleared/null
        "AffiliateCommissionEligible" = false AND
        "AffiliateCommissionSnapshotCapturedAtUtc" IS NULL AND
        "AffiliateCommissionPolicyIdSnapshot" IS NULL AND
        "AffiliateCommissionRateSnapshot" IS NULL AND
        "AffiliateCommissionBaseAmountSnapshot" IS NULL AND
        "AffiliateCommissionAmountSnapshot" IS NULL AND
        ("AffiliateMaxCommissionPerItemSnapshot" IS NULL OR "AffiliateMaxCommissionPerItemSnapshot" = 0)
    )
);
