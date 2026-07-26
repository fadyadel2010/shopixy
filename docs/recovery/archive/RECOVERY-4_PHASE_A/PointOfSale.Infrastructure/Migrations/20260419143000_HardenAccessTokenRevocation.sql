BEGIN;

CREATE TABLE IF NOT EXISTS "RevokedCustomerAccessTokens" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "Jti" character varying(450) NOT NULL,
    "CustomerId" uuid NOT NULL,
    "RevokedAt" timestamp without time zone NOT NULL,
    "ExpiresAt" timestamp without time zone NOT NULL,
    "Reason" character varying(100) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_RevokedCustomerAccessTokens_Jti"
    ON "RevokedCustomerAccessTokens" ("Jti");

CREATE INDEX IF NOT EXISTS "IX_RevokedCustomerAccessTokens_CustomerId"
    ON "RevokedCustomerAccessTokens" ("CustomerId");

CREATE INDEX IF NOT EXISTS "IX_RevokedCustomerAccessTokens_ExpiresAt"
    ON "RevokedCustomerAccessTokens" ("ExpiresAt");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId'
    ) THEN
        ALTER TABLE "RevokedCustomerAccessTokens"
            ADD CONSTRAINT "FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId"
            FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers"("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

ALTER TABLE "OnlineCustomerRefreshTokens"
    ADD COLUMN IF NOT EXISTS "CurrentAccessTokenJti" character varying(128) NULL;

ALTER TABLE "OnlineCustomerRefreshTokens"
    ADD COLUMN IF NOT EXISTS "CurrentAccessTokenExpiresAtUtc" timestamp without time zone NULL;

CREATE INDEX IF NOT EXISTS "IX_RefreshToken_TenantId_CurrentAccessTokenJti"
    ON "OnlineCustomerRefreshTokens" ("TenantId", "CurrentAccessTokenJti");

CREATE TABLE IF NOT EXISTS "RevokedStorefrontAccessTokens" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NULL,
    "Jti" character varying(128) NOT NULL,
    "RevokedAtUtc" timestamp without time zone NOT NULL,
    "ExpiresAtUtc" timestamp without time zone NOT NULL,
    "Reason" character varying(256) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_RevokedStorefrontAccessTokens_TenantId_Jti"
    ON "RevokedStorefrontAccessTokens" ("TenantId", "Jti");

CREATE INDEX IF NOT EXISTS "IX_RevokedStorefrontAccessTokens_ExpiresAtUtc"
    ON "RevokedStorefrontAccessTokens" ("ExpiresAtUtc");

CREATE INDEX IF NOT EXISTS "IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId"
    ON "RevokedStorefrontAccessTokens" ("TenantId", "OnlineCustomerId");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustomerId'
    ) THEN
        ALTER TABLE "RevokedStorefrontAccessTokens"
            ADD CONSTRAINT "FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustomerId"
            FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers"("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

COMMIT;

-- Optional rollback:
-- DROP TABLE IF EXISTS "RevokedStorefrontAccessTokens";
-- ALTER TABLE "OnlineCustomerRefreshTokens" DROP COLUMN IF EXISTS "CurrentAccessTokenExpiresAtUtc";
-- ALTER TABLE "OnlineCustomerRefreshTokens" DROP COLUMN IF EXISTS "CurrentAccessTokenJti";
