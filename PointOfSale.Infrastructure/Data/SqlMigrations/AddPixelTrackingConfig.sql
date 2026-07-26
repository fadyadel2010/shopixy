-- Migration: AddPixelTrackingConfig
-- Description: Adds PixelTrackingConfigs table for handling pixel conversion value overrides.

CREATE TABLE "PixelTrackingConfigs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "Strategy" integer NOT NULL,
    "Value" numeric(18,4) NOT NULL,
    "AppliesToJson" text NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" text,
    CONSTRAINT "PK_PixelTrackingConfigs" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_PixelTrackingConfigs_TenantId" ON "PixelTrackingConfigs" ("TenantId");

COMMENT ON TABLE "PixelTrackingConfigs" IS 'Configuration for Pixel Conversion Value Override (Phase M1).';
COMMENT ON COLUMN "PixelTrackingConfigs"."IsEnabled" IS 'Whether value override is enabled.';
COMMENT ON COLUMN "PixelTrackingConfigs"."Strategy" IS 'Strategy for calculating the override value (1=Percentage, 2=Fixed).';
COMMENT ON COLUMN "PixelTrackingConfigs"."Value" IS 'The value parameter for the strategy.';
COMMENT ON COLUMN "PixelTrackingConfigs"."AppliesToJson" IS 'List of providers this override applies to (JSON).';
