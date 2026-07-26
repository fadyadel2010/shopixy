        -- Migration: Add RevokedCustomerAccessTokens table for server-side token revocation
        -- Purpose: Track revoked CommerceHub customer access tokens to prevent replay attacks after logout
        -- Date: 2026-04-19

        -- Create the RevokedCustomerAccessTokens table
        CREATE TABLE IF NOT EXISTS "RevokedCustomerAccessTokens" (
            "Id" UUID NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
            "Jti" VARCHAR(450) NOT NULL,
            "CustomerId" UUID NOT NULL,
            "RevokedAt" TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
            "ExpiresAt" TIMESTAMP NOT NULL,
            "Reason" VARCHAR(100) NULL,
            
            -- Unique constraint for fast lookup during token validation
            CONSTRAINT "IX_RevokedCustomerAccessTokens_Jti" UNIQUE ("Jti")
        );

        -- Index for cleanup queries and customer-specific lookups
        CREATE INDEX IF NOT EXISTS "IX_RevokedCustomerAccessTokens_CustomerId" 
        ON "RevokedCustomerAccessTokens" ("CustomerId");

        CREATE INDEX IF NOT EXISTS "IX_RevokedCustomerAccessTokens_ExpiresAt" 
        ON "RevokedCustomerAccessTokens" ("ExpiresAt");

        -- Add foreign key to CommerceHubCustomers
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint 
                WHERE conname = 'FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId'
            ) THEN
                ALTER TABLE "RevokedCustomerAccessTokens"
                ADD CONSTRAINT "FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId"
                FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers"("Id")
                ON DELETE CASCADE;
            END IF;
        END $$;
