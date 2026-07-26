-- =============================================================================
-- Smart Abandoned Cart Recovery Engine - WhatsApp Rate Limits Table
-- Run this in PgAdmin4
-- =============================================================================

-- WhatsAppRateLimits table: Tracks message rate limits and opt-out status
CREATE TABLE IF NOT EXISTS "WhatsAppRateLimits" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PhoneNumber" character varying(20) NOT NULL,
    "MessageCount" integer NOT NULL DEFAULT 0,
    "WindowStartUtc" timestamp with time zone NOT NULL,
    "LastMessageAtUtc" timestamp with time zone NOT NULL,
    "OptedOut" boolean NOT NULL DEFAULT FALSE,
    "OptedOutAtUtc" timestamp with time zone NULL,
    CONSTRAINT "PK_WhatsAppRateLimits" PRIMARY KEY ("Id")
);

-- Unique constraint for atomic upsert operations (TenantId + PhoneNumber)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WhatsAppRateLimits_TenantId_PhoneNumber" 
ON "WhatsAppRateLimits" ("TenantId", "PhoneNumber");

-- Index for tenant-level queries
CREATE INDEX IF NOT EXISTS "IX_WhatsAppRateLimits_TenantId" 
ON "WhatsAppRateLimits" ("TenantId");

-- Verify the table was created
SELECT table_name FROM information_schema.tables 
WHERE table_name = 'WhatsAppRateLimits';
