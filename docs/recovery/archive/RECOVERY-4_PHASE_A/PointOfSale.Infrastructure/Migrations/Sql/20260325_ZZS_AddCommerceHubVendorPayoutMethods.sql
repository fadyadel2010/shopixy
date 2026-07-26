BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubVendorPayoutMethods"
(
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "MethodType" integer NOT NULL,
    "DestinationValue" character varying(200) NOT NULL,
    "IsDefault" boolean NOT NULL DEFAULT false,
    "IsVerified" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubVendorPayoutMethods" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt"
    ON "CommerceHubVendorPayoutMethods" ("VendorId", "CreatedAt" DESC);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_DestinationValue"
    ON "CommerceHubVendorPayoutMethods" ("VendorId", "MethodType", "DestinationValue")
    WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPayoutMethods_VendorId_Default"
    ON "CommerceHubVendorPayoutMethods" ("VendorId")
    WHERE "IsDeleted" = false AND "IsDefault" = true;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "PayoutMethodId" uuid NULL;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "DestinationValueSnapshot" character varying(200) NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_PayoutMethodId"
    ON "CommerceHubPayouts" ("PayoutMethodId");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMethodId'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            ADD CONSTRAINT "FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMethodId"
                FOREIGN KEY ("PayoutMethodId")
                REFERENCES "CommerceHubVendorPayoutMethods" ("Id")
                ON DELETE SET NULL;
    END IF;
END $$;

COMMIT;
