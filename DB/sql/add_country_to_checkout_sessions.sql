-- Migration: Add Country column to CheckoutSessions table
-- Phase: Abandoned Checkout Recovery - Country Support
-- Date: 2026-04-19
-- Purpose: Persist country field for shipping/tax context in recovered checkout sessions

-- IMPORTANT: This migration has two parts:
-- Part 1: Can run in a transaction (column addition)
-- Part 2: Must run OUTSIDE a transaction (concurrent index creation)

-- ============================================================================
-- PART 1: Add Column (can run in transaction)
-- ============================================================================

-- Add Country column (nullable, as it's optional and existing records won't have it)
ALTER TABLE "CheckoutSessions"
ADD COLUMN IF NOT EXISTS "Country" TEXT NULL;

-- Add comment for documentation
COMMENT ON COLUMN "CheckoutSessions"."Country" IS 'Country from shipping address (ISO 3166-1 alpha-2 code or full name). Used for shipping/tax calculations and recovery.';

-- ============================================================================
-- PART 2: Create Index Concurrently (MUST run OUTSIDE transaction)
-- ============================================================================
-- 
-- The index creation has been moved to a separate migration file:
-- add_country_to_checkout_sessions_index.sql
--
-- That file contains:
-- CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_CheckoutSessions_Country" 
-- ON "CheckoutSessions" ("Country") 
-- WHERE "Country" IS NOT NULL;
--
-- Why separate file?
-- - PostgreSQL does not allow CREATE INDEX CONCURRENTLY inside transaction blocks
-- - Separate file can be executed outside transaction by migration tools
-- - Avoids blocking writes to CheckoutSessions table during index creation
--
-- Execution order:
-- 1. Run this file first (adds column)
-- 2. Run add_country_to_checkout_sessions_index.sql (creates index)
--
-- For development/testing with small datasets, you can manually run:
-- CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_Country" 
-- ON "CheckoutSessions" ("Country") 
-- WHERE "Country" IS NOT NULL;
-- ============================================================================
