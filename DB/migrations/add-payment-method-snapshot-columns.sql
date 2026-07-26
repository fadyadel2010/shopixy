-- ================================================
-- PHASE 10.4.D3: COD Payment Integration Migration
-- ================================================
-- Adds payment method snapshot fields to Orders table
-- These fields store immutable snapshots from TenantPaymentMethod at order creation
-- PostgreSQL Syntax

-- Add PaymentMethodCode column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'PaymentMethodCode'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "PaymentMethodCode" VARCHAR(50) NOT NULL DEFAULT 'COD';
        RAISE NOTICE 'Added PaymentMethodCode column to Orders table';
    END IF;
END $$;

-- Add PaymentMethodName column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'PaymentMethodName'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "PaymentMethodName" VARCHAR(255) NULL;
        RAISE NOTICE 'Added PaymentMethodName column to Orders table';
    END IF;
END $$;

-- Add PaymentFee column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'PaymentFee'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "PaymentFee" DECIMAL(18,2) NOT NULL DEFAULT 0;
        RAISE NOTICE 'Added PaymentFee column to Orders table';
    END IF;
END $$;

-- Add IsPaid column
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Orders' AND column_name = 'IsPaid'
    ) THEN
        ALTER TABLE "Orders" ADD COLUMN "IsPaid" BOOLEAN NOT NULL DEFAULT FALSE;
        RAISE NOTICE 'Added IsPaid column to Orders table';
    END IF;
END $$;

-- Verify columns were added
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns 
WHERE table_name = 'Orders' 
AND column_name IN ('PaymentMethodCode', 'PaymentMethodName', 'PaymentFee', 'IsPaid')
ORDER BY column_name;

-- Log completion
DO $$ BEGIN RAISE NOTICE 'PHASE 10.4.D3: COD Payment Integration Migration completed successfully'; END $$;
