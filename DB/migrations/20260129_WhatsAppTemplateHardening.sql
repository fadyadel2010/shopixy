-- =====================================================================
-- Migration: WhatsApp Template Hardening
-- Date: 2026-01-29
-- Purpose: Add CreatedAtUtc column and unique constraint for templates
-- Phase: WhatsApp Final Hardening
-- =====================================================================

-- Step 1: Add CreatedAtUtc column if it doesn't exist
-- NOTE: Column names are PascalCase (quoted) to match EF Core convention
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'whatsapp_message_templates' 
        AND column_name = 'CreatedAtUtc'
    ) THEN
        ALTER TABLE whatsapp_message_templates 
        ADD COLUMN "CreatedAtUtc" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW();
        
        RAISE NOTICE 'Added CreatedAtUtc column to whatsapp_message_templates';
    ELSE
        RAISE NOTICE 'CreatedAtUtc column already exists';
    END IF;
END $$;

-- Step 2: Drop existing non-unique index if exists
DROP INDEX IF EXISTS "IX_whatsapp_message_templates_TenantId_Type_Language";

-- Step 3: Create unique constraint (replaces old non-unique index)
-- This prevents race conditions creating duplicate templates
-- NOTE: Column names are PascalCase (quoted) to match EF Core convention
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes 
        WHERE indexname = 'uq_whatsapp_template_tenant_type_language'
    ) THEN
        CREATE UNIQUE INDEX uq_whatsapp_template_tenant_type_language 
        ON whatsapp_message_templates ("TenantId", "Type", "Language");
        
        RAISE NOTICE 'Created unique index uq_whatsapp_template_tenant_type_language';
    ELSE
        RAISE NOTICE 'Unique index already exists';
    END IF;
END $$;

-- Step 4: Verify changes
SELECT 
    column_name, 
    data_type, 
    is_nullable,
    column_default
FROM information_schema.columns 
WHERE table_name = 'whatsapp_message_templates'
ORDER BY ordinal_position;

-- List indexes on the table
SELECT indexname, indexdef 
FROM pg_indexes 
WHERE tablename = 'whatsapp_message_templates';

-- =====================================================================
-- End of Migration
-- =====================================================================
