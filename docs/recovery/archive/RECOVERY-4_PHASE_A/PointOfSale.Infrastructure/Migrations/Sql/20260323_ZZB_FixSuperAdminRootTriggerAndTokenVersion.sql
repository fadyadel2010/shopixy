-- Migration: Fix Super Admin Root User Trigger + Add TokenVersion
-- Date: 2026-03-23
-- Fixes: C-4 (root user trigger blocking login) + C-3 (token replay detection)
--
-- CRITICAL: The original protect_system_super_admin_mutation() trigger blocked
-- ALL updates to the root user, including PasswordHash, LastLoginAt, and 
-- RefreshTokenHash. This prevented the root user from logging in.
--
-- This migration replaces the trigger to ONLY protect immutable identity fields
-- (Id, Email, IsSystem, CreatedAt) while allowing operational fields
-- (PasswordHash, LastLoginAt, RefreshTokenHash, RefreshTokenExpiresAtUtc, TokenVersion)
-- to be updated normally.

BEGIN;

-- =========================================================================
-- STEP 1: Add TokenVersion column (C-3 replay detection)
-- =========================================================================

ALTER TABLE "SuperAdmins"
    ADD COLUMN IF NOT EXISTS "TokenVersion" integer NOT NULL DEFAULT 0;

-- =========================================================================
-- STEP 2: Replace the root user protection trigger (C-4 fix)
--         ONLY protect identity fields, ALLOW operational updates
-- =========================================================================

CREATE OR REPLACE FUNCTION protect_system_super_admin_mutation()
RETURNS trigger AS $$
BEGIN
    IF OLD."IsSystem" = true THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'System root Super Admin cannot be deleted.';
        END IF;

        -- ONLY protect immutable identity fields
        -- Allow: PasswordHash, LastLoginAt, RefreshTokenHash,
        --        RefreshTokenExpiresAtUtc, TokenVersion
        IF NEW."Id" IS DISTINCT FROM OLD."Id" THEN
            RAISE EXCEPTION 'System root Super Admin Id cannot be changed.';
        END IF;

        IF NEW."Email" IS DISTINCT FROM OLD."Email" THEN
            RAISE EXCEPTION 'System root Super Admin Email cannot be changed.';
        END IF;

        IF NEW."IsSystem" IS DISTINCT FROM OLD."IsSystem" THEN
            RAISE EXCEPTION 'System root Super Admin IsSystem flag cannot be changed.';
        END IF;

        IF NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt" THEN
            RAISE EXCEPTION 'System root Super Admin CreatedAt cannot be changed.';
        END IF;
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

-- Re-create triggers (idempotent via DROP IF EXISTS)
DROP TRIGGER IF EXISTS "TRG_SuperAdmins_ProtectSystemRoot_Update" ON "SuperAdmins";
CREATE TRIGGER "TRG_SuperAdmins_ProtectSystemRoot_Update"
    BEFORE UPDATE ON "SuperAdmins"
    FOR EACH ROW
    EXECUTE FUNCTION protect_system_super_admin_mutation();

DROP TRIGGER IF EXISTS "TRG_SuperAdmins_ProtectSystemRoot_Delete" ON "SuperAdmins";
CREATE TRIGGER "TRG_SuperAdmins_ProtectSystemRoot_Delete"
    BEFORE DELETE ON "SuperAdmins"
    FOR EACH ROW
    EXECUTE FUNCTION protect_system_super_admin_mutation();

COMMIT;
