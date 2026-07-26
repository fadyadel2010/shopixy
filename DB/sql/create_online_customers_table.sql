-- =============================================================================
-- Phase 10.3.1 - Online Customer Identity (Core)
-- PostgreSQL Schema Migration for OnlineCustomers Table
-- =============================================================================

-- Create OnlineCustomers table (Write Model)
CREATE TABLE IF NOT EXISTS "OnlineCustomers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(255),
    "Provider" character varying(20) NOT NULL DEFAULT 'Local',
    "ProviderUserId" character varying(255),
    "DisplayName" character varying(200),
    "Phone" character varying(50),
    "IsActive" boolean NOT NULL DEFAULT true,
    "EmailVerified" boolean NOT NULL DEFAULT false,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "LastLoginAtUtc" timestamp with time zone,
    "UpdatedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_OnlineCustomers" PRIMARY KEY ("Id")
);

-- Create unique index on (TenantId, Email) for tenant-scoped email uniqueness
CREATE UNIQUE INDEX IF NOT EXISTS "IX_OnlineCustomers_TenantId_Email" 
    ON "OnlineCustomers" ("TenantId", "Email");

-- Create index on TenantId for tenant-filtered queries
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_TenantId" 
    ON "OnlineCustomers" ("TenantId");

-- Create index on IsActive for filtering active customers
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_IsActive" 
    ON "OnlineCustomers" ("IsActive");

-- Create index on EmailVerified for filtering verified customers
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_EmailVerified" 
    ON "OnlineCustomers" ("EmailVerified");

-- Create index on Provider for filtering by authentication provider
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_Provider" 
    ON "OnlineCustomers" ("Provider");

-- Create index on CreatedAtUtc for sorting by registration date
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_CreatedAtUtc" 
    ON "OnlineCustomers" ("CreatedAtUtc");

-- Create composite index for provider lookups (social login)
CREATE INDEX IF NOT EXISTS "IX_OnlineCustomers_TenantId_Provider_ProviderUserId" 
    ON "OnlineCustomers" ("TenantId", "Provider", "ProviderUserId");

-- Add comment explaining the table purpose
COMMENT ON TABLE "OnlineCustomers" IS 
    'Phase 10.3.1: Storefront customer identity - separate from POS Customers. Write model for CQRS architecture.';

-- =============================================================================
-- MongoDB Collection Indexes (run via mongo shell or application startup)
-- Collection: online_customers
-- =============================================================================
-- db.online_customers.createIndex({ "tenantId": 1, "customerId": 1 }, { unique: true, name: "IX_TenantId_CustomerId" });
-- db.online_customers.createIndex({ "tenantId": 1, "email": 1 }, { name: "IX_TenantId_Email" });
-- db.online_customers.createIndex({ "tenantId": 1 }, { name: "IX_TenantId" });
