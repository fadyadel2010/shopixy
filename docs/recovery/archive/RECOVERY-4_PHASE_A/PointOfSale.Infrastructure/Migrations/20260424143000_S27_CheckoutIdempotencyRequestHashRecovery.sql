-- ============================================================================
-- Migration: S27_CheckoutIdempotencyRequestHashRecovery
-- Description: Add lookup index for pre-cart checkout idempotency replay recovery.
-- Date: 2026-04-24
-- ============================================================================

-- IMPORTANT: This migration uses CREATE INDEX CONCURRENTLY to avoid blocking writes.
-- 
-- DEPLOYMENT REQUIREMENTS:
-- 1. This statement MUST be executed OUTSIDE of a transaction block
-- 2. If your migration runner wraps migrations in transactions, you have two options:
--    a) Configure the runner to execute this migration without a transaction wrapper
--    b) Schedule this migration during a maintenance window and use regular CREATE INDEX
-- 3. CONCURRENTLY builds the index without taking locks that prevent concurrent inserts,
--    updates, or deletes on the table
-- 4. If the command fails partway through, it may leave an INVALID index that should be
--    dropped before retrying: DROP INDEX CONCURRENTLY IF EXISTS "IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted";
--
-- Note: CREATE INDEX CONCURRENTLY cannot run inside a transaction block.
-- If this fails with "CREATE INDEX CONCURRENTLY cannot run inside a transaction block",
-- ensure your migration tool is configured to run this file outside of a transaction.

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted"
    ON "OrderRequestIdempotency" ("TenantId", "RequestHash", "IsDeleted");
