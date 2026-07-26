BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubCustomerPayoutDetails"
(
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Method" integer NOT NULL,
    "AccountIdentifier" character varying(500) NOT NULL,
    "AccountHolderName" character varying(200) NULL,
    "IsVerified" boolean NOT NULL DEFAULT FALSE,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_CommerceHubCustomerPayoutDetails" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubCustomerPayoutDetails_CommerceHubCustomers_CustomerId"
        FOREIGN KEY ("CustomerId")
        REFERENCES "CommerceHubCustomers" ("Id")
        ON DELETE RESTRICT
);

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "PayoutMethodSnapshot" integer NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "PayoutAccountSnapshot" character varying(500) NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "PayoutAccountMaskedSnapshot" character varying(32) NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "PayoutAccountHolderNameSnapshot" character varying(200) NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt"
    ON "CommerceHubCustomerPayoutDetails" ("CustomerId", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt"
    ON "CommerceHubCustomerPayoutDetails" ("CustomerId", "Method", "CreatedAt" DESC);

COMMIT;
