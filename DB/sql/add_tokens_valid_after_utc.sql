-- Migration: Add TokensValidAfterUtc to OnlineCustomers
-- Purpose: Durable cutoff timestamp for token validation after password reset
-- Phase: Security Hardening - Password Reset Token Revocation

-- Add the column (nullable, defaults to NULL)
ALTER TABLE "OnlineCustomers"
ADD COLUMN "TokensValidAfterUtc" timestamp without time zone NULL;

-- Add index for efficient JWT validation queries
-- The middleware queries by (Id, TenantId) and needs TokensValidAfterUtc
CREATE INDEX "IX_OnlineCustomers_Id_TenantId_TokensValidAfterUtc"
ON "OnlineCustomers" ("Id", "TenantId")
INCLUDE ("TokensValidAfterUtc", "IsActive");

-- Add comment explaining the column's purpose
COMMENT ON COLUMN "OnlineCustomers"."TokensValidAfterUtc" IS 
'Durable cutoff timestamp for token validation. Any access token issued before this timestamp MUST be rejected. Set when password is reset or changed. NULL means no cutoff is enforced.';
