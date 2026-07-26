-- =====================================================
-- Services Marketplace Phase 3 Hardening - Idempotency
-- =====================================================
-- Purpose: Prevent duplicate trust event processing

-- =====================================================
-- 1. ProcessedTrustEvents
-- =====================================================
CREATE TABLE "ProcessedTrustEvents" (
    "EventId" UUID PRIMARY KEY,
    "EventType" VARCHAR(100) NOT NULL,
    "ProviderProfileId" UUID NOT NULL,
    "ProcessedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "TenantId" UUID NOT NULL,
    
    CONSTRAINT "FK_ProcessedTrustEvents_Tenants" FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_ProcessedTrustEvents_ProviderProfileId" ON "ProcessedTrustEvents"("ProviderProfileId");
CREATE INDEX "IX_ProcessedTrustEvents_ProcessedAt" ON "ProcessedTrustEvents"("ProcessedAt");
CREATE INDEX "IX_ProcessedTrustEvents_TenantId" ON "ProcessedTrustEvents"("TenantId");

-- =====================================================
-- 2. Cleanup Old Events (Optional)
-- =====================================================
-- Function to clean up events older than 30 days
CREATE OR REPLACE FUNCTION cleanup_old_processed_events()
RETURNS void AS $$
BEGIN
    DELETE FROM "ProcessedTrustEvents"
    WHERE "ProcessedAt" < NOW() - INTERVAL '30 days';
END;
$$ LANGUAGE plpgsql;
