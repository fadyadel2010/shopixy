-- Quick fix: Add PreviewImageUrl column to ThemePresets table
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'ThemePresets' AND column_name = 'PreviewImageUrl') THEN
        ALTER TABLE "ThemePresets" ADD COLUMN "PreviewImageUrl" VARCHAR(500);
    END IF;
END $$;

-- Verify
SELECT column_name FROM information_schema.columns WHERE table_name = 'ThemePresets';
