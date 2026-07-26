-- Phase 9 Store Config Schema Update
-- Adds missing columns for storefront builder customization
-- Uses EF Core default naming convention (PascalCase)

-- Add CustomizationJson column (stores page sections, theme overrides)
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'CustomizationJson') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "CustomizationJson" text NOT NULL DEFAULT '{}';
    END IF;
END $$;

-- Add ThemePresetKey column
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'ThemePresetKey') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "ThemePresetKey" VARCHAR(100) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- Add ThemeDraftOverridesJson column
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'ThemeDraftOverridesJson') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "ThemeDraftOverridesJson" text NOT NULL DEFAULT '{}';
    END IF;
END $$;

-- Add ThemeLiveOverridesJson column
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'ThemeLiveOverridesJson') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "ThemeLiveOverridesJson" text NOT NULL DEFAULT '{}';
    END IF;
END $$;

-- Add VersionsJson column for version history
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'VersionsJson') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "VersionsJson" text NOT NULL DEFAULT '[]';
    END IF;
END $$;

-- Add LiveVersion column for current published version number
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'StoreConfigs' AND column_name = 'LiveVersion') THEN
        ALTER TABLE "StoreConfigs" ADD COLUMN "LiveVersion" INTEGER NOT NULL DEFAULT 0;
    END IF;
END $$;

-- Create ThemePresets table if not exists
CREATE TABLE IF NOT EXISTS "ThemePresets" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Key" VARCHAR(100) NOT NULL UNIQUE,
    "Name" VARCHAR(200) NOT NULL,
    "Description" TEXT,
    "IsRtl" BOOLEAN NOT NULL DEFAULT false,
    "BaseThemeJson" TEXT NOT NULL DEFAULT '{}',
    "ThumbnailUrl" VARCHAR(500),
    "PreviewImageUrl" VARCHAR(500),
    "IsActive" BOOLEAN NOT NULL DEFAULT true,
    "CreatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Add PreviewImageUrl column if table already exists but column doesn't
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ThemePresets' AND column_name = 'PreviewImageUrl') THEN
        ALTER TABLE "ThemePresets" ADD COLUMN "PreviewImageUrl" VARCHAR(500);
    END IF;
END $$;

-- Seed default theme preset if doesn't exist
INSERT INTO "ThemePresets" ("Id", "Key", "Name", "Description", "IsRtl", "BaseThemeJson", "IsActive", "CreatedAtUtc")
SELECT '11111111-1111-1111-1111-111111111111'::uuid, 'default', 'Default Modern', 'Standard light theme with blue accent', false,
       '{"colors":{"primary":"#2563eb","secondary":"#0f172a","background":"#ffffff","text":"#020617","muted":"#64748b"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"8px"}}',
       true, '2024-01-01 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM "ThemePresets" WHERE "Key" = 'default');

INSERT INTO "ThemePresets" ("Id", "Key", "Name", "Description", "IsRtl", "BaseThemeJson", "IsActive", "CreatedAtUtc")
SELECT '22222222-2222-2222-2222-222222222222'::uuid, 'dark-minimal', 'Dark Minimal', 'Modern dark theme', false,
       '{"colors":{"primary":"#3b82f6","secondary":"#1e293b","background":"#0f172a","text":"#f8fafc","muted":"#94a3b8"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"12px"}}',
       true, '2024-01-01 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM "ThemePresets" WHERE "Key" = 'dark-minimal');

INSERT INTO "ThemePresets" ("Id", "Key", "Name", "Description", "IsRtl", "BaseThemeJson", "IsActive", "CreatedAtUtc")
SELECT '33333333-3333-3333-3333-333333333333'::uuid, 'modern-light', 'Modern Light', 'Clean modern light theme', false,
       '{"colors":{"primary":"#4F46E5","secondary":"#0f172a","background":"#ffffff","text":"#1F2937","accent":"#10B981"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"8px"}}',
       true, '2024-01-01 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM "ThemePresets" WHERE "Key" = 'modern-light');

-- Verify structure
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'StoreConfigs'
ORDER BY ordinal_position;
