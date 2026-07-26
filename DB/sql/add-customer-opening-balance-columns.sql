ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "OpeningBalanceAmount" numeric NULL;
ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "BalanceType" text NULL;
ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "OpeningBalanceDate" timestamp with time zone NULL;
ALTER TABLE "CustomerTransactions" ADD COLUMN IF NOT EXISTS "IsOpeningBalance" boolean NOT NULL DEFAULT false;
