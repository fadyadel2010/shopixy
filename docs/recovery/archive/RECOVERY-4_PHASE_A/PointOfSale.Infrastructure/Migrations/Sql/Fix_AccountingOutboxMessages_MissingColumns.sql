-- ============================================================================
-- Fix: Add Missing BaseEntity Columns to AccountingOutboxMessages
-- Run this in PgAdmin4 to add the missing columns
-- Date: 2026-02-08
-- ============================================================================

DO $$
BEGIN
    -- Add CreatedBy if not exists
    IF NOT EXISTS (
        SELECT FROM information_schema.columns 
        WHERE table_name = 'AccountingOutboxMessages' 
        AND column_name = 'CreatedBy'
    ) THEN
        ALTER TABLE "AccountingOutboxMessages" 
        ADD COLUMN "CreatedBy" uuid NULL;
        RAISE NOTICE 'Added column CreatedBy';
    ELSE
        RAISE NOTICE 'Column CreatedBy already exists';
    END IF;
    
    -- Add UpdatedBy if not exists
    IF NOT EXISTS (
        SELECT FROM information_schema.columns 
        WHERE table_name = 'AccountingOutboxMessages' 
        AND column_name = 'UpdatedBy'
    ) THEN
        ALTER TABLE "AccountingOutboxMessages" 
        ADD COLUMN "UpdatedBy" uuid NULL;
        RAISE NOTICE 'Added column UpdatedBy';
    ELSE
        RAISE NOTICE 'Column UpdatedBy already exists';
    END IF;
    
    -- Add IsDeleted if not exists
    IF NOT EXISTS (
        SELECT FROM information_schema.columns 
        WHERE table_name = 'AccountingOutboxMessages' 
        AND column_name = 'IsDeleted'
    ) THEN
        ALTER TABLE "AccountingOutboxMessages" 
        ADD COLUMN "IsDeleted" boolean NOT NULL DEFAULT false;
        RAISE NOTICE 'Added column IsDeleted';
    ELSE
        RAISE NOTICE 'Column IsDeleted already exists';
    END IF;
    
    -- Add CreatedAt if not exists
    IF NOT EXISTS (
        SELECT FROM information_schema.columns 
        WHERE table_name = 'AccountingOutboxMessages' 
        AND column_name = 'CreatedAt'
    ) THEN
        ALTER TABLE "AccountingOutboxMessages" 
        ADD COLUMN "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
        RAISE NOTICE 'Added column CreatedAt';
    ELSE
        RAISE NOTICE 'Column CreatedAt already exists';
    END IF;
    
    -- Add UpdatedAt if not exists (NULLABLE)
    IF NOT EXISTS (
        SELECT FROM information_schema.columns 
        WHERE table_name = 'AccountingOutboxMessages' 
        AND column_name = 'UpdatedAt'
    ) THEN
        ALTER TABLE "AccountingOutboxMessages" 
        ADD COLUMN "UpdatedAt" timestamp with time zone NULL;
        RAISE NOTICE 'Added column UpdatedAt';
    ELSE
        RAISE NOTICE 'Column UpdatedAt already exists';
    END IF;
END $$;

-- ============================================================================
-- Fix: Ensure UpdatedAt is NULLABLE (in case it was created as NOT NULL)
-- ============================================================================
ALTER TABLE "AccountingOutboxMessages" ALTER COLUMN "UpdatedAt" DROP NOT NULL;

-- Verify the columns exist
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'AccountingOutboxMessages'
ORDER BY ordinal_position;
