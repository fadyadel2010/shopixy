-- SQL Update Script for PointOfSale.API StoreConfigs Table
-- Phase 7: Commerce AI Experience Config
-- Run this script in PgAdmin4 or psql to update the schema

-- Add new columns for Commerce AI configuration
ALTER TABLE "StoreConfigs"
ADD COLUMN IF NOT EXISTS "CommerceAiWidgetStyle" text NOT NULL DEFAULT 'minimal',
ADD COLUMN IF NOT EXISTS "CommerceAiWidgetPosition" text NOT NULL DEFAULT 'bottom-right',
ADD COLUMN IF NOT EXISTS "CommerceAiAutoGreetingEnabled" boolean NOT NULL DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS "CommerceAiAutoGreetingMessage" text NOT NULL DEFAULT '',
ADD COLUMN IF NOT EXISTS "CommerceAiPersonality" text NOT NULL DEFAULT 'friendly';

-- Verify the update
SELECT "Id", "StoreName", "CommerceAiWidgetStyle", "CommerceAiWidgetPosition", "CommerceAiPersonality" FROM "StoreConfigs" LIMIT 5;
