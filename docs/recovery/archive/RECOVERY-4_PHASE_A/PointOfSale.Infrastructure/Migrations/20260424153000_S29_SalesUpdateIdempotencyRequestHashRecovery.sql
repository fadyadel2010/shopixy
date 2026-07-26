-- ============================================================================
-- Migration: S29_SalesUpdateIdempotencyRequestHashRecovery
-- Description: Add lookup index for POS sale update idempotency recovery after
--              browser reloads or lost payment attempt identifiers.
-- Date: 2026-04-24
-- ============================================================================

-- IMPORTANT: This migration uses CREATE INDEX CONCURRENTLY to avoid blocking writes.
--
-- DEPLOYMENT REQUIREMENTS:
-- 1. This statement MUST be executed OUTSIDE of a transaction block
-- 2. If your migration runner wraps migrations in transactions, configure this
--    migration to run without a transaction wrapper or use regular CREATE INDEX
--    during a maintenance window
-- 3. If the command fails partway through, it may leave an INVALID index that
--    should be dropped before retrying:
--    DROP INDEX CONCURRENTLY IF EXISTS "IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted";

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted"
    ON "SalesRequestIdempotency" ("TenantId", "Operation", "RequestHash", "IsDeleted");
