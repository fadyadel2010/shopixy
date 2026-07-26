-- =============================================================================
-- CARRIER-AGNOSTIC SHIPPING MODULE
-- Database Migration Script
-- 
-- Run this script to add the new tables and columns for the shipping module.
-- =============================================================================

-- =========================================================================
-- PART 1: CarrierConfigurations Table
-- Per-tenant configuration for shipping carriers
-- =========================================================================

CREATE TABLE IF NOT EXISTS "CarrierConfigurations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CarrierCode" varchar(50) NOT NULL,
    "DisplayName" varchar(100) NOT NULL,
    "IsEnabled" boolean NOT NULL DEFAULT false,
    "IsDefault" boolean NOT NULL DEFAULT false,
    "ApiKey" varchar(500),
    "ApiSecret" varchar(500),
    "AccountId" varchar(100),
    "ApiUrl" varchar(500),
    "UseSandbox" boolean NOT NULL DEFAULT false,
    "SupportsCod" boolean NOT NULL DEFAULT false,
    "SupportsTracking" boolean NOT NULL DEFAULT true,
    "SupportsWebhooks" boolean NOT NULL DEFAULT false,
    "SupportsLabels" boolean NOT NULL DEFAULT false,
    "SupportsQuotes" boolean NOT NULL DEFAULT true,
    "SupportsCancellation" boolean NOT NULL DEFAULT true,
    "Configuration" text,
    "WebhookUrl" varchar(500),
    "WebhookSecret" varchar(256),
    "PickupAddress" varchar(500),
    "PickupCity" varchar(100),
    "PickupCountry" varchar(10),
    "PickupPhone" varchar(50),
    "PickupContactName" varchar(100),
    "AvailableServiceLevels" text,
    "DefaultServiceLevel" varchar(50) NOT NULL DEFAULT 'standard',
    "UseCarrierRates" boolean NOT NULL DEFAULT true,
    "FixedShippingCost" numeric(18,2),
    "MarkupPercentage" numeric(5,2) NOT NULL DEFAULT 0,
    "FreeShippingThreshold" numeric(18,2),
    "SortOrder" integer NOT NULL DEFAULT 0,
    "Notes" varchar(1000),
    "LastValidatedAtUtc" timestamp with time zone,
    "LastValidationSucceeded" boolean,
    "LastValidationError" varchar(1000),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CarrierConfigurations" PRIMARY KEY ("Id")
);

-- Unique index: one configuration per carrier per tenant
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CarrierConfigurations_TenantId_CarrierCode" 
ON "CarrierConfigurations" ("TenantId", "CarrierCode") 
WHERE "IsDeleted" = false;

-- Index for enabled carriers query
CREATE INDEX IF NOT EXISTS "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder" 
ON "CarrierConfigurations" ("TenantId", "IsEnabled", "SortOrder");

-- Index for default carrier lookup
CREATE INDEX IF NOT EXISTS "IX_CarrierConfigurations_TenantId_IsDefault" 
ON "CarrierConfigurations" ("TenantId", "IsDefault") 
WHERE "IsDefault" = true;

-- =========================================================================
-- PART 2: Shipments Table Modifications
-- Add carrier-agnostic columns to existing Shipments table
-- =========================================================================

-- Add new carrier-agnostic columns
DO $$
BEGIN
    -- CarrierCode (required for carrier-agnostic shipments)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CarrierCode') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CarrierCode" varchar(50) NOT NULL DEFAULT 'manual';
    END IF;
    
    -- ServiceLevel
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'ServiceLevel') THEN
        ALTER TABLE "Shipments" ADD COLUMN "ServiceLevel" varchar(50) DEFAULT 'standard';
    END IF;
    
    -- COD fields
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'IsCashOnDelivery') THEN
        ALTER TABLE "Shipments" ADD COLUMN "IsCashOnDelivery" boolean NOT NULL DEFAULT false;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CodAmount') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CodAmount" numeric(18,2);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CodCollected') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CodCollected" boolean NOT NULL DEFAULT false;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CodCollectedAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CodCollectedAtUtc" timestamp with time zone;
    END IF;
    
    -- AWB Number
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'AwbNumber') THEN
        ALTER TABLE "Shipments" ADD COLUMN "AwbNumber" varchar(100);
    END IF;
    
    -- Recipient email
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'RecipientEmail') THEN
        ALTER TABLE "Shipments" ADD COLUMN "RecipientEmail" varchar(255);
    END IF;
    
    -- Address line 2
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'ShippingAddressLine2') THEN
        ALTER TABLE "Shipments" ADD COLUMN "ShippingAddressLine2" varchar(500);
    END IF;
    
    -- Coordinates
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'Latitude') THEN
        ALTER TABLE "Shipments" ADD COLUMN "Latitude" numeric(9,6);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'Longitude') THEN
        ALTER TABLE "Shipments" ADD COLUMN "Longitude" numeric(9,6);
    END IF;
    
    -- Package details
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'PackageCount') THEN
        ALTER TABLE "Shipments" ADD COLUMN "PackageCount" integer NOT NULL DEFAULT 1;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'PackageDescription') THEN
        ALTER TABLE "Shipments" ADD COLUMN "PackageDescription" varchar(500);
    END IF;
    
    -- Additional timestamps
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'ReadyForPickupAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "ReadyForPickupAtUtc" timestamp with time zone;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'PickedUpAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "PickedUpAtUtc" timestamp with time zone;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'ReturnedAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "ReturnedAtUtc" timestamp with time zone;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'FailedAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "FailedAtUtc" timestamp with time zone;
    END IF;
    
    -- Carrier metadata
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CarrierMetadata') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CarrierMetadata" text;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CarrierCreateResponse') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CarrierCreateResponse" text;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'CarrierTrackingResponse') THEN
        ALTER TABLE "Shipments" ADD COLUMN "CarrierTrackingResponse" text;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'LastCarrierSyncAtUtc') THEN
        ALTER TABLE "Shipments" ADD COLUMN "LastCarrierSyncAtUtc" timestamp with time zone;
    END IF;
    
    -- Status & Notes
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'FailureReason') THEN
        ALTER TABLE "Shipments" ADD COLUMN "FailureReason" varchar(500);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'ReturnReason') THEN
        ALTER TABLE "Shipments" ADD COLUMN "ReturnReason" varchar(500);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'DeliveryAttempts') THEN
        ALTER TABLE "Shipments" ADD COLUMN "DeliveryAttempts" integer NOT NULL DEFAULT 0;
    END IF;
    
    -- Label & Documents
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'LabelUrl') THEN
        ALTER TABLE "Shipments" ADD COLUMN "LabelUrl" varchar(1000);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'LabelData') THEN
        ALTER TABLE "Shipments" ADD COLUMN "LabelData" text;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'Shipments' AND column_name = 'LabelFormat') THEN
        ALTER TABLE "Shipments" ADD COLUMN "LabelFormat" varchar(20);
    END IF;

END $$;

-- =========================================================================
-- PART 3: Shipments Indexes
-- Add indexes for carrier-agnostic queries
-- =========================================================================

-- Index for tracking number lookup
CREATE INDEX IF NOT EXISTS "IX_Shipments_CarrierCode_TrackingNumber" 
ON "Shipments" ("CarrierCode", "TrackingNumber");

-- Index for status-based queries (tracking sync job)
CREATE INDEX IF NOT EXISTS "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc" 
ON "Shipments" ("TenantId", "Status", "LastCarrierSyncAtUtc");

-- Index for carrier-specific queries
CREATE INDEX IF NOT EXISTS "IX_Shipments_TenantId_CarrierCode_Status" 
ON "Shipments" ("TenantId", "CarrierCode", "Status");

-- =========================================================================
-- PART 4: Make ShippingMethodId nullable
-- Carrier-managed shipments may not have a POS shipping method
-- =========================================================================

DO $$
BEGIN
    -- Check if column is currently NOT NULL
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Shipments' 
        AND column_name = 'ShippingMethodId' 
        AND is_nullable = 'NO'
    ) THEN
        ALTER TABLE "Shipments" ALTER COLUMN "ShippingMethodId" DROP NOT NULL;
    END IF;
END $$;

-- =========================================================================
-- VERIFICATION
-- =========================================================================

-- Verify CarrierConfigurations table
SELECT 'CarrierConfigurations table exists: ' || EXISTS(
    SELECT 1 FROM information_schema.tables 
    WHERE table_name = 'CarrierConfigurations'
);

-- Count columns in Shipments table
SELECT 'Shipments column count: ' || COUNT(*) 
FROM information_schema.columns 
WHERE table_name = 'Shipments';

-- List new columns
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'Shipments' 
AND column_name IN ('CarrierCode', 'ServiceLevel', 'IsCashOnDelivery', 'CodAmount', 'CarrierMetadata', 'LastCarrierSyncAtUtc')
ORDER BY column_name;

DO $$ BEGIN RAISE NOTICE 'Migration completed successfully!'; END $$;
