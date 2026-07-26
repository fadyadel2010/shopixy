-- Phase 7.1: Theme & Store Config System
-- Creates the StoreConfigs table for per-tenant store configuration
-- Uses individual columns for owned entities (compatible with EF Core owned entity pattern)

-- Create StoreConfigs table
CREATE TABLE IF NOT EXISTS "StoreConfigs" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "StoreName" varchar(100) NOT NULL,
    "StoreDescription" varchar(500) NULL,
    "ThemeId" varchar(50) NOT NULL DEFAULT 'default',
    
    -- Theme.Colors
    "Theme_Colors_Primary" varchar(20) NOT NULL DEFAULT '#2563eb',
    "Theme_Colors_Secondary" varchar(20) NOT NULL DEFAULT '#0f172a',
    "Theme_Colors_Background" varchar(20) NOT NULL DEFAULT '#ffffff',
    "Theme_Colors_Text" varchar(20) NOT NULL DEFAULT '#020617',
    "Theme_Colors_Muted" varchar(20) NOT NULL DEFAULT '#64748b',
    
    -- Theme.Font
    "Theme_Font_Family" varchar(50) NOT NULL DEFAULT 'Inter',
    "Theme_Font_BaseSize" varchar(20) NOT NULL DEFAULT '16px',
    
    -- Theme.Ui
    "Theme_Ui_BorderRadius" varchar(20) NOT NULL DEFAULT '8px',
    "Theme_Ui_ButtonStyle" varchar(20) NOT NULL DEFAULT 'solid',
    
    -- Theme.Layout
    "Theme_Layout_ProductCardVariant" varchar(30) NOT NULL DEFAULT 'default',
    "Theme_Layout_HeaderVariant" varchar(30) NOT NULL DEFAULT 'standard',
    
    -- Behavior
    "Behavior_EnableGuestCheckout" boolean NOT NULL DEFAULT true,
    "Behavior_ShowOutOfStockProducts" boolean NOT NULL DEFAULT false,
    "Behavior_EnableSearch" boolean NOT NULL DEFAULT true,
    
    -- Audit
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" timestamptz NOT NULL DEFAULT NOW()
);

-- Create unique index on TenantId (one config per tenant)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_StoreConfigs_TenantId" 
    ON "StoreConfigs" ("TenantId");

-- Add foreign key constraint to Tenants table
ALTER TABLE "StoreConfigs" 
    ADD CONSTRAINT "FK_StoreConfigs_Tenants_TenantId" 
    FOREIGN KEY ("TenantId") 
    REFERENCES "Tenants" ("Id") 
    ON DELETE CASCADE;

-- Log migration
DO $$
BEGIN
    RAISE NOTICE 'Phase 7.1: StoreConfigs table created successfully';
END $$;
