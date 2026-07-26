BEGIN;

CREATE TABLE IF NOT EXISTS "SuperAdmins"
(
    "Id" uuid NOT NULL,
    "Email" character varying(320) NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "IsSystem" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastLoginAt" timestamp with time zone NULL,
    CONSTRAINT "PK_SuperAdmins" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SuperAdmins_Email"
    ON "SuperAdmins" ("Email");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SuperAdmins_SingleSystemRoot"
    ON "SuperAdmins" ("IsSystem")
    WHERE "IsSystem" = true;

CREATE OR REPLACE FUNCTION protect_system_super_admin_mutation()
RETURNS trigger AS $$
BEGIN
    IF OLD."IsSystem" = true THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'System root Super Admin cannot be deleted.';
        END IF;

        IF NEW."Id" IS DISTINCT FROM OLD."Id"
            OR NEW."Email" IS DISTINCT FROM OLD."Email"
            OR NEW."PasswordHash" IS DISTINCT FROM OLD."PasswordHash"
            OR NEW."IsSystem" IS DISTINCT FROM OLD."IsSystem"
            OR NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt" THEN
            RAISE EXCEPTION 'System root Super Admin cannot be modified.';
        END IF;
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION enforce_super_admin_presence()
RETURNS trigger AS $$
BEGIN
    IF (SELECT COUNT(1) FROM "SuperAdmins") <= 1 THEN
        RAISE EXCEPTION 'At least one Super Admin must always exist.';
    END IF;

    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

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

DROP TRIGGER IF EXISTS "TRG_SuperAdmins_EnforcePresence_Delete" ON "SuperAdmins";
CREATE TRIGGER "TRG_SuperAdmins_EnforcePresence_Delete"
    BEFORE DELETE ON "SuperAdmins"
    FOR EACH ROW
    EXECUTE FUNCTION enforce_super_admin_presence();

COMMIT;
