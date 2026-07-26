-- Add Weight column to ProductVariants table
-- This allows variants to have distinct weights for accurate shipping calculations
-- If null, the system falls back to Product.Weight

DO $$
BEGIN
    -- Check if ProductVariants table exists and Weight column doesn't exist
    IF EXISTS (
        SELECT 1 
        FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'ProductVariants'
    ) AND NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_schema = 'public' 
        AND table_name = 'ProductVariants' 
        AND column_name = 'Weight'
    ) THEN
        ALTER TABLE "ProductVariants"
        ADD COLUMN "Weight" DECIMAL(18, 3) NULL;
        
        RAISE NOTICE 'Added Weight column to ProductVariants table';
    ELSIF NOT EXISTS (
        SELECT 1 
        FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'ProductVariants'
    ) THEN
        RAISE NOTICE 'ProductVariants table does not exist - skipping Weight column addition';
    ELSE
        RAISE NOTICE 'Weight column already exists in ProductVariants table';
    END IF;
END $$;
