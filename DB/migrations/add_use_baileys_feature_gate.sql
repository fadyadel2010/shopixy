-- Migration: Add UseBaileys Feature Gate to Tenants Table
-- Phase 4: Backend Integration - WhatsApp Baileys Migration
-- Date: 2026-02-01
-- 
-- Purpose: Add feature gate for gradual rollout of Baileys transport
-- 
-- Rollout Strategy:
-- 1. Deploy with UseBaileys = false (all tenants use Playwright)
-- 2. Enable for pilot tenants (2-3 tenants)
-- 3. Gradual rollout: 10% -> 25% -> 50% -> 100%
-- 4. Monitor metrics at each stage
-- 5. Instant rollback by setting UseBaileys = false

-- Add UseBaileys column to Tenants table
ALTER TABLE "Tenants"
ADD COLUMN "UseBaileys" BOOLEAN NOT NULL DEFAULT FALSE;

-- Add index for efficient feature gate queries
CREATE INDEX "IX_Tenants_UseBaileys" ON "Tenants" ("UseBaileys");

-- Add comment for documentation
COMMENT ON COLUMN "Tenants"."UseBaileys" IS 'Feature gate for WhatsApp Baileys migration. When true, uses Baileys transport (20-40 MB RAM). When false, uses Playwright transport (300-600 MB RAM). Default: false for backward compatibility.';

-- Verify migration
SELECT 
    COUNT(*) as total_tenants,
    SUM(CASE WHEN "UseBaileys" = true THEN 1 ELSE 0 END) as baileys_enabled,
    SUM(CASE WHEN "UseBaileys" = false THEN 1 ELSE 0 END) as playwright_enabled
FROM "Tenants";
