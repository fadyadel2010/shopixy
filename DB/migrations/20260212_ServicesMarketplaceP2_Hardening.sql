-- =====================================================
-- Services Marketplace P2 Hardening Migration
-- =====================================================
-- Fixes P2-13 (cleanup scheduling) and P2-16 (composite index)
-- P2-15 (MongoDB TTL) is handled in application code (MongoDbContext)

-- =====================================================
-- P2-16: Composite Index (TenantId, ServiceId) on ServiceRequests
-- =====================================================
-- The hot query path for marketplace queries is:
-- SELECT * FROM ServiceRequests WHERE TenantId = @tid AND ServiceId = @sid
-- Single-column indexes on TenantId and ServiceId are suboptimal for this pattern.
-- A composite index eliminates the need for an index merge.
-- =====================================================
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ServiceRequests_TenantId_ServiceId"
    ON "ServiceRequests"("TenantId", "ServiceId")
    WHERE "IsDeleted" = FALSE;

-- =====================================================
-- P2-13: Schedule ProcessedTrustEvents cleanup via pg_cron
-- =====================================================
-- The cleanup_old_processed_events() function already exists from Phase3_Idempotency.
-- If pg_cron is available, schedule it. Otherwise, use Hangfire (see Program.cs).
-- Uncomment the following if pg_cron extension is installed:
--
-- SELECT cron.schedule('cleanup-processed-trust-events', '0 3 * * *', 
--     $$SELECT cleanup_old_processed_events()$$);
--
-- For Hangfire-based scheduling (recommended), see the
-- ProcessedTrustEventsCleanupJob registered in Program.cs.
