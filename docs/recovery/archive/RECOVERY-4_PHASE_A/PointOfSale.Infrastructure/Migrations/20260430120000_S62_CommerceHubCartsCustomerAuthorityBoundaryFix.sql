-- Migration: S62_CommerceHubCartsCustomerAuthorityBoundaryFix
-- Date: 2026-04-30 12:00:00
-- Description: Enforce CommerceHub cart customer authority by mirroring the legacy OnlineCustomerId column to CommerceHubCustomerId

-- Repair legacy rows before enforcing the authority boundary. CommerceHubCustomerId is the
-- business owner; OnlineCustomerId is retained only to satisfy legacy non-null/index shape.
ALTER TABLE "CommerceHubCarts"
    ADD COLUMN IF NOT EXISTS "CommerceHubCustomerId" uuid NULL;

UPDATE "CommerceHubCarts"
SET "CommerceHubCustomerId" = "OnlineCustomerId"
WHERE ("CommerceHubCustomerId" IS NULL OR "CommerceHubCustomerId" = '00000000-0000-0000-0000-000000000000')
  AND "OnlineCustomerId" IS NOT NULL
  AND "OnlineCustomerId" <> '00000000-0000-0000-0000-000000000000';

UPDATE "CommerceHubCarts"
SET "OnlineCustomerId" = "CommerceHubCustomerId"
WHERE "CommerceHubCustomerId" IS NOT NULL
  AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000'
  AND "OnlineCustomerId" <> "CommerceHubCustomerId";

ALTER TABLE "CommerceHubCarts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubCarts_CustomerAuthorityBoundary";

ALTER TABLE "CommerceHubCarts"
    ADD CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary"
    CHECK (
        "CommerceHubCustomerId" IS NOT NULL 
        AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000' 
        AND "OnlineCustomerId" IS NOT NULL 
        AND "OnlineCustomerId" <> '00000000-0000-0000-0000-000000000000'
        AND "OnlineCustomerId" = "CommerceHubCustomerId"
    );
