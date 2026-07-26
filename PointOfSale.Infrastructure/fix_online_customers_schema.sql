-- Fix OnlineCustomers table schema
-- Run this against your PostgreSQL database to add missing columns

-- Add EmailVerifiedAtUtc column if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'OnlineCustomers' AND column_name = 'EmailVerifiedAtUtc') THEN
        ALTER TABLE "OnlineCustomers" ADD COLUMN "EmailVerifiedAtUtc" timestamp with time zone NULL;
    END IF;
END $$;

-- Add NormalizedEmail column if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'OnlineCustomers' AND column_name = 'NormalizedEmail') THEN
        ALTER TABLE "OnlineCustomers" ADD COLUMN "NormalizedEmail" text NOT NULL DEFAULT '';
    END IF;
END $$;

-- Update NormalizedEmail to match Email for existing records
UPDATE "OnlineCustomers" SET "NormalizedEmail" = LOWER(TRIM("Email")) WHERE "NormalizedEmail" = '';
