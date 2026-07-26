-- =====================================================================
-- PHASE 1: SMART ABANDONED CART RECOVERY ENGINE - FOUNDATION
-- Migration: Add TenantAutomationSettings table and WhatsApp use case
-- Date: 2026-02-04
-- =====================================================================

-- =====================================================================
-- 1. CREATE TenantAutomationSettings TABLE
-- =====================================================================

CREATE TABLE IF NOT EXISTS "TenantAutomationSettings" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "EnableAbandonedRecovery" BOOLEAN NOT NULL DEFAULT FALSE,
    "FirstMessageDelayMinutes" INTEGER NOT NULL DEFAULT 60,
    "SecondMessageDelayMinutes" INTEGER NOT NULL DEFAULT 1440,
    "WorkingHoursFrom" TIME NOT NULL DEFAULT '09:00:00',
    "WorkingHoursTo" TIME NOT NULL DEFAULT '21:00:00',
    "UpdatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "CreatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    
    -- Foreign key to Tenants table
    CONSTRAINT "FK_TenantAutomationSettings_Tenants_TenantId" 
        FOREIGN KEY ("TenantId") 
        REFERENCES "Tenants"("Id") 
        ON DELETE CASCADE,
    
    -- Unique constraint: one settings record per tenant
    CONSTRAINT "UQ_TenantAutomationSettings_TenantId" 
        UNIQUE ("TenantId")
);

-- Index for fast tenant lookup
CREATE INDEX IF NOT EXISTS "IX_TenantAutomationSettings_TenantId" 
    ON "TenantAutomationSettings"("TenantId");

-- Index for finding enabled tenants (Phase 2: for background jobs)
CREATE INDEX IF NOT EXISTS "IX_TenantAutomationSettings_EnableAbandonedRecovery" 
    ON "TenantAutomationSettings"("EnableAbandonedRecovery") 
    WHERE "EnableAbandonedRecovery" = TRUE;

COMMENT ON TABLE "TenantAutomationSettings" IS 
    'Tenant-level automation configuration for e-commerce marketing features. Phase: Smart Abandoned Cart Recovery Engine - Foundation';

COMMENT ON COLUMN "TenantAutomationSettings"."EnableAbandonedRecovery" IS 
    'Whether abandoned cart recovery automation is enabled. Default FALSE - must be explicitly enabled by admin.';

COMMENT ON COLUMN "TenantAutomationSettings"."FirstMessageDelayMinutes" IS 
    'Delay in minutes before sending first abandoned cart message. Default 60 minutes (1 hour). Range: 0-10080 (7 days).';

COMMENT ON COLUMN "TenantAutomationSettings"."SecondMessageDelayMinutes" IS 
    'Delay in minutes before sending second follow-up message. Default 1440 minutes (24 hours). Range: 0-20160 (14 days). Set to 0 to disable.';

COMMENT ON COLUMN "TenantAutomationSettings"."WorkingHoursFrom" IS 
    'Start of working hours (tenant timezone). Messages only sent during working hours. Default 09:00.';

COMMENT ON COLUMN "TenantAutomationSettings"."WorkingHoursTo" IS 
    'End of working hours (tenant timezone). Messages only sent during working hours. Default 21:00.';

-- =====================================================================
-- 2. ADD UseForAbandonedCartRecovery COLUMN TO whatsapp_accounts
-- =====================================================================

-- Check if column already exists (idempotent migration)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'whatsapp_accounts' 
        AND column_name = 'use_for_abandoned_cart_recovery'
    ) THEN
        ALTER TABLE whatsapp_accounts 
        ADD COLUMN use_for_abandoned_cart_recovery BOOLEAN NOT NULL DEFAULT FALSE;
        
        RAISE NOTICE 'Added use_for_abandoned_cart_recovery column to whatsapp_accounts';
    ELSE
        RAISE NOTICE 'use_for_abandoned_cart_recovery column already exists in whatsapp_accounts';
    END IF;
END $$;

COMMENT ON COLUMN whatsapp_accounts.use_for_abandoned_cart_recovery IS 
    'Use for abandoned cart recovery messages. Phase: Smart Abandoned Cart Recovery Engine';

-- =====================================================================
-- 3. CREATE INDEX FOR ABANDONED CART RECOVERY ACCOUNT LOOKUP
-- =====================================================================

-- Index for fast lookup of active accounts for abandoned cart recovery
CREATE INDEX IF NOT EXISTS ix_whatsapp_accounts_abandoned_cart_recovery 
    ON whatsapp_accounts(tenant_id, use_for_abandoned_cart_recovery, enabled, status) 
    WHERE use_for_abandoned_cart_recovery = TRUE 
    AND enabled = TRUE;

-- =====================================================================
-- 4. VERIFICATION QUERIES
-- =====================================================================

-- Verify table creation
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'TenantAutomationSettings') THEN
        RAISE NOTICE '✓ TenantAutomationSettings table created successfully';
    ELSE
        RAISE EXCEPTION '✗ TenantAutomationSettings table creation failed';
    END IF;
    
    IF EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'whatsapp_accounts' 
        AND column_name = 'use_for_abandoned_cart_recovery'
    ) THEN
        RAISE NOTICE '✓ use_for_abandoned_cart_recovery column added successfully';
    ELSE
        RAISE EXCEPTION '✗ use_for_abandoned_cart_recovery column addition failed';
    END IF;
END $$;

-- =====================================================================
-- ROLLBACK SCRIPT (for reference - run manually if needed)
-- =====================================================================

/*
-- Drop indexes
DROP INDEX IF EXISTS ix_whatsapp_accounts_abandoned_cart_recovery;
DROP INDEX IF EXISTS "IX_TenantAutomationSettings_EnableAbandonedRecovery";
DROP INDEX IF EXISTS "IX_TenantAutomationSettings_TenantId";

-- Drop column
ALTER TABLE whatsapp_accounts DROP COLUMN IF EXISTS use_for_abandoned_cart_recovery;

-- Drop table
DROP TABLE IF EXISTS "TenantAutomationSettings";
*/
