-- S80: Add DEFAULT 'EGP' to Tenants.Currency column
-- This aligns the database schema with the EF model configuration that was added in S40.
-- The EF model has HasDefaultValue("EGP") but the migration did not apply it to the database.

-- Add DEFAULT 'EGP' to Tenants.Currency column
ALTER TABLE "Tenants" 
    ALTER COLUMN "Currency" SET DEFAULT 'EGP';
